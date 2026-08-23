/**
 * 后端 UI 状态快照与运行时事件 DTO
 *
 * 与 Nori.Desktop Runtime/AppRuntime.BuildSnapshot 及事件载荷一一对应。
 * 前端不再持有业务真相: 这里全部是只读投影。
 */

// ===================================================================
// 快照
// ===================================================================

/** 应用信息 */
export interface AppInfo {
	appVersion: string
	platform: string
}

/** 通用设置 */
export interface GeneralState {
	language: string
	petAutoSummon: boolean
	/** 主界面侧边栏是否折叠 */
	sidebarCollapsed: boolean
}

/** 桌宠窗口状态 (宿主显隐的唯一真相, 托盘切换后同样同步) */
export interface PetState {
	visible: boolean
}

/** 运行会话类型 (Linux 下区分 x11 / wayland) */
export type SessionType = "windows" | "macos" | "x11" | "wayland" | "unknown"

/**
 * 平台能力
 *
 * 前端一切与平台相关的 UI 都由这些标志驱动: 不支持就明确禁用并给出说明,
 * 不再靠 try/catch 静默吞掉 PlatformNotSupportedException。
 */
export interface PlatformState {
	os: "windows" | "macos" | "linux" | "unknown"
	sessionType: SessionType
	/** 能否读取窗口外的全局光标 (眼神跟随) */
	supportsGlobalCursor: boolean
	/** 能否从 HTML 标题栏发起原生窗口拖动 */
	supportsWindowDrag: boolean
	/** 能否做逐像素点击空透 */
	supportsHitThrough: boolean
	/** 能否置顶窗口 */
	supportsTopmost: boolean
	/** 系统托盘是否可用 */
	supportsTray: boolean
}

/** 遥测状态 (不包含 DSN 或任何用户身份) */
export interface TelemetryState {
	enabled: boolean
	available: boolean
}

/** AI 大脑状态 (秘密已脱敏) */
export interface AiState {
	configured: boolean
	provider: string
	baseUrl: string
	model: string
	persona: string
	hasApiKey: boolean
}

/** 模型目录条目 */
export interface ModelItem {
	id: string
	installed: boolean
}

/** 模型目录状态 */
export interface ModelsState {
	selected: string
	items: ModelItem[]
	scale: number
	expressions: string[]
}

/** Live2D 行为开关 */
export interface BehaviorsState {
	clickInteraction: boolean
	autoBlink: boolean
	eyeTracking: boolean
	idleEyeAnimation: boolean
	idleAnimation: boolean
	expressionEnabled: boolean
	lipSync: boolean
	shadow: boolean
	beatSync: boolean
	renderScale: number
	maxFps: number
}

/** 语音配置 (秘密已脱敏) */
export interface VoiceState {
	volume: number
	ttsProvider: string
	ttsBaseUrl: string
	hasTtsApiKey: boolean
	ttsVoice: string
	ttsSpeed: number
	ttsAutoPlay: boolean
	gptsovitsBaseUrl: string
	gptsovitsRefAudio: string
	gptsovitsPromptText: string
	gptsovitsPromptLang: string
	sttProvider: string
	sttBaseUrl: string
	hasSttApiKey: boolean
	noticePending: boolean
	speaking: boolean
}

/** Embedding 配置 (秘密已脱敏) */
export interface EmbeddingState {
	model: string
	baseUrl: string
	dimensions: string
	hasApiKey: boolean
}

/** 提醒事项 */
export interface ReminderDto {
	id: string
	content: string
	triggerTime: number
}

/** 主动交互状态 */
export interface ProactiveState {
	idleEnabled: boolean
	idleMinutes: number
	dailyGreeting: boolean
	reminders: ReminderDto[]
}

/** 技能条目 (指令正文按需 skills_export 获取) */
export interface SkillDto {
	id: string
	name: string
	description: string
	author: string
	version: string
	icon: string
	tags: string[]
	category: string
	instructions: string
	enabled: boolean
	source: "builtin" | "market" | "custom" | "url"
}

/** 工具条目 */
export interface ToolDto {
	name: string
	description: string
	permissionLevel: "safe" | "confirm" | "dangerous"
	category: "builtin" | "mcp" | "custom"
	enabled: boolean
}

/** 情绪状态 */
export interface EmotionDto {
	type: string
}

/** UI 状态快照 */
export interface UiSnapshot {
	version: number
	app: AppInfo
	general: GeneralState
	telemetry: TelemetryState
	ai: AiState
	models: ModelsState
	pet: PetState
	platform: PlatformState
	behaviors: BehaviorsState
	voice: VoiceState
	embedding: EmbeddingState
	proactive: ProactiveState
	skills: SkillDto[]
	enabledSkillsCount: number
	tools: ToolDto[]
	mcpServersCount?: number
	emotion: EmotionDto
}

// ===================================================================
// Agent 事件
// ===================================================================

/** Agent 运行状态 (与后端 AgentRunState 对齐) */
export type AgentState =
	| "idle"
	| "thinking"
	| "streaming"
	| "tool_executing"
	| "waiting_approval"
	| "speaking"
	| "error"

/** LLM 用量指标 */
export interface UsageMetrics {
	promptTokens: number
	completionTokens: number
	totalTokens: number
	cachedTokens: number
	cacheHitRate: number
	durationMs: number
	model?: string
}

/** 工具授权请求 */
export interface ApprovalRequestDto {
	type: "approval-request"
	sessionId: string | null
	requestId: string
	toolName: string
	arguments?: Record<string, unknown>
	description?: string
	permissionLevel: "confirm" | "dangerous"
	category?: string
}

/** Agent 事件联合载荷 */
export type AgentEventPayload =
	| {type: "state"; sessionId?: string | null; state: AgentState}
	| {type: "chunk"; sessionId: string; chunk: string}
	| {type: "tool-executing"; sessionId: string; toolName: string; arguments?: Record<string, unknown>}
	| {type: "tool-executed"; sessionId: string; toolName: string; result?: unknown; error?: string}
	| ({type: "usage"; sessionId: string} & UsageMetrics)
	| {type: "complete"; sessionId: string; message: {text: string; emotion?: string; expression?: string; action?: string}}
	| {type: "cancelled"; sessionId: string}
	| {type: "error"; sessionId: string; error: string}
	| ApprovalRequestDto
	| {type: "approval-result"; sessionId: string; requestId: string; approved: boolean; reason: string}

// ===================================================================
// 聊天历史 (服务端已规范化)
// ===================================================================

/** 聊天历史消息 */
export interface HistoryMessage {
	id: number
	role: "user" | "assistant"
	content: string
	createdAt: string
}

// ===================================================================
// 领域对象
// ===================================================================

/** 记忆条目 */
export interface MemoryItem {
	id: number
	type: string
	content: string
	importance: number
	source: string
	tags?: string
	createdAt: string
	updatedAt: string
}

/** MCP 服务器状态 */
export interface McpServerStatusInfo {
	serverId: string
	name: string
	status: "disconnected" | "connecting" | "connected" | "error"
	errorMessage?: string
	tools: {
		name: string
		description?: string
		inputSchema?: Record<string, unknown>
	}[]
	resources?: unknown[]
}

/** 模型元数据 (model_get_meta) */
export interface ModelMeta {
	modelId: string
	scale: number
	expressions: string[]
	motions: {group: string; names: string[]}[]
}
