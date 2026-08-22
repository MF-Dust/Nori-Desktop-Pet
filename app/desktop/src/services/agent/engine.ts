import {invoke} from "../host/invoke"
import {listen} from "../host/event"
import {petLive2DController} from "../live2d"
import type {AgentProtocolItem, AgentState, AgentTextMessage, AgentToolCall, EmotionType, ToolRequestApproval} from "./protocol"
import {StreamingJsonParser} from "./jsonParser"
import {buildAgentSystemPrompt, type PromptBuildOptions} from "./promptBuilder"
import {toolManager} from "./tools"
import {AgentRunSession} from "./AgentRunSession"
import {mcpService} from "../mcp"
import {memoryService} from "../memory"
import {emotionManager} from "../emotion"
import {ttsService} from "../tts"
import {readBooleanConfig, readStringConfig} from "../config"

/**
 * 历史消息格式
 */
export interface HistoryMessage {
	role: "user" | "assistant"
	content: string
}

/**
 * LLM 用量与缓存命中指标
 */
export interface LlmUsageMetrics {
	promptTokens: number
	completionTokens: number
	totalTokens: number
	cachedTokens: number
	cacheHitRate: number
	durationMs: number
	model?: string
}

/**
 * Agent 回调监听
 */
export interface AgentRunCallbacks {
	onStateChange?: (state: AgentState) => void
	onTextChunk?: (chunk: string) => void
	onToolExecuting?: (toolName: string, args: Record<string, unknown>) => void
	onToolExecuted?: (toolName: string, result: unknown, error?: string) => void
	onUsage?: (usage: LlmUsageMetrics) => void
	onComplete?: (finalMessage: AgentTextMessage) => void
	onError?: (error: Error) => void
	/** 逐调用工具授权; confirm/dangerous 工具执行前必须经用户批准 */
	requestToolApproval?: ToolRequestApproval
}

/**
 * Agent 引擎
 */
export class AgentEngine {
	private state: AgentState = "idle"
	private maxToolIterations = 5
	private mcpSynced = false
	/** 当前活动会话; 旧会话只能清理自己, 不能改写全局状态 */
	private activeSession: AgentRunSession | null = null
	public lastUsage: LlmUsageMetrics | null = null

	/**
	 * 获取当前状态
	 */
	public getState(): AgentState {
		return this.state
	}

	/**
	 * 中止当前 Agent 会话: 先取消本地信号/监听/朗读, 再 best-effort 取消宿主操作
	 */
	public abort(): void {
		const SESSION = this.activeSession
		if (!SESSION || SESSION.isCancelled) return
		SESSION.cancel()
		ttsService.stop()
		if (this.activeSession === SESSION) {
			this.state = "idle"
		}
		// best-effort: 宿主端聊天流/MCP 调用同步取消
		void invoke("cancel_agent_session", {sessionId: SESSION.id}).catch(() => {
			/* 宿主不可用时忽略, 本地监听已清理 */
		})
	}

	/**
	 * 仅允许仍处活动状态的会话改写全局状态
	 */
	private setState(state: AgentState, session: AgentRunSession | null, callbacks?: AgentRunCallbacks): void {
		if (session && this.activeSession !== session) return
		this.state = state
		if (callbacks?.onStateChange) {
			callbacks.onStateChange(state)
		}
	}

