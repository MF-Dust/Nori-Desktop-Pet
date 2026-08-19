import {invoke} from "../host/invoke"
import {listen, type UnlistenFn} from "../host/event"
import {createLive2D} from "../live2d"
import type {AgentProtocolItem, AgentState, AgentTextMessage, AgentToolCall} from "./protocol"
import {StreamingJsonParser} from "./jsonParser"
import {buildAgentSystemPrompt, type PromptBuildOptions} from "./promptBuilder"
import {toolManager} from "./tools"

/**
 * 历史消息格式
 */
export interface HistoryMessage {
	role: "user" | "assistant"
	content: string
}

/**
 * Agent 回调监听
 */
export interface AgentRunCallbacks {
	onStateChange?: (state: AgentState) => void
	onTextChunk?: (chunk: string) => void
	onToolExecuting?: (toolName: string, args: Record<string, unknown>) => void
	onToolExecuted?: (toolName: string, result: unknown, error?: string) => void
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

	/**
	 * 获取当前状态
	 */
	public getState(): AgentState {
		return this.state
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
		this.setState("thinking", callbacks)

		// 复制历史消息并在末尾加入当前用户输入
		const WORKING_HISTORY: HistoryMessage[] = [...history, {role: "user", content: userText}]
		let iterations = 0
		let finalMessage: AgentTextMessage = {type: "message", text: ""}

		try {
			while (iterations < this.maxToolIterations) {
				iterations++

				// 1. 读取 AI 配置
				const [PROVIDER, BASE_URL, API_KEY, MODEL] = await Promise.all([
					invoke<string | null>("get_config", {key: "llm_provider"}),
					invoke<string | null>("get_config", {key: "llm_api_base"}),
					invoke<string | null>("get_config", {key: "llm_api_key"}),
					invoke<string | null>("get_config", {key: "llm_model"}),
				])

				if (!BASE_URL || !API_KEY || !MODEL) {
					throw new Error("尚未配置完整的 LLM 参数 (API Base, API Key 或 Model 缺失)")
				}

				// 2. 组装 System Prompt 与可用工具
				const SYSTEM_PROMPT = buildAgentSystemPrompt(options)

				// 3. 准备流式接收
				const STREAM_ID = `agent-${Date.now()}-${iterations}`
				const PARSER = new StreamingJsonParser()
				let rawResponseText = ""

				if (this.unlistenChunk) {
					this.unlistenChunk()
					this.unlistenChunk = null
				}

				this.unlistenChunk = await listen("nori:chat-chunk", (event) => {
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

				// 4. 调用后端大模型
				try {
					rawResponseText = await invoke<string>("chat_completion_stream", {
						provider: PROVIDER || "openai",
						baseUrl: BASE_URL,
						apiKey: API_KEY,
						model: MODEL,
						messages: [
							{role: "system", content: SYSTEM_PROMPT},
							...WORKING_HISTORY,
						],
						streamId: STREAM_ID,
					})
				} finally {
					if (this.unlistenChunk) {
						this.unlistenChunk()
						this.unlistenChunk = null
					}
				}

				// 5. 解析全部返回对象
				const ITEMS: AgentProtocolItem[] = StreamingJsonParser.parseComplete(rawResponseText)
				let hasToolCall = false

				for (const item of ITEMS) {
					// A. 处理普通消息
					if (item.type === "message") {
						finalMessage = item
						await this.dispatchEffects(item)
					}

					// B. 处理工具调用
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
						break // 进入下一轮推理
					}
				}

				// 若本轮没有触发新的工具调用，说明已产出最终回复，跳出循环
				if (!hasToolCall) {
					break
				}
			}

			this.setState("idle", callbacks)
			if (callbacks?.onComplete) {
				callbacks.onComplete(finalMessage)
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
		const L2D = createLive2D()

		// 触发表情
		if (msg.expression) {
			try {
				await L2D.playExpression(msg.expression)
			} catch {
				/* 表情未匹配时忽略 */
			}
		}

		// 触发动作
		if (msg.action) {
			try {
				await L2D.playMotionByName(msg.action)
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
