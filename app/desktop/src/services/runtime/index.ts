/**
 * 后端运行时客户端
 *
 * WebView 唯一的业务桥接入口: 只读状态快照 + 领域命令 + 事件订阅。
 * 组件不允许直接 invoke 业务命令或触碰配置键 —— 一切经由这里。
 */
import {ref} from "vue"
import {invoke} from "../host/invoke"
import {listen, type UnlistenFn} from "../host/event"
import type {
	AgentEventPayload,
	BehaviorsState,
	HistoryMessage,
	InteractionConfig,
	McpServerStatusInfo,
	MemoryAtom,
	MemoryItem,
	MemoryIndexStatus,
	MemoryListPage,
	MemoryOverview,
	MemoryRecallDebug,
	MemorySettings,
	MemorySource,
	ModelMeta,
	PlatformState,
	UiSnapshot,
} from "./types"

export type {
	AgentEventPayload,
	AgentState,
	ApprovalRequestDto,
	AppInfo,
	BehaviorsState,
	EmbeddingState,
	EmotionDto,
	GeneralState,
	HistoryMessage,
	InteractionAction,
	InteractionActionMode,
	InteractionConfig,
	InteractionReactionMode,
	InteractionRect,
	InteractionRegion,
	McpServerStatusInfo,
	MemoryAtom,
	MemoryItem,
	MemoryIndexStatus,
	MemoryListPage,
	MemoryOverview,
	MemoryRecallDebug,
	MemorySettings,
	MemorySource,
	MemoryState,
	ModelItem,
	ModelMeta,
	ModelsState,
	PetState,
	PlatformState,
	ProactiveState,
	TelemetryState,
	ReminderDto,
	SkillDto,
	ToolDto,
	UsageMetrics,
	VoiceState,
	UiSnapshot,
} from "./types"

/** 全局只读快照 (响应式) */
const SNAPSHOT = ref<UiSnapshot | null>(null)

let bootstrap: Promise<void> | null = null

/** 上一次见到的语言, 用于跳窗口同步语言切换 */
let lastLanguage: string | null = null
const languageHandlers = new Set<(language: string) => void>()

async function refresh(): Promise<void> {
	try {
		SNAPSHOT.value = await invoke<UiSnapshot>("ui_get_snapshot")
		const LANGUAGE = SNAPSHOT.value?.general.language
		if (LANGUAGE && LANGUAGE !== lastLanguage) {
			const FIRST = lastLanguage === null
			lastLanguage = LANGUAGE
			// 首次拉取不回放 (main.ts 已经用它初始化 i18n), 只处理后续变更
			if (!FIRST) for (const handler of languageHandlers) handler(LANGUAGE)
		}
	} catch (error) {
		console.error("获取 UI 快照失败:", error)
	}
}

/**
 * 运行时客户端
 */
