import {invoke} from "../host/invoke"
import {listen, type UnlistenFn} from "../host/event"
import {petLive2DController} from "../live2d"
import type {AgentProtocolItem, AgentState, AgentTextMessage, AgentToolCall, EmotionType} from "./protocol"
import {StreamingJsonParser} from "./jsonParser"
import {buildAgentSystemPrompt, type PromptBuildOptions} from "./promptBuilder"
import {toolManager} from "./tools"
import {mcpService} from "../mcp"
import {memoryService} from "../memory"
import {emotionManager} from "../emotion"
import {ttsService} from "../tts"

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
}

/**
 * Agent 引擎
 */
export class AgentEngine {
	private state: AgentState = "idle"
	private maxToolIterations = 5
	private unlistenChunk: UnlistenFn | null = null
	private unlistenUsage: UnlistenFn | null = null
	private mcpSynced = false
	private isAborted = false
	public lastUsage: LlmUsageMetrics | null = null

	/**
	 * 获取当前状态
	 */
	public getState(): AgentState {
		return this.state
	}

	/**
	 * 中止当前 Agent 生成回路
	 */
	public abort(): void {
		this.isAborted = true
		if (this.unlistenChunk) {
			this.unlistenChunk()
			this.unlistenChunk = null
		}
		if (this.unlistenUsage) {
			this.unlistenUsage()
			this.unlistenUsage = null
		}
		ttsService.stop()
		this.state = "idle"
	}

	/**
	 * 设置状态并通知
	 */
	private setState(state: AgentState, callbacks?: AgentRunCallbacks): void {
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
		this.isAborted = false
		this.setState("thinking", callbacks)

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
				if (this.isAborted) break
				iterations++

				// 1. 读取 AI 与用户自定义人设配置
				const [PROVIDER, BASE_URL, API_KEY, MODEL, USER_PERSONA] = await Promise.all([
					invoke<string | null>("get_config", {key: "llm_provider"}),
					invoke<string | null>("get_config", {key: "llm_api_base"}),
					invoke<string | null>("get_config", {key: "llm_api_key"}),
					invoke<string | null>("get_config", {key: "llm_model"}),
					invoke<string | null>("get_config", {key: "nori_user_persona"}),
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

				// 3. 准备流式接收
				const STREAM_ID = `agent-${Date.now()}-${iterations}`
				const PARSER = new StreamingJsonParser()
				let rawResponseText = ""

				if (this.unlistenChunk) {
					this.unlistenChunk()
					this.unlistenChunk = null
				}
				if (this.unlistenUsage) {
					this.unlistenUsage()
					this.unlistenUsage = null
				}

				this.unlistenChunk = await listen("nori:chat-chunk", (event) => {
					if (this.isAborted) return
					const PAYLOAD = event.payload as {streamId: string; chunk: string}
					if (PAYLOAD.streamId === STREAM_ID && PAYLOAD.chunk) {
						this.setState("streaming", callbacks)
						const ITEMS = PARSER.push(PAYLOAD.chunk)
						for (const item of ITEMS) {
							if (item.type === "message" && item.text) {
								if (callbacks?.onTextChunk) callbacks.onTextChunk(item.text)
							}
						}
					}
				})

				this.unlistenUsage = await listen("nori:chat-usage", (event) => {
					if (this.isAborted) return
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
				})

				// 4. 调用后端大模型
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
						persist: false,
					})
				} finally {
					if (this.unlistenChunk) {
						this.unlistenChunk()
						this.unlistenChunk = null
					}
					if (this.unlistenUsage) {
						this.unlistenUsage()
						this.unlistenUsage = null
					}
				}

				if (this.isAborted) break

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
						this.setState("tool_executing", callbacks)
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

						this.setState("thinking", callbacks)
					}
				}

				// 若本轮没有触发新的工具调用，说明已产出最终回复，跳出循环
				if (!hasToolCall) {
					break
				}
			}

			if (this.isAborted) {
				this.setState("idle", callbacks)
				return finalMessage
			}

			this.setState("idle", callbacks)
			if (callbacks?.onComplete) {
				callbacks.onComplete(finalMessage)
			}

			// 自动朗读回复 (朗读期间进入 speaking 状态)
			if (finalMessage.text) {
				try {
					const AUTO_TTS = await invoke<string | null>("get_config", {key: "tts_auto_play"})
					if (AUTO_TTS === "true" || AUTO_TTS === "1") {
						this.setState("speaking", callbacks)
						await ttsService.speak(finalMessage.text)
						this.setState("idle", callbacks)
					}
				} catch {
					this.setState("idle", callbacks)
					/* 忽略自动朗读异常 */
				}
			}

			return finalMessage
		} catch (error) {
			this.setState("error", callbacks)
			const ERR = error instanceof Error ? error : new Error(String(error))
			if (callbacks?.onError) {
				callbacks.onError(ERR)
			}
			throw ERR
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
