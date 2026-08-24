import type {
	HistoryMessage,
	InteractionConfig,
	MemoryAtom,
	MemoryIndexStatus,
	MemoryItem,
	MemoryOverview,
	MemoryRecallDebug,
	MemorySettings,
	MemorySource,
	ModelMeta,
	McpServerStatusInfo,
	ProviderConnectionTestResult,
	SkillDto,
	UiSnapshot,
} from "../runtime/types"

export type CommandArgs = Record<string, unknown>
export type EmptyCommandArgs = undefined

/** 前端实际使用的宿主命令契约。C# 仍会再次校验参数和来源窗口。 */
export interface BridgeCommandMap {
	ui_get_snapshot: {args: EmptyCommandArgs; result: UiSnapshot}
	write_log: {args: {level: "info" | "warn" | "error" | "debug"; message: string}; result: void}
	get_recent_logs: {args: EmptyCommandArgs; result: {time: string; level: string; source: string; message: string}[]}
	clear_recent_logs: {args: EmptyCommandArgs; result: void}
	get_diagnostic_info: {args: EmptyCommandArgs; result: Record<string, string>}
	export_diagnostics: {args: EmptyCommandArgs; result: {fileName: string; bytes: number; skipped: string[]} | null}
	open_log_folder: {args: EmptyCommandArgs; result: void}
	run_gc_collect: {args: EmptyCommandArgs; result: {released_bytes: number}}
	debug_crash_test: {args: {mode: string}; result: void}
	get_system_language: {args: EmptyCommandArgs; result: string}
	exit_app: {args: EmptyCommandArgs; result: void}
	clipboard_write_text: {args: {text: string}; result: void}
	open_url: {args: {url: string}; result: void}

	llm_fetch_models: {args: {provider: string; baseUrl: string; apiKey: string}; result: string[]}
	llm_test_connection: {args: {provider: string; baseUrl: string; apiKey: string; model: string}; result: ProviderConnectionTestResult}
	embedding_test_connection: {args: {baseUrl: string; apiKey: string; model: string; dimensions?: string}; result: ProviderConnectionTestResult}
	ai_test_connection: {args: {target: "chat" | "embedding"; provider?: string; baseUrl?: string; apiKey?: string; model?: string; dimensions?: string}; result: ProviderConnectionTestResult}
	settings_update_ai: {args: Partial<{provider: string; baseUrl: string; apiKey: string; model: string; persona: string}>; result: void}
	settings_update_embedding: {args: Partial<{model: string; baseUrl: string; apiKey: string; dimensions: string}>; result: void}
	settings_update_ai_providers: {args: {
		chat?: Partial<{provider: string; baseUrl: string; apiKey: string; model: string}>
		embedding?: Partial<{model: string; baseUrl: string; apiKey: string; dimensions: string}>
		persona?: string
	}; result: void}
	settings_update_voice: {args: CommandArgs; result: void}
	settings_update_general: {args: CommandArgs; result: void}
	settings_update_proactive: {args: CommandArgs; result: void}
	settings_ack_voice_notice: {args: EmptyCommandArgs; result: void}

	chat_start: {args: {text: string}; result: string}
	chat_cancel: {args: {sessionId: string}; result: boolean}
	approval_respond: {args: {requestId: string; approved: boolean}; result: boolean}
	chat_history_page: {args: {limit?: number; beforeId?: number}; result: HistoryMessage[]}
	chat_clear: {args: EmptyCommandArgs; result: void}

	model_select: {args: {modelId: string}; result: void}
	complete_first_run: {args: {modelId: string; telemetryEnabled: boolean}; result: void}
	init_enter_main: {args: EmptyCommandArgs; result: void}
	get_init_config: {args: EmptyCommandArgs; result: CommandArgs}
	init_ready: {args: EmptyCommandArgs; result: {initStartPending: boolean}}
	model_import_local: {args: {resourceType: "live2d"; sourceKind: "zip" | "folder"}; result: string[] | null}
	model_get_meta: {args: {modelId: string}; result: ModelMeta}
	model_set_display: {args: {modelId: string} & CommandArgs; result: void}
	model_set_interactions: {args: {modelId: string; interactions: InteractionConfig}; result: void}
	model_set_behavior: {args: CommandArgs; result: void}
	model_list: {args: EmptyCommandArgs; result: UiSnapshot}
	pet_play_motion: {args: {name?: string}; result: boolean}
	pet_reload_model: {args: {modelId?: string}; result: void}
	pet_get_state: {args: EmptyCommandArgs; result: CommandArgs}

	tools_set_enabled: {args: {name: string; enabled: boolean}; result: void}
	tools_execute_manual: {args: {name: string; arguments: CommandArgs}; result: unknown}

