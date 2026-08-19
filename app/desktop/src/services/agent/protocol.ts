/**
 * Nori Agent 协议定义
 *
 * 遵循 docs/开发任务清单.md 模块 09/10/11 与技术文档草案
 */

/**
 * 情绪类型
 */
export type EmotionType =
	| "neutral"
	| "happy"
	| "sad"
	| "angry"
	| "surprised"
	| "shy"
	| "sleepy"
	| "fond"

/**
 * Agent 文本回复消息 (带情绪、表情、动作联动)
 */
export interface AgentTextMessage {
	type: "message"
	text: string
	emotion?: EmotionType | string
	expression?: string
	action?: string
}

/**
 * Agent 工具调用请求
 */
export interface AgentToolCall {
	type: "tool_call"
	id: string
	name: string
	arguments: Record<string, unknown>
}

/**
 * 工具执行结果
 */
export interface AgentToolResult {
	type: "tool_result"
	id: string
	name: string
	result?: unknown
	error?: string
}

/**
 * 系统与环境事件
 */
export interface AgentSystemEvent {
	type: "event"
	name: string
	payload?: unknown
}

/**
 * Agent 协议联合类型
 */
export type AgentProtocolItem =
	| AgentTextMessage
	| AgentToolCall
	| AgentToolResult
	| AgentSystemEvent

/**
 * Agent 运行状态
 */
export type AgentState = "idle" | "thinking" | "streaming" | "tool_executing" | "speaking" | "error"