export const RUNTIME = {
	/** 只读快照 */
	snapshot: SNAPSHOT,

	/**
	 * 引导: 拉取首份快照并订阅全局状态变更广播。
	 * 幂等, 多窗口/多组件可重复调用。
	 */
	async init(): Promise<void> {
		if (!bootstrap) {
			bootstrap = (async () => {
				await refresh()
				await listen<{version: number; topics: string[]}>("nori:state-changed", () => {
					void refresh()
				})
			})()
		}
		await bootstrap
	},

	/** 手动刷新快照 */
	refresh,

	/**
	 * 当前平台能力 (快照未到位时给最保守的假设)
	 *
	 * 组件不要自己猜平台: 拖拽手柄、穿透开关、眼神跟随这类 UI 全部看这里。
	 */
	platform(): PlatformState {
		return SNAPSHOT.value?.platform ?? {
			os: "unknown",
			sessionType: "unknown",
			supportsGlobalCursor: false,
			supportsWindowDrag: false,
			supportsHitThrough: false,
			supportsTopmost: false,
			supportsTray: false,
		}
	},

	// ------------------------------------------------------------------
	// 事件订阅
	// ------------------------------------------------------------------

	/** Agent 会话事件 (仅发起会话的窗口会收到) */
	onAgentEvent(handler: (payload: AgentEventPayload) => void): Promise<UnlistenFn> {
		return listen<AgentEventPayload>("nori:agent-event", ({payload}) => handler(payload))
	},

	/** 主动交互消息 (挂机关怀/日程问候/提醒触发) */
	onProactiveMessage(handler: (text: string) => void): Promise<UnlistenFn> {
		return listen<{text: string}>("nori:proactive-message", ({payload}) => handler(payload.text))
	},

	/**
	 * 语言变更 (任一窗口改了语言后, 每个窗口都靠它重放 setLanguage)
	 *
	 * 返回取消注册的函数
	 */
	onLanguageChanged(handler: (language: string) => void): () => void {
		languageHandlers.add(handler)
		return () => languageHandlers.delete(handler)
	},

	// ------------------------------------------------------------------
	// 聊天 / Agent
	// ------------------------------------------------------------------

	startChat(text: string): Promise<string> {
		return invoke<string>("chat_start", {text})
	},
	cancelChat(sessionId: string): Promise<boolean> {
		return invoke<boolean>("chat_cancel", {sessionId})
	},
	respondApproval(requestId: string, approved: boolean): Promise<boolean> {
		return invoke<boolean>("approval_respond", {requestId, approved})
	},
	historyPage(limit = 50, beforeId = 0): Promise<HistoryMessage[]> {
		return invoke<HistoryMessage[]>("chat_history_page", {limit, beforeId})
	},
	clearChat(): Promise<void> {
		return invoke<void>("chat_clear")
	},

	// ------------------------------------------------------------------
	// 设置
	// ------------------------------------------------------------------

	updateAi(patch: Partial<{provider: string; baseUrl: string; apiKey: string; model: string; persona: string}>): Promise<void> {
		return invoke<void>("settings_update_ai", patch)
	},
	updateVoice(patch: Partial<{
		volume: string
		ttsProvider: string
		ttsBaseUrl: string
		ttsApiKey: string
		ttsVoice: string
		ttsSpeed: string
		ttsAutoPlay: boolean
		gptsovitsBaseUrl: string
		gptsovitsRefAudio: string
		gptsovitsPromptText: string
		gptsovitsPromptLang: string
		sttProvider: string
		sttBaseUrl: string
		sttApiKey: string
	}>): Promise<void> {
		return invoke<void>("settings_update_voice", patch)
	},
	updateGeneral(patch: Partial<{language: string; petAutoSummon: boolean; sidebarCollapsed: boolean; telemetryEnabled: boolean}>): Promise<void> {
		return invoke<void>("settings_update_general", patch)
	},
	updateProactive(patch: Partial<{idleEnabled: boolean; idleMinutes: number; dailyGreeting: boolean}>): Promise<void> {
		return invoke<void>("settings_update_proactive", patch)
	},
	updateEmbedding(patch: Partial<{model: string; baseUrl: string; apiKey: string; dimensions: string}>): Promise<void> {
		return invoke<void>("settings_update_embedding", patch)
	},
	ackVoiceNotice(): Promise<void> {
		return invoke<void>("settings_ack_voice_notice")
	},
	fetchModels(provider: string, baseUrl: string, apiKey: string): Promise<string[]> {
		return invoke<string[]>("llm_fetch_models", {provider, baseUrl, apiKey})
	},

	toolsSetEnabled(name: string, enabled: boolean): Promise<void> {
		return invoke<void>("tools_set_enabled", {name, enabled})
	},
	toolsExecuteManual(name: string, args: Record<string, unknown> = {}): Promise<unknown> {
		return invoke("tools_execute_manual", {name, arguments: args})
	},

	// ------------------------------------------------------------------
	// 模型
	// ------------------------------------------------------------------

	selectModel(modelId: string): Promise<void> {
		return invoke<void>("model_select", {modelId})
	},
	firstRunSelectModel(modelId: string): Promise<void> {
		return invoke<void>("first_run_select_model", {modelId})
	},
	completeFirstRun(modelId: string, telemetryEnabled: boolean): Promise<void> {
		return invoke<void>("complete_first_run", {modelId, telemetryEnabled})
	},

	/**
	 * init 窗口把主界面切换交给宿主；宿主会按模型有效性与 pet_auto_summon 决定桌宠显隐。
	 */
	initEnterMain(): Promise<void> {
		return invoke<void>("init_enter_main")
	},

	/**
	 * init 窗口就绪握手
	 *
	 * 返回 initStartPending=true 说明向导已经广播过 nori:init-start (早于本页订阅),
	 * 调用方应直接执行初始化流程, 不能再等事件
	 */
	initReady(): Promise<{initStartPending: boolean}> {
		return invoke<{initStartPending: boolean}>("init_ready")
	},
	importLocalModel(): Promise<string[] | null> {
		return invoke<string[] | null>("model_import_local", {resourceType: "live2d"})
	},
	modelMeta(modelId: string): Promise<ModelMeta> {
		return invoke<ModelMeta>("model_get_meta", {modelId})
	},
	setModelDisplay(modelId: string, patch: {scale?: number; expressions?: string[]}): Promise<void> {
		return invoke<void>("model_set_display", {modelId, ...patch})
	},
	setModelInteractions(modelId: string, interactions: InteractionConfig): Promise<void> {
		return invoke<void>("model_set_interactions", {modelId, interactions})
	},
	setModelBehavior(patch: Partial<BehaviorsState>): Promise<void> {
		return invoke<void>("model_set_behavior", patch)
	},

	// ------------------------------------------------------------------
	// 记忆库
	// ------------------------------------------------------------------

	memoryAdd(content: string, importance = 0.8, tags?: string, kind?: string): Promise<MemoryItem> {
		return invoke<MemoryItem>("memory_add", {content, importance, tags, kind})
	},
	memoryList(limit = 50): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("memory_list", {limit})
	},
	memoryListPage(query?: string, kind?: string, status?: string, limit = 50, offset = 0): Promise<MemoryListPage> {
		return invoke<MemoryListPage>("memory_list_page", {query, kind, status, limit, offset})
	},
	memoryGet(id: number): Promise<{item: MemoryItem; atoms: MemoryAtom[]; sources: MemorySource[]}> {
		return invoke("memory_get", {id})
	},
	memoryUpdate(id: number, content: string, importance?: number, tags?: string, patch?: {kind?: string; canonicalSummary?: string; personaSummary?: string; confidence?: number}): Promise<boolean> {
		return invoke<boolean>("memory_update", {id, content, importance, tags, ...patch})
	},
	memoryArchive(id: number): Promise<boolean> {
		return invoke<boolean>("memory_archive", {id})
	},
	memoryRestore(id: number): Promise<boolean> {
		return invoke<boolean>("memory_restore", {id})
	},
	memoryDelete(id: number): Promise<boolean> {
		return invoke<boolean>("memory_delete", {id, confirmToken: "DELETE_MEMORY"})
	},
	memoryClear(): Promise<void> {
		return invoke<void>("memory_clear", {confirmToken: "CLEAR_PERSONAL_MEMORY"})
	},
	memoryOverview(): Promise<MemoryOverview> {
		return invoke<MemoryOverview>("memory_overview")
	},
	memoryAtoms(memoryId?: number, status?: string, limit = 50, offset = 0): Promise<MemoryAtom[]> {
		return invoke<MemoryAtom[]>("memory_atom_list", {memoryId, status, limit, offset})
	},
	memorySearch(keyword: string, limit = 20): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("memory_search_hybrid", {keyword, limit})
	},
	memoryKnowledgeStatus(): Promise<MemoryIndexStatus> {
		return invoke<MemoryIndexStatus>("memory_knowledge_status")
	},
	memoryKnowledgeReindex(): Promise<MemoryIndexStatus> {
		return invoke<MemoryIndexStatus>("memory_knowledge_reindex")
	},
	memoryKnowledgeOpen(): Promise<void> {
		return invoke<void>("memory_knowledge_open")
	},
	memoryRecallDebug(query: string): Promise<MemoryRecallDebug> {
		return invoke<MemoryRecallDebug>("memory_recall_debug", {query})
	},
	memoryGetSettings(): Promise<MemorySettings> {
		return invoke<MemorySettings>("memory_get_settings")
	},
	memoryUpdateSettings(settings: Partial<MemorySettings>): Promise<MemorySettings> {
		return invoke<MemorySettings>("memory_update_settings", {settings})
	},
	memoryReembed(): Promise<number> {
		return invoke<number>("memory_reembed_all")
	},

	// ------------------------------------------------------------------
	// 技能
	// ------------------------------------------------------------------

	skillsMarketplace() {
		return invoke<import("./types").SkillDto[]>("skills_marketplace")
	},
	skillsToggle(id: string, enabled: boolean): Promise<void> {
		return invoke<void>("skills_toggle", {id, enabled})
	},
	skillsInstallUrl(url: string): Promise<unknown> {
		return invoke("skills_install_url", {url})
	},
	skillsSaveCustom(skill: Record<string, unknown>): Promise<unknown> {
		return invoke("skills_save_custom", {skill})
	},
	skillsUninstall(id: string): Promise<void> {
		return invoke<void>("skills_uninstall", {id})
	},
	skillsExport(id: string): Promise<string> {
		return invoke<string>("skills_export", {id})
	},
	skillsImportJson(json: string): Promise<unknown> {
		return invoke("skills_import_json", {json})
	},

	// ------------------------------------------------------------------
	// MCP
	// ------------------------------------------------------------------

	mcpGetServers(): Promise<McpServerStatusInfo[]> {
		return invoke<McpServerStatusInfo[]>("mcp_get_servers")
	},
	mcpSaveServer(config: Record<string, unknown>): Promise<McpServerStatusInfo> {
		return invoke<McpServerStatusInfo>("mcp_save_server", config)
	},
	mcpDeleteServer(id: string): Promise<boolean> {
		return invoke<boolean>("mcp_delete_server", {id})
	},
	mcpConnect(id: string): Promise<McpServerStatusInfo> {
		return invoke<McpServerStatusInfo>("mcp_connect_server", {id})
	},
	mcpDisconnect(id: string): Promise<McpServerStatusInfo> {
		return invoke<McpServerStatusInfo>("mcp_disconnect_server", {id})
	},
	mcpTestServer(config: Record<string, unknown>): Promise<McpServerStatusInfo> {
		return invoke<McpServerStatusInfo>("mcp_test_server", config)
	},
	mcpCallTool(serverId: string, toolName: string, args: Record<string, unknown>): Promise<unknown> {
		return invoke("mcp_call_tool", {serverId, toolName, arguments: args})
	},
	mcpImportUrl(url: string): Promise<unknown> {
		return invoke("mcp_import_url", {url})
	},

	// ------------------------------------------------------------------
	// 提醒
	// ------------------------------------------------------------------

	reminderAdd(content: string, delayMinutes: number): Promise<unknown> {
		return invoke("reminder_add", {content, delayMinutes})
	},
	reminderCancel(id: string): Promise<boolean> {
		return invoke<boolean>("reminder_cancel", {id})
	},

	// ------------------------------------------------------------------
	// 语音
	// ------------------------------------------------------------------

	ttsTest(text?: string): Promise<void> {
		return invoke<void>("tts_test", text ? {text} : {})
	},
	ttsStop(): Promise<void> {
		return invoke<void>("tts_stop")
	},
	sttStart(): Promise<void> {
		return invoke<void>("stt_start")
	},
	sttStop(): Promise<{text: string}> {
		return invoke<{text: string}>("stt_stop")
	},

	// ------------------------------------------------------------------
	// 桌宠 / 日志 / 调试
	// ------------------------------------------------------------------

	getRecentLogs(): Promise<{time: string; level: string; source: string; message: string}[]> {
		return invoke("get_recent_logs")
	},
	clearRecentLogs(): Promise<void> {
		return invoke<void>("clear_recent_logs")
	},
	getDiagnosticInfo(): Promise<Record<string, string>> {
		return invoke("get_diagnostic_info")
	},
	openLogFolder(): Promise<void> {
		return invoke<void>("open_log_folder")
	},
	runGcCollect(): Promise<{released_bytes: number}> {
		return invoke("run_gc_collect")
	},
	debugCrashTest(mode: string): Promise<void> {
		return invoke<void>("debug_crash_test", {mode})
	},
	petPlayMotion(name?: string): Promise<boolean> {
		return invoke<boolean>("pet_play_motion", name ? {name} : {})
	},
	writeLog(level: "info" | "warn" | "error", message: string): Promise<void> {
		return invoke<void>("write_log", {level, message})
	},
	exitApp(): Promise<void> {
		return invoke<void>("exit_app")
	},
	copyText(text: string): Promise<void> {
		return invoke<void>("clipboard_write_text", {text})
	},
	openUrl(url: string): Promise<void> {
		return invoke<void>("open_url", {url})
	},
}