	/**
	 * 执行一次 Agent 对话回路
	 */
	public async run(
		userText: string,
		history: HistoryMessage[],
		options: PromptBuildOptions = {},
		callbacks?: AgentRunCallbacks
	): Promise<AgentTextMessage> {
		const SESSION = new AgentRunSession()
		this.activeSession = SESSION
		this.setState("thinking", SESSION, callbacks)

		// 首次运行异步同步一次已连接的 MCP 工具
		if (!this.mcpSynced) {
			this.mcpSynced = true
			void mcpService.syncToolsWithToolManager()
		}

		// 复制历史消息并在末尾加入当前用户输入
		const WORKING_HISTORY: HistoryMessage[] = [...history, {role: "user", content: userText}]
		let iterations = 0
		let finalMessage: AgentTextMessage = {type: "message", text: ""}

		try {
			while (iterations < this.maxToolIterations) {
				if (SESSION.isCancelled) break
				iterations++

				// 1. 读取 AI 与用户自定义人设配置
				const [PROVIDER, BASE_URL, API_KEY, MODEL, USER_PERSONA] = await Promise.all([
					readStringConfig("llm_provider", "openai"),
					readStringConfig("llm_api_base", ""),
					readStringConfig("llm_api_key", ""),
					readStringConfig("llm_model", ""),
					readStringConfig("nori_user_persona", ""),
				])

				if (!BASE_URL || !API_KEY || !MODEL) {
					throw new Error("尚未配置完整的 LLM 参数 (API Base, API Key 或 Model 缺失)")
				}

				// 2. 组装 System Prompt 与可用工具 (自动检索相关记忆与关联情绪)
				const RELEVANT_MEMORIES = options.memories ?? (await memoryService.getRelevantMemories(userText))
				const CURRENT_EMOTION = options.emotion ?? emotionManager.getState().type
				const RESOLVED_PERSONA = options.persona || USER_PERSONA || undefined

				const PROMPT_OPTIONS: PromptBuildOptions = {
					...options,
					persona: RESOLVED_PERSONA,
					emotion: CURRENT_EMOTION,
					memories: RELEVANT_MEMORIES,
				}
				const SYSTEM_PROMPT = buildAgentSystemPrompt(PROMPT_OPTIONS)

				// 滑动窗口截断长上下文 (保留最近 12 轮，参考 AstrBot context/truncator 机制)
				const MAX_CONTEXT_ROUNDS = 12
				const TRUNCATED_HISTORY = WORKING_HISTORY.length > MAX_CONTEXT_ROUNDS
					? WORKING_HISTORY.slice(-MAX_CONTEXT_ROUNDS)
					: WORKING_HISTORY

				// 3. 准备流式接收: 监听归本 session 所有, 迭代结束只清理本轮自己的监听
				const STREAM_ID = `agent-${Date.now()}-${iterations}`
				SESSION.currentStreamId = STREAM_ID
				const PARSER = new StreamingJsonParser()
				let rawResponseText = ""

				const unlistenChunk = SESSION.addListener(await listen("nori:chat-chunk", (event) => {
					if (SESSION.isCancelled) return
					const PAYLOAD = event.payload as {streamId: string; chunk: string}
					if (PAYLOAD.streamId === STREAM_ID && PAYLOAD.chunk) {
						this.setState("streaming", SESSION, callbacks)
						const ITEMS = PARSER.push(PAYLOAD.chunk)
						for (const item of ITEMS) {
							if (item.type === "message" && item.text) {
								if (callbacks?.onTextChunk) callbacks.onTextChunk(item.text)
							}
						}
					}
				}))

				const unlistenUsage = SESSION.addListener(await listen("nori:chat-usage", (event) => {
					if (SESSION.isCancelled) return
					const PAYLOAD = event.payload as {streamId: string} & LlmUsageMetrics
					if (PAYLOAD.streamId === STREAM_ID) {
						const USAGE: LlmUsageMetrics = {
							promptTokens: PAYLOAD.promptTokens,
							completionTokens: PAYLOAD.completionTokens,
							totalTokens: PAYLOAD.totalTokens,
							cachedTokens: PAYLOAD.cachedTokens,
							cacheHitRate: PAYLOAD.cacheHitRate,
							durationMs: PAYLOAD.durationMs,
							model: PAYLOAD.model || MODEL,
						}
						this.lastUsage = USAGE
						if (callbacks?.onUsage) {
							callbacks.onUsage(USAGE)
						}
					}
				}))

				// 4. 调用后端大模型, 携带 session ID 以便宿主登记可取消操作
				try {
					rawResponseText = await invoke<string>("chat_completion_stream", {
						provider: PROVIDER || "openai",
						baseUrl: BASE_URL,
						apiKey: API_KEY,
						model: MODEL,
						messages: [
							{role: "system", content: SYSTEM_PROMPT},
							...TRUNCATED_HISTORY,
						],
						streamId: STREAM_ID,
						sessionId: SESSION.id,
						persist: false,
					})
				} finally {
					unlistenChunk()
					unlistenUsage()
				}

				if (SESSION.isCancelled) break

				// 5. 解析全部返回对象
				const ITEMS: AgentProtocolItem[] = StreamingJsonParser.parseComplete(rawResponseText)
				let hasToolCall = false

				for (const item of ITEMS) {
					// A. 处理普通消息 (工具调用之后的消息需要等结果反馈，跳过提前定稿)
					if (item.type === "message" && !hasToolCall) {
						finalMessage = item
						await this.dispatchEffects(item)
					}

					// B. 处理工具调用 (同一轮可执行多个工具，全部并入下一轮推理)
					if (item.type === "tool_call") {
						hasToolCall = true
						const TOOL_CALL = item as AgentToolCall
						this.setState("tool_executing", SESSION, callbacks)
						if (callbacks?.onToolExecuting) {
							callbacks.onToolExecuting(TOOL_CALL.name, TOOL_CALL.arguments)
						}

						// 执行工具
						const EXEC_RES = await toolManager.execute(TOOL_CALL.name, TOOL_CALL.arguments)
						if (callbacks?.onToolExecuted) {
							callbacks.onToolExecuted(TOOL_CALL.name, EXEC_RES.result, EXEC_RES.error)
						}

						// 将工具调用与执行结果追加进上下文历史
						WORKING_HISTORY.push({
							role: "assistant",
							content: JSON.stringify(TOOL_CALL),
						})
						WORKING_HISTORY.push({
							role: "user",
							content: `【系统工具执行反馈 - ${TOOL_CALL.name}】:\n${JSON.stringify({
								id: TOOL_CALL.id,
								name: TOOL_CALL.name,
								result: EXEC_RES.result,
								error: EXEC_RES.error,
							})}`,
						})

						this.setState("thinking", SESSION, callbacks)
					}
				}

				// 若本轮没有触发新的工具调用，说明已产出最终回复，跳出循环
				if (!hasToolCall) {
					break
				}
			}

			if (SESSION.isCancelled) {
				this.setState("idle", SESSION, callbacks)
				return finalMessage
			}

			this.setState("idle", SESSION, callbacks)
			if (callbacks?.onComplete) {
				callbacks.onComplete(finalMessage)
			}

			// 自动朗读回复 (朗读期间进入 speaking 状态); 中止后不再开始新的朗读
			if (finalMessage.text && !SESSION.isCancelled) {
				try {
					const AUTO_TTS = await readBooleanConfig("tts_auto_play", false)
					if (AUTO_TTS && !SESSION.isCancelled) {
						this.setState("speaking", SESSION, callbacks)
						await ttsService.speak(finalMessage.text)
						this.setState("idle", SESSION, callbacks)
					}
				} catch {
					this.setState("idle", SESSION, callbacks)
					/* 忽略自动朗读异常 */
				}
			}

			return finalMessage
		} catch (error) {
			// 宿主取消/本地中止造成的错误视为正常取消, 不进入用户可见错误状态
			if (SESSION.isCancelled) {
				this.setState("idle", SESSION, callbacks)
				return finalMessage
			}
			this.setState("error", SESSION, callbacks)
			const ERR = error instanceof Error ? error : new Error(String(error))
			if (callbacks?.onError) {
				callbacks.onError(ERR)
			}
			throw ERR
		} finally {
			// 会话收尾只清理自己的监听; 新会话已接管时不再改写 activeSession
			SESSION.unlistenAll()
			if (this.activeSession === SESSION) {
				this.activeSession = null
			}
		}
	}

	/**
	 * 分发消息附加的 Live2D 表情、动作与情绪等副作用
	 */
	private async dispatchEffects(msg: AgentTextMessage): Promise<void> {

		// 触发情绪联动
		if (msg.emotion) {
			try {
				emotionManager.setEmotion(msg.emotion as EmotionType)
			} catch {
				/* 忽略未知情绪 */
			}
		}

		// 触发表情
		if (msg.expression) {
			try {
				if (petLive2DController) await petLive2DController.playExpression(msg.expression)
			} catch {
				/* 表情未匹配时忽略 */
			}
		}

		// 触发动作
		if (msg.action) {
			try {
				if (petLive2DController) await petLive2DController.playMotionByName(msg.action)
				// 广播动作给桌宠主窗口
				await invoke("write_log", {level: "info", message: `Agent 消息驱动动作: ${msg.action}`})
			} catch {
				/* 动作未匹配时忽略 */
			}
		}
	}
}

/**
 * 全局 Agent 引擎单例
 */
export const agentEngine = new AgentEngine()
