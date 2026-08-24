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
	/** 后端 UiSnapshot.app.appVersion; 与 ProductVersion.Current 对应 */
	appVersion: string
	/** 显式产品版本；旧宿主只提供 appVersion 时前端回退使用它。 */
	productVersion?: string
	platform: string
	debugCrashTestsAvailable: boolean
	/** 是否通过 --safe-mode 启动 */
	safeMode: boolean
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
	consent: "unset" | "granted" | "denied"
	/** 只有 granted 才为 true; unset 时 UI 可视觉开启但远程 SDK 必须关闭 */
	enabled: boolean
	available: boolean
}

/** 敏感配置问题摘要 (只含键名和分类) */
export interface SecretIssueDto {
	key: string
	category: string
	requiresUserAction: boolean
}

/** 聊天 Provider 配置状态 (秘密已脱敏) */
export interface AiChatState {
	configured: boolean
	provider: string
	baseUrl: string
	model: string
	persona: string
	hasApiKey: boolean
}

/** AI 大脑状态 (包含扁平兼容字段与统一 chat/embedding 嵌套结构) */
export interface AiState extends AiChatState {
	chat?: AiChatState
	embedding?: EmbeddingState
}

/** Provider 连接测试结果 (不包含密钥或请求正文) */
export interface ProviderConnectionTestResult {
	success: boolean
	provider: string
	latencyMs: number
	category: string
	message: string
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
	loadError?: string | null
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
	aiInteraction: boolean
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
	configured: boolean
	model: string
	baseUrl: string
	dimensions: string
	hasApiKey: boolean
}

/** 长期记忆设置快照 */
export interface MemorySettings {
	enabled: boolean
	reflectionEnabled: boolean
	reflectionRounds: number
	reflectionMinChars: number
	recallTopK: number
	keywordTopK: number
	vectorTopK: number
	rrfK: number
	minSimilarity: number
	decayEnabled: boolean
	archiveEnabled: boolean
	sourceRetentionThreshold: number
	archiveThreshold: number
	knowledgeEnabled: boolean
	knowledgeWatch: boolean
	debugRetrieval: boolean
}

/** 知识/向量索引状态 */
export interface MemoryIndexStatus {
	state: "ready" | "checking" | "rebuilding" | "partial" | "failed"
	processed: number
	total: number
	lastError?: string
	lastMaintenanceAt?: string
	lastReflectionAt?: string
}

/** 记忆快照 */
export interface MemoryState {
	enabled: boolean
	reflectionEnabled: boolean
	decayEnabled: boolean
	archiveEnabled: boolean
	active: number
	atoms: number
	archived: number
	total: number
	knowledgePath: string
	knowledgeChunks: number
	indexState: MemoryIndexStatus["state"]
	indexProcessed: number
	indexTotal: number
	lastError?: string
	lastReflection?: string
	lastMaintenance?: string
	ftsAvailable: boolean
	reflectionRounds: number
	reflectionMinChars: number
	recallTopK: number
	keywordTopK: number
	vectorTopK: number
	rrfK: number
	minSimilarity: number
	sourceRetentionThreshold: number
	archiveThreshold: number
	knowledgeEnabled: boolean
	knowledgeWatch: boolean
	debugRetrieval: boolean
}

/** 记忆事实原子 */
export interface MemoryAtom {
	id: number
	parentMemoryId: number
	atomType: string
	content: string
	importance: number
	confidence: number
	status: string
	createdAt: string
	lastAccessedAt?: string
	lastReinforcedAt?: string
	ttlDays?: number
	expiresAt?: string
	reinforcementCount: number
	decayType: string
	entities?: string
	supersededBy?: number
}

/** 记忆来源消息 */
export interface MemorySource {
	id: number
	memoryId: number
	role: string
	content: string
	messageTime?: string
	sequence: number
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
	secretIssues: SecretIssueDto[]
	ai: AiState
	models: ModelsState
	pet: PetState
	platform: PlatformState
	behaviors: BehaviorsState
	voice: VoiceState
	embedding: EmbeddingState
	memory: MemoryState
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
	| {type: "tool-executed"; sessionId: string; toolName: string; result?: unknown; success: boolean; error?: string}
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
	embedding?: string
	createdAt: string
	updatedAt: string
	kind?: string
	canonicalSummary?: string
	personaSummary?: string
	confidence?: number
	status?: string
	accessCount?: number
	reinforcementCount?: number
	lastAccessedAt?: string
	lastReinforcedAt?: string
	ttlDays?: number
	expiresAt?: string
	supersededBy?: number
	embeddingFingerprint?: string
}

/** 记忆总览 */
export interface MemoryOverview {
	activeMemories: number
	atomCount: number
	archivedMemories: number
	totalMemories: number
	knowledgeChunks: number
	reflectionCursor?: string
	lastReflection?: string
	lastMaintenance?: string
	index: MemoryIndexStatus
	settings: MemorySettings
}

/** 分页记忆列表 */
export interface MemoryListPage {
	items: MemoryItem[]
	total: number
}

/** Recall Debugger 结果 */
export interface MemoryRecallDebug {
	trace?: {
		query: string
		expandedQuery: string
		keywordHits: {memoryId: number; score: number; rank: number}[]
		vectorHits: {memoryId: number; score: number; rank: number}[]
		atomHits: {memoryId: number; score: number; rank: number}[]
		rrfHits: {memoryId: number; score: number; rank: number}[]
		filteredIds: number[]
		injectedIds: number[]
	}
	personal: MemoryItem[]
	atoms: MemoryAtom[]
	knowledge: {id: number; heading: string; subheading?: string; content: string; awareness: string; knowledgeType?: string; score: number}[]
	echoes: {content: string; score: number}[]
}

/** MCP 服务器状态 */
export interface McpServerStatusInfo {
	serverId: string
	name: string
	status: "disconnected" | "connecting" | "connected" | "error"
	errorMessage?: string
	hasEnvironment?: boolean
	secretIssue?: string
	tools: {
		name: string
		description?: string
		inputSchema?: Record<string, unknown>
	}[]
	resources?: unknown[]
}

/** 交互反应模式: 本地动作 / AI 大脑响应 */
export type InteractionReactionMode = "local" | "ai"

/** 交互动作触发模式: 无 / 随机 / 指定 */
export type InteractionActionMode = "none" | "random" | "selected"

/** 交互动作定义 (动作或表情) */
export interface InteractionAction {
	mode: InteractionActionMode
	group?: string
	name?: string
}

/** 归一化矩形区域 (0~1, y 向下) */
export interface InteractionRect {
	x: number
	y: number
	width: number
	height: number
}

/** 自定义矩形交互区域 */
export interface InteractionRegion {
	id: string
	name: string
	reactionMode: InteractionReactionMode
	rect: InteractionRect
	motion: InteractionAction
	expression: InteractionAction
}

/** 交互区域配置 */
export interface InteractionConfig {
	version: 1
	regions: InteractionRegion[]
}

/** 模型元数据 (model_get_meta) */
export interface ModelMeta {
	modelId: string
	scale: number
	opacity: number
	shadow: boolean
	renderScale: number
	qualityMode: "adaptive" | "quality" | "eco"
	maxFps: number
	expressions: string[]
	motions: {group: string; names: string[]}[]
	interactions: InteractionConfig
}