	memory_add: {args: CommandArgs; result: MemoryItem}
	memory_list: {args: {limit?: number}; result: MemoryItem[]}
	memory_list_page: {args: {query?: string; kind?: string; status?: string; limit?: number; offset?: number}; result: {items: MemoryItem[]; total: number}}
	memory_get: {args: {id: number}; result: {item: MemoryItem; atoms: MemoryAtom[]; sources: MemorySource[]}}
	memory_update: {args: CommandArgs; result: boolean}
	memory_delete: {args: {id: number; confirmToken: string}; result: boolean}
	memory_clear: {args: {confirmToken: string}; result: void}
	memory_archive: {args: {id: number}; result: boolean}
	memory_restore: {args: {id: number}; result: boolean}
	memory_overview: {args: EmptyCommandArgs; result: MemoryOverview}
	memory_atom_list: {args: CommandArgs; result: MemoryAtom[]}
	memory_search_hybrid: {args: CommandArgs; result: MemoryItem[]}
	memory_knowledge_status: {args: EmptyCommandArgs; result: MemoryIndexStatus}
	memory_knowledge_reindex: {args: EmptyCommandArgs; result: MemoryIndexStatus}
	memory_knowledge_open: {args: EmptyCommandArgs; result: void}
	memory_recall_debug: {args: {query: string}; result: MemoryRecallDebug}
	memory_get_settings: {args: EmptyCommandArgs; result: MemorySettings}
	memory_update_settings: {args: {settings: CommandArgs}; result: MemorySettings}
	memory_reembed_all: {args: EmptyCommandArgs; result: number}

	skills_marketplace: {args: EmptyCommandArgs; result: SkillDto[]}
	skills_toggle: {args: {id: string; enabled: boolean}; result: void}
	skills_install_url: {args: {url: string}; result: unknown}
	skills_save_custom: {args: {skill: CommandArgs}; result: unknown}
	skills_uninstall: {args: {id: string}; result: void}
	skills_export: {args: {id: string}; result: string}
	skills_import_json: {args: {json: string}; result: unknown}

	mcp_get_servers: {args: EmptyCommandArgs; result: McpServerStatusInfo[]}
	mcp_save_server: {args: CommandArgs; result: McpServerStatusInfo}
	mcp_delete_server: {args: {id: string}; result: boolean}
	mcp_connect_server: {args: {id: string}; result: McpServerStatusInfo}
	mcp_disconnect_server: {args: {id: string}; result: McpServerStatusInfo}
	mcp_list_tools: {args: EmptyCommandArgs; result: unknown}
	mcp_test_server: {args: CommandArgs; result: McpServerStatusInfo}
	mcp_call_tool: {args: {serverId: string; toolName: string; arguments: CommandArgs}; result: unknown}
	mcp_import_url: {args: {url: string}; result: unknown}

	reminder_add: {args: {content: string; delayMinutes: number}; result: unknown}
	reminder_cancel: {args: {id: string}; result: boolean}
	tts_test: {args: {text?: string}; result: void}
	tts_stop: {args: EmptyCommandArgs; result: void}
	stt_start: {args: EmptyCommandArgs; result: void}
	stt_stop: {args: EmptyCommandArgs; result: {text: string}}

	audio_host_ready: {args: EmptyCommandArgs; result: void}
	audio_playback_finished: {args: {token: string; error?: string}; result: void}
	audio_level: {args: {level: number}; result: void}
	audio_record_ready: {args: {token: string}; result: void}
	audio_record_failed: {args: {token: string; error?: string}; result: void}
	audio_upload_failed: {args: {token: string; error?: string}; result: void}

	window_show: {args: {label: string}; result: void}
	window_hide: {args: {label: string}; result: void}
	window_close: {args: {label: string}; result: void}
	window_focus: {args: {label: string}; result: void}
	window_is_visible: {args: {label: string}; result: boolean}
	window_scale_factor: {args: {label: string}; result: number}
	window_outer_position: {args: {label: string}; result: {x: number; y: number}}
	window_outer_size: {args: {label: string}; result: {width: number; height: number}}
	window_set_size: {args: {label: string; width: number; height: number}; result: void}
	window_set_position: {args: {label: string; x: number; y: number}; result: void}
	window_start_drag: {args: {label: string}; result: void}
}

export type BridgeCommandName = keyof BridgeCommandMap
export type BridgeCommandArgs<K extends BridgeCommandName> = BridgeCommandMap[K]["args"]
export type BridgeCommandResult<K extends BridgeCommandName> = BridgeCommandMap[K]["result"]
