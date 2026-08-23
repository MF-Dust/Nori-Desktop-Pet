using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Nori.Core.Agent;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Live2D;
using Nori.Core.Memory;
using Nori.Core.Mcp;
using Nori.Core.Platform;
using Nori.Core.Resources;
using Nori.Core.Security;
using Nori.Core.Skills;
using Nori.Core.Tools;
using Nori.Desktop.Diagnostics;
using Nori.Desktop.Runtime;
using Nori.Desktop.Telemetry;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Bridge;

/// <summary>
/// 桥接命令
///
/// 后端化后的命令面原则:
/// - WebView 不再持有通用 get_config/set_config 与业务编排入口;
///   前端只消费带版本号的 UI 快照 (ui_get_snapshot) 与领域命令。
/// - 状态变更与敏感能力按来源窗口授权 (RequireLabel): 业务命令只允许 main,
///   首次运行命令严格限制 first-run。
/// - 秘密只写不读: 快照仅返回 hasApiKey 等脱敏标记, 明文绝不回传。
/// 命令名保持 snake_case 且动词开头, 与前端 invoke("xxx") 完全一致。
/// </summary>
public sealed class BridgeCommands
{
	private readonly AppServices _services;

	/// <summary>UI 线程调度入口, 测试可注入同步实现</summary>
	private readonly Action<Action> _postUi;

	public BridgeCommands(AppServices services) : this(services, null)
	{
	}

	/// <summary>测试可注入 UI 调度入口的构造函数</summary>
	public BridgeCommands(AppServices services, Action<Action>? postUi)
	{
		_services = services;
		_postUi = postUi ?? (action => Dispatcher.UIThread.Post(action));
	}

	private AppRuntime Runtime => _services.Runtime
		?? throw new InvalidOperationException("应用运行时尚未就绪");

	/// <summary>
	/// 分发一次命令调用。
	/// </summary>
	public async Task<object?> InvokeAsync(
		IBridgeSource source,
		string cmd,
		JsonElement args,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		object? result = cmd switch
		{
		// ---- 应用 ----
		// invoke("exit_app")
		"exit_app" => RequireMain(source, () =>
		{
			_services.Windows.Shutdown();
			return (object?)null;
		}),

		// invoke("write_log", {level: "info", message: "xxx"})
		"write_log" => Run(() => _services.Logger.Write(LogSource.Frontend, Str(args, "level"), Str(args, "message"))),

		// invoke("get_system_language")
		"get_system_language" => ConfigStore.SystemLanguage(),

		/// invoke("complete_first_run", {modelId: "arg-nori", telemetryEnabled: true})
		"complete_first_run" => await CompleteFirstRunAsync(source, args, cancellationToken),

		// invoke("first_run_select_model", {modelId: "arg-nori"})
		"first_run_select_model" => RequireLabel(source, WindowLabels.FirstRun, () =>
			Run(() =>
			{
				UpdateConfigDirect(ConfigStore.KeySelectedModel, Str(args, "modelId"));
				Runtime.InvalidateSnapshot("models");
			})),

		// invoke("init_ready") → {initStartPending}
		// init 页面订阅完 nori:init-start 后调用; 返回 true 说明广播已先于订阅发生, 页面应直接跑初始化
		"init_ready" => RequireLabel(source, WindowLabels.Init, () => new
		{
			initStartPending = Runtime.ConsumeInitStartPending(),
		}),

		/// invoke("init_enter_main")
		"init_enter_main" => await InitEnterMainAsync(source, cancellationToken),

		// invoke("get_init_config")
		"get_init_config" => _services.Config.GetInitConfig(),

		// ---- UI 状态快照 ----
		// invoke("ui_get_snapshot")
		"ui_get_snapshot" => Runtime.BuildSnapshot(source),

		// invoke("settings_ack_voice_notice")
		"settings_ack_voice_notice" => RequireMain(source, () =>
			Run(() =>
			{
				UpdateConfigDirect("voice_notice_pending", "0");
				Runtime.InvalidateSnapshot("voice");
			})),

		// ---- AI 设置 ----
		// invoke("llm_fetch_models", {provider, baseUrl, apiKey})
		"llm_fetch_models" => await FetchModelsWithSourceCheckAsync(source, args),

		// invoke("settings_update_ai", {provider?, baseUrl?, apiKey?, model?, persona?})
		"settings_update_ai" => RequireLabel(source, WindowLabels.FirstRun, WindowLabels.Main, () =>
			Run(() =>
			{
				UpdateOptionalConfig(args, "provider", "llm_provider");
				UpdateOptionalConfig(args, "baseUrl", "llm_api_base");
				UpdateSecretConfig(args, "apiKey", "llm_api_key");
				UpdateOptionalConfig(args, "model", "llm_model");
				UpdateOptionalConfig(args, "persona", "nori_user_persona");
				Runtime.InvalidateSnapshot("ai");
			})),

		// invoke("settings_update_voice", {...})
		"settings_update_voice" => RequireMain(source, () =>
			Run(() =>
			{
				UpdateOptionalConfig(args, "volume", "audio_volume");
				UpdateOptionalConfig(args, "ttsProvider", "tts_provider");
				UpdateOptionalConfig(args, "ttsBaseUrl", "tts_base_url");
				UpdateSecretConfig(args, "ttsApiKey", "tts_api_key");
				UpdateOptionalConfig(args, "ttsVoice", "tts_voice");
				UpdateOptionalConfig(args, "ttsSpeed", "tts_speed");
				UpdateBoolConfig(args, "ttsAutoPlay", "tts_auto_play");
				UpdateOptionalConfig(args, "gptsovitsBaseUrl", "gptsovits_base_url");
				UpdateOptionalConfig(args, "gptsovitsRefAudio", "gptsovits_ref_audio");
				UpdateOptionalConfig(args, "gptsovitsPromptText", "gptsovits_prompt_text");
				UpdateOptionalConfig(args, "gptsovitsPromptLang", "gptsovits_prompt_lang");
				UpdateOptionalConfig(args, "sttProvider", "stt_provider");
				UpdateOptionalConfig(args, "sttBaseUrl", "stt_base_url");
				UpdateSecretConfig(args, "sttApiKey", "stt_api_key");
				if (_services.Runtime?.Voice is not null)
				{
					string raw = _services.Config.GetStringOr("audio_volume", "1");
					if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vol))
					{
						_services.Runtime.Voice.SetVolume(vol);
					}
					if (HasTtsConfigurationChange(args)) _services.Runtime.Voice.NotifyConfigurationChanged();
				}
				Runtime.InvalidateSnapshot("voice");
			})),

		// invoke("settings_update_general", {language?, petAutoSummon?, sidebarCollapsed?, telemetryEnabled?})
		"settings_update_general" => RequireLabel(source, WindowLabels.FirstRun, WindowLabels.Main, () =>
			Run(() =>
			{
				UpdateOptionalConfig(args, "language", ConfigStore.KeyLanguage);
				UpdateBoolConfig(args, "petAutoSummon", "pet_auto_summon");
				UpdateBoolConfig(args, "sidebarCollapsed", "ui_sidebar_collapsed");
				UpdateTelemetryConsent(source, args);
				_services.Telemetry.Configure(_services.Config.GetTelemetryConsent() == TelemetryConsent.Granted);
				Runtime.InvalidateSnapshot("general", "telemetry");
			})),

		// invoke("settings_update_proactive", {idleEnabled?, idleMinutes?, dailyGreeting?})
		"settings_update_proactive" => RequireMain(source, () =>
			Run(() =>
			{
				UpdateBoolConfig(args, "idleEnabled", "proactive_idle_enabled");
				UpdateNumberConfig(args, "idleMinutes", "proactive_idle_minutes");
				UpdateBoolConfig(args, "dailyGreeting", "proactive_daily_greeting");
				Runtime.InvalidateSnapshot("proactive");
			})),

		// invoke("settings_update_embedding", {model?, baseUrl?, apiKey?, dimensions?})
		"settings_update_embedding" => RequireMain(source, () =>
			Run(() =>
			{
				UpdateOptionalConfig(args, "model", "embedding_model");
				UpdateOptionalConfig(args, "baseUrl", "embedding_api_base");
				UpdateSecretConfig(args, "apiKey", "embedding_api_key");
				UpdateOptionalConfig(args, "dimensions", "embedding_dimensions");
				Runtime.QueueEmbeddingRebuild();
			})),

		// invoke("tools_set_enabled", {name: "getTime", enabled: false})
		"tools_set_enabled" => RequireMain(source, () =>
			Run(() =>
			{
				string name = Str(args, "name");
				bool enabled = OptionalBool(args, "enabled") ?? true;
				if (!Runtime.Tools.SetEnabled(name, enabled))
				{
					throw new InvalidOperationException($"未找到工具: {name}");
				}
				PersistDisabledTools();
				Runtime.InvalidateSnapshot("tools");
			})),

		// ---- 模型 ----
		// invoke("model_list")
		"model_list" => RequireMain(source, () => Runtime.BuildSnapshot(source)),

		// invoke("model_select", {modelId: "nori"})
		"model_select" => RequireLabel(source, WindowLabels.FirstRun, WindowLabels.Main, () =>
			Run(() =>
			{
				string modelId = Str(args, "modelId");
				UpdateConfigDirect(ConfigStore.KeySelectedModel, modelId);
				ApplyPetConfigAndBroadcast(ConfigStore.KeySelectedModel, modelId);
				_services.Logger.Write(LogSource.Backend, "info", $"启用模型: {modelId}");
				Runtime.InvalidateSnapshot("models");
			})),

		// invoke("model_import_local", {resourceType?: "live2d"})
		"model_import_local" => await ModelImportLocalAsync(source, args, cancellationToken),

		// invoke("model_get_meta", {modelId: "arg-nori"})
		"model_get_meta" => RequireMain(source, () =>
		{
			string modelId = Str(args, "modelId");
			string dir = _services.Resources.ResourceDir(ResourceType.Live2D, modelId);
			Nori.Core.Live2D.Model3MetaInfo meta = Nori.Core.Live2D.Model3Meta.Read(dir);
			float? scale = ReadFloatConfig($"l2d_scale_{modelId}") ?? ReadFloatConfig("l2d_scale") ?? 1f;
			float? opacity = ReadFloatConfig($"l2d_opacity_{modelId}") ?? ReadFloatConfig("l2d_opacity") ?? 1f;
			float? renderScale = ReadFloatConfig($"l2d_render_scale_{modelId}") ?? ReadFloatConfig("l2d_render_scale") ?? 2f;
			string qualityMode = _services.Config.GetStringOr($"l2d_quality_mode_{modelId}", _services.Config.GetStringOr("l2d_quality_mode", "adaptive"));
			bool shadow = _services.Config.GetBoolOr($"l2d_shadow_{modelId}", _services.Config.GetBoolOr("l2d_shadow", true));
			return new
			{
				modelId,
				scale,
				opacity,
				renderScale,
				qualityMode,
				shadow,
				expressions = meta.Expressions,
				motions = meta.Motions.Select(group => new {group = group.Group, names = group.Names}),
				interactions = ReadInteractionConfig(modelId),
			};
		}),

		/// invoke("model_set_interactions", {modelId, interactions})
		"model_set_interactions" => await ModelSetInteractionsAsync(source, args),

		// invoke("model_set_display", {modelId, scale?, expressions?})
		"model_set_display" => await ModelSetDisplayAsync(source, args),

		// invoke("model_set_behavior", {autoBlink?: true, maxFps?: 60, ...})
		"model_set_behavior" => await ModelSetBehaviorAsync(source, args),

		// ---- 聊天 / Agent 会话 ----
		// invoke("chat_start", {text: "你好呀"})
		"chat_start" => RequireMain(source, () =>
		{
			string text = Str(args, "text").Trim();
			if (text.Length == 0) throw new InvalidOperationException("消息内容不能为空");
			return Runtime.StartChat(source, text);
		}),

		// invoke("chat_cancel", {sessionId: "..."})
		"chat_cancel" => RequireMain(source, () => Runtime.CancelChat(source.Label, Str(args, "sessionId"))),

		// invoke("approval_respond", {requestId: "...", approved: true})
		"approval_respond" => RequireMain(source, () => Runtime.RespondApproval(
			source.Label, Str(args, "requestId"), OptionalBool(args, "approved") ?? false)),

		// invoke("chat_history_page", {limit?: 50, beforeId?: 0})
		"chat_history_page" => RequireMain(source, () => GetHistoryPage(
			ClampLimit(OptionalInt(args, "limit"), 50),
			(long)(OptionalDouble(args, "beforeId") ?? 0))),

		// invoke("chat_clear")
		"chat_clear" => RequireMain(source, () => Run(() =>
		{
			_services.Chat.ClearHistory();
			Runtime.InvalidateSnapshot("chat");
		})),

		// ---- 记忆库 ----
		/// invoke("memory_add", {content, type?, importance?, tags?})
		"memory_add" => await MemoryAddAsync(source, args),

		/// invoke("memory_list", {limit?})
		"memory_list" => await MemoryListAsync(source, args),

		/// invoke("memory_update", {id, content, importance?, tags?})
		"memory_update" => await MemoryUpdateAsync(source, args),

		/// invoke("memory_delete", {id, confirmToken: "DELETE_MEMORY"})
		"memory_delete" => RequireMain(source, () => HardDeleteMemory(source, args)),

		/// invoke("memory_clear", {confirmToken: "CLEAR_PERSONAL_MEMORY"})
		"memory_clear" => RequireMain(source, () => ClearMemories(source, args)),

		/// invoke("memory_archive", {id})
		"memory_archive" => RequireMain(source, () => ArchiveMemory(args)),

		/// invoke("memory_restore", {id})
		"memory_restore" => RequireMain(source, () => RestoreMemory(args)),

		/// invoke("memory_overview")
		"memory_overview" => RequireMain(source, MemoryOverview),

		/// invoke("memory_list_page", {query?, kind?, status?, limit?, offset?})
		"memory_list_page" => RequireMain(source, () => MemoryListPage(args)),

		/// invoke("memory_get", {id})
		"memory_get" => RequireMain(source, () => MemoryGet(args)),

		/// invoke("memory_atom_list", {memoryId?, status?, limit?, offset?})
		"memory_atom_list" => RequireMain(source, () => MemoryAtomList(args)),

		/// invoke("memory_knowledge_status")
		"memory_knowledge_status" => RequireMain(source, () => Runtime.Knowledge.Status),

		/// invoke("memory_knowledge_reindex")
		"memory_knowledge_reindex" => await MemoryKnowledgeReindexAsync(source),

		/// invoke("memory_knowledge_open")
		"memory_knowledge_open" => RequireMain(source, () => OpenKnowledgeFolder()),

		/// invoke("memory_recall_debug", {query})
		"memory_recall_debug" => await MemoryRecallDebugAsync(source, args),

		/// invoke("memory_get_settings")
		"memory_get_settings" => RequireMain(source, () => Runtime.Memory.Settings),

		/// invoke("memory_update_settings", {settings: {...}})
		"memory_update_settings" => RequireMain(source, () => UpdateMemorySettings(args)),

		/// invoke("memory_search_hybrid", {keyword, limit?})
		"memory_search_hybrid" => await MemorySearchHybridAsync(source, args),

		/// invoke("memory_reembed_all")
		"memory_reembed_all" => await MemoryReembedAllAsync(source),

		// ---- 技能 ----
		// invoke("skills_marketplace")
		"skills_marketplace" => RequireMain(source, () => SkillServiceMarketplace()),

		// invoke("skills_toggle", {id, enabled})
		"skills_toggle" => RequireMain(source, () =>
			Run(() =>
			{
				if (!SkillsToggle(Str(args, "id"), OptionalBool(args, "enabled") ?? true))
				{
					throw new InvalidOperationException($"未找到技能: {Str(args, "id")}");
				}
				Runtime.InvalidateSnapshot("skills");
			})),

		// invoke("skills_install_url", {url})
		"skills_install_url" => await SkillsInstallUrlAsync(source, args),

		// invoke("skills_save_custom", {skill: {...}})
		"skills_save_custom" => await SkillsSaveCustomAsync(source, args),

		// invoke("skills_uninstall", {id})
		"skills_uninstall" => RequireMain(source, () =>
			Run(() =>
			{
				Runtime.Skills.Uninstall(Str(args, "id"));
				Runtime.InvalidateSnapshot("skills");
			})),

		// invoke("skills_export", {id}) → JSON 字符串
		"skills_export" => RequireMain(source, () => Runtime.Skills.Export(Str(args, "id"))),

		// invoke("skills_import_json", {json})
		"skills_import_json" => await SkillsImportJsonAsync(source, args),

		// ---- MCP ----
		// invoke("mcp_get_servers")
		"mcp_get_servers" => await McpGetServersAsync(source),
		// invoke("mcp_save_server", {id, name, transport, command, args, env, url, enabled, autoConnect})
		"mcp_save_server" => await McpSaveServerAsync(source, args),
		// invoke("mcp_delete_server", {id})
		"mcp_delete_server" => await McpDeleteServerAsync(source, args),
		// invoke("mcp_connect_server", {id})
		"mcp_connect_server" => await McpConnectServerAsync(source, args),
		// invoke("mcp_disconnect_server", {id})
		"mcp_disconnect_server" => await McpDisconnectServerAsync(source, args),
		// invoke("mcp_list_tools")
		"mcp_list_tools" => await McpListToolsAsync(source),
		// invoke("mcp_test_server", {id, name, transport, command, args, env, url, enabled, autoConnect})
		"mcp_test_server" => await McpTestServerAsync(source, args),
		// invoke("mcp_call_tool", {serverId, toolName, arguments, sessionId?})
		"mcp_call_tool" => await McpCallToolAsync(source, args),
		// invoke("mcp_import_url", {url})
		"mcp_import_url" => await McpImportUrlAsync(source, args),

		// invoke("tools_execute_manual", {name, arguments}) — 设置页手动测试, 仅放行 safe 工具
		"tools_execute_manual" => await ToolsExecuteManualAsync(source, args),

		// ---- 定时提醒 ----
		// invoke("reminder_add", {content, delayMinutes})
		"reminder_add" => RequireMain(source, () =>
		{
			Nori.Core.Proactive.ReminderItem item = Runtime.Proactive.AddReminder(
				Str(args, "content"), OptionalDouble(args, "delayMinutes") ?? 15);
			Runtime.InvalidateSnapshot("proactive");
			return item;
		}),

		// invoke("reminder_cancel", {id})
		"reminder_cancel" => RequireMain(source, () =>
		{
			bool cancelled = Runtime.Proactive.CancelReminder(Str(args, "id"));
			Runtime.InvalidateSnapshot("proactive");
			return cancelled;
		}),

		// ---- 语音 ----
		// invoke("tts_test", {text?})
		"tts_test" => await TtsTestAsync(source, args),

		// invoke("tts_stop")
		"tts_stop" => RequireMain(source, () => Run(Runtime.Voice.Stop)),

		// invoke("stt_start")
		"stt_start" => await SttStartAsync(source),

		// invoke("stt_stop") → {text}
		"stt_stop" => await SttStopAsync(source),

		// ---- 前端音频宿主回报 (WebAudio / MediaRecorder 下沉后的反向通道) ----
		// invoke("audio_host_ready")
		"audio_host_ready" => RequireMain(source, () => Run(Runtime.MarkAudioHostReady)),

		// invoke("audio_playback_finished", {token, error?})
		"audio_playback_finished" => RequireMain(source, () =>
			Run(() => Runtime.ReportPlaybackFinished(Str(args, "token"), OptionalStr(args, "error")))),

		// invoke("audio_level", {level: 0.42})
		"audio_level" => RequireMain(source, () =>
			Run(() => Runtime.ReportAudioLevel(Num(args, "level")))),

		// invoke("audio_record_ready", {token})
		"audio_record_ready" => RequireMain(source, () =>
			Run(() => Runtime.ReportRecordingReady(Str(args, "token")))),

		// invoke("audio_record_failed", {token, error?})
		"audio_record_failed" => RequireMain(source, () =>
			Run(() => Runtime.ReportRecordingFailed(Str(args, "token"), OptionalStr(args, "error")))),

		// invoke("audio_upload_failed", {token, error?})
		"audio_upload_failed" => RequireMain(source, () =>
			Run(() => Runtime.ReportRecordingFailed(Str(args, "token"), OptionalStr(args, "error")))),

		// ---- 桌宠 Live2D 原生控制 ----
		// invoke("pet_play_motion", {name?})
		"pet_play_motion" => RequireMain(source, () =>
			Run(() => PlayPetMotion(args))),

		// invoke("pet_reload_model", {modelId?})
		"pet_reload_model" => RequireMain(source, () =>
			Run(() => _services.PetRuntime.RequestModelLoad(OptionalStr(args, "modelId") ?? _services.PetRuntime.CurrentModelId))),

		// invoke("pet_get_state")
		"pet_get_state" => RequireMain(source, GetPetState),

		// ---- 窗口 ----
		"window_show" => await OnUi(() => Run(() => _services.Windows.Show(Str(args, "label")))),
		"window_hide" => await OnUi(() => Run(() => _services.Windows.Hide(Str(args, "label")))),
		"window_close" => await OnUi(() => Run(() => _services.Windows.Close(OptionalLabel(args) ?? source.Label))),
		"window_focus" => await OnUi(() => Run(() => Target(source, args).Activate())),
		"window_is_visible" => await OnUi(() => (object?)Target(source, args).IsVisible),
		"window_scale_factor" => await OnUi(() => (object?)Target(source, args).RenderScaling),
		"window_outer_position" => await OnUi(() => OuterPosition(Target(source, args))),
		"window_outer_size" => await OnUi(() => OuterSize(Target(source, args))),
		"window_set_size" => await OnUi(() => SetSize(Target(source, args), args)),
		"window_set_position" => await OnUi(() => SetPosition(Target(source, args), args)),
		"window_start_drag" => await OnUi(() => Run(() => PlatformServices.Current.StartWindowDrag(NativeHandleOf(Target(source, args))))),

		// ---- 插件替代 ----
		// invoke("open_url", {url: "https://..."})
		"open_url" => Run(() => ShellOpen.OpenUrl(Str(args, "url"))),

		// invoke("clipboard_write_text", {text: "..."})
		"clipboard_write_text" => await WriteClipboardAsync(source, Str(args, "text")),

		// ---- 调试 ----
		"get_recent_logs" => RequireMain(source, () => _services.Logger.RecentLogs().Select(entry => new
		{
			time = entry.Time,
			level = entry.Level,
			source = entry.Source == LogSource.Frontend ? "frontend" : "backend",
			message = entry.Message,
		}).ToArray()),
		"clear_recent_logs" => RequireMain(source, () => Run(_services.Logger.ClearRecentLogs)),
		"get_diagnostic_info" => RequireMain(source, () => DiagnosticInfo.Build(_services.PetRuntime)),
		"open_log_folder" => RequireMain(source, () => Run(OpenLogFolder)),
		"run_gc_collect" => RequireMain(source, RunGcCollect),
		"debug_crash_test" => RequireMain(source, () => Run(() => DebugCrashTest(Str(args, "mode")))),

			_ => throw new InvalidOperationException($"未知的命令: {cmd}"),
		};
		cancellationToken.ThrowIfCancellationRequested();
		return result;
	}

	/// <summary>main 窗口校验 (无返回值场景)</summary>
	private static void RequireMainVoid(IBridgeSource source)
	{
		if (source.Label != WindowLabels.Main)
		{
			throw new InvalidOperationException($"命令只能由 {WindowLabels.Main} 窗口调用");
		}
	}

	private async Task<object?> MemoryAddAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		MemoryKind? kind = OptionalStr(args, "kind") is { } kindText ? MemoryKindExtensions.Parse(kindText) : null;
		MemoryItem item = await Runtime.Memory.AddAsync(
			Str(args, "content"),
			OptionalStr(args, "type") ?? kind?.ToStorage() ?? "manual",
			OptionalDouble(args, "importance") ?? 0.5,
			OptionalStr(args, "tags"),
			"manual",
			kind);
		Runtime.InvalidateSnapshot("memory");
		return item;
	}

	private Task<object?> MemoryListAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		return Task.FromResult<object?>(_services.Memory.GetAll(ClampLimit(OptionalInt(args, "limit"), 50)));
	}

	private async Task<object?> MemoryUpdateAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		MemoryKind? kind = OptionalStr(args, "kind") is { } kindText ? MemoryKindExtensions.Parse(kindText) : null;
		bool updated = await Runtime.Memory.UpdateAsync(
			(long)Num(args, "id"),
			Str(args, "content"),
			OptionalDouble(args, "importance"),
			OptionalStr(args, "tags"),
			kind,
			OptionalStr(args, "canonicalSummary"),
			OptionalStr(args, "personaSummary"),
			OptionalDouble(args, "confidence"));
		if (updated) Runtime.InvalidateSnapshot("memory");
		return updated;
	}

	private async Task<object?> MemorySearchHybridAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		return await Runtime.Memory.SearchHybridAsync(Str(args, "keyword"), ClampLimit(OptionalInt(args, "limit"), 20));
	}

	private async Task<object?> MemoryReembedAllAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		int count = await Runtime.Memory.ReembedAllAsync();
		Runtime.InvalidateSnapshot("memory");
		return count;
	}

	private object? HardDeleteMemory(IBridgeSource source, JsonElement args)
	{
		if (args.ValueKind != JsonValueKind.Object || OptionalStr(args, "confirmToken") != "DELETE_MEMORY")
			throw new InvalidOperationException("删除记忆需要明确确认");
		bool deleted = Runtime.Memory.Delete((long)Num(args, "id"));
		if (deleted) Runtime.InvalidateSnapshot("memory");
		return deleted;
	}

	private object? ArchiveMemory(JsonElement args)
	{
		bool archived = Runtime.Memory.Archive((long)Num(args, "id"));
		if (archived) Runtime.InvalidateSnapshot("memory");
		return archived;
	}

	private object? RestoreMemory(JsonElement args)
	{
		bool restored = Runtime.Memory.Restore((long)Num(args, "id"));
		if (restored) Runtime.InvalidateSnapshot("memory");
		return restored;
	}

	private object? ClearMemories(IBridgeSource source, JsonElement args)
	{
		if (OptionalStr(args, "confirmToken") != "CLEAR_PERSONAL_MEMORY")
			throw new InvalidOperationException("清空记忆需要明确确认");
		Runtime.Memory.Clear();
		Runtime.Memory.ClearCache();
		Runtime.InvalidateSnapshot("memory");
		return null;
	}

	private object MemoryOverview()
	{
		(int active, int atoms, int archived, int total) = Runtime.Memory.GetOverview();
		MemorySettings settings = Runtime.Memory.Settings;
		return new
		{
			activeMemories = active,
			atomCount = atoms,
			archivedMemories = archived,
			totalMemories = total,
			knowledgeChunks = Runtime.Knowledge.Status.Total,
			reflectionCursor = Runtime.Memory.Store.GetEngineState("reflection_cursor"),
			lastReflection = Runtime.Memory.Store.GetEngineState("last_reflection_at"),
			lastMaintenance = Runtime.Memory.Store.GetEngineState("last_maintenance_at"),
			index = Runtime.Knowledge.Status,
			settings,
		};
	}

	private object MemoryListPage(JsonElement args)
	{
		string query = OptionalStr(args, "query")?.Trim() ?? "";
		string? kind = OptionalStr(args, "kind");
		string? status = OptionalStr(args, "status");
		int limit = ClampLimit(OptionalInt(args, "limit"), 50);
		int offset = Math.Max(0, OptionalInt(args, "offset") ?? 0);
		IEnumerable<MemoryItem> items = Runtime.Memory.Store.GetAll(100000);
		if (query.Length > 0)
		{
			items = items.Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| (item.CanonicalSummary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
				|| (item.PersonaSummary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
				|| (item.Tags?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
		}
		if (kind is not null) items = items.Where(item => item.Kind.Equals(MemoryKindExtensions.Parse(kind).ToStorage(), StringComparison.OrdinalIgnoreCase));
		if (status is not null) items = items.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
		List<MemoryItem> filtered = items.OrderByDescending(item => item.UpdatedAt).ToList();
		return new {items = filtered.Skip(offset).Take(limit).ToArray(), total = filtered.Count};
	}

	private object MemoryGet(JsonElement args)
	{
		long id = (long)Num(args, "id");
		MemoryItem item = Runtime.Memory.Get(id) ?? throw new InvalidOperationException("未找到记忆");
		return new {item, atoms = Runtime.Memory.GetAtoms(id, limit: 100), sources = Runtime.Memory.GetSources(id)};
	}

	private object MemoryAtomList(JsonElement args)
	{
		long? memoryId = args.TryGetProperty("memoryId", out JsonElement memoryIdElement) && memoryIdElement.ValueKind == JsonValueKind.Number
			? memoryIdElement.GetInt64() : null;
		MemoryStatus? status = OptionalStr(args, "status") is { } statusText ? MemoryStatusExtensions.Parse(statusText) : null;
		return Runtime.Memory.GetAtoms(memoryId, status, ClampLimit(OptionalInt(args, "limit"), 50), Math.Max(0, OptionalInt(args, "offset") ?? 0));
	}

	private async Task<object?> MemoryKnowledgeReindexAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		MemoryIndexStatus status = await Runtime.Knowledge.ReindexAsync().ConfigureAwait(false);
		Runtime.InvalidateSnapshot("memory");
		return status;
	}

	private object? OpenKnowledgeFolder()
	{
		string directory = System.IO.Path.GetDirectoryName(Runtime.Knowledge.Path) ?? AppPaths.DataDir;
		Process.Start(new ProcessStartInfo(directory) {UseShellExecute = true});
		return null;
	}

	private async Task<object?> MemoryRecallDebugAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string query = Str(args, "query");
		IReadOnlyList<(string Role, string Content)> recent = AgentHistory.NormalizeRecent(_services.Chat.GetHistory(8, 0));
		MemoryContext context = await Runtime.Memory.BuildContextAsync(query, recent, CancellationToken.None, true, false).ConfigureAwait(false);
		return new {trace = context.Debug, personal = context.Personal, atoms = context.Atoms, knowledge = context.Knowledge, echoes = context.Echoes};
	}

	private object? UpdateMemorySettings(JsonElement args)
	{
		JsonElement settings = args.TryGetProperty("settings", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object ? nested : args;
		SetMemoryBool(settings, "enabled", "memory_enabled");
		SetMemoryBool(settings, "reflectionEnabled", "memory_reflection_enabled");
		SetMemoryBool(settings, "decayEnabled", "memory_decay_enabled");
		SetMemoryBool(settings, "archiveEnabled", "memory_archive_enabled");
		SetMemoryBool(settings, "knowledgeEnabled", "memory_knowledge_enabled");
		SetMemoryBool(settings, "knowledgeWatch", "memory_knowledge_watch");
		SetMemoryBool(settings, "debugRetrieval", "memory_debug_retrieval");
		SetMemoryInt(settings, "reflectionRounds", "memory_reflection_rounds", 1, 32);
		SetMemoryInt(settings, "reflectionMinChars", "memory_reflection_min_chars", 100, 20000);
		SetMemoryInt(settings, "recallTopK", "memory_recall_top_k", 1, 20);
		SetMemoryInt(settings, "keywordTopK", "memory_keyword_top_k", 1, 100);
		SetMemoryInt(settings, "vectorTopK", "memory_vector_top_k", 1, 100);
		SetMemoryInt(settings, "rrfK", "memory_rrf_k", 1, 500);
		SetMemoryDouble(settings, "minSimilarity", "memory_min_similarity");
		SetMemoryDouble(settings, "sourceRetentionThreshold", "memory_source_retention_threshold");
		SetMemoryDouble(settings, "archiveThreshold", "memory_archive_threshold");
		Runtime.InvalidateSnapshot("memory");
		return Runtime.Memory.Settings;
	}

	private void SetMemoryBool(JsonElement args, string name, string key)
	{
		bool? value = OptionalBool(args, name);
		if (value is not null) UpdateConfigDirect(key, value.Value ? "true" : "false");
	}

	private void SetMemoryInt(JsonElement args, string name, string key, int min, int max)
	{
		if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number) return;
		int number = Math.Clamp(value.GetInt32(), min, max);
		UpdateConfigDirect(key, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}

	private void SetMemoryDouble(JsonElement args, string name, string key)
	{
		if (!args.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number) return;
		double number = Math.Clamp(value.GetDouble(), 0, 1);
		UpdateConfigDirect(key, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}

	private async Task<object?> SkillsInstallUrlAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		object skill = await Runtime.Skills.InstallFromUrlAsync(Str(args, "url"));
		Runtime.InvalidateSnapshot("skills");
		return skill;
	}

	private async Task<object?> SkillsSaveCustomAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		SkillRecord skill = args.GetProperty("skill").Deserialize<SkillRecord>(BridgeJson.Options)
			?? throw new InvalidOperationException("技能数据不能为空");
		object saved = Runtime.Skills.SaveCustom(skill);
		Runtime.InvalidateSnapshot("skills");
		return saved;
	}

	private async Task<object?> SkillsImportJsonAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		Runtime.Skills.ImportJson(Str(args, "json"));
		Runtime.InvalidateSnapshot("skills");
		return null;
	}

	private async Task<object?> McpGetServersAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		IReadOnlyList<McpServerStatusInfo> servers = await _services.Mcp.GetServersAsync();
		await Runtime.RefreshMcpToolsAsync();
		Runtime.InvalidateSnapshot("mcp", "tools");
		return servers;
	}

	private async Task<object?> McpListToolsAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		return await _services.Mcp.GetAllToolsAsync();
	}

	private async Task<object?> ToolsExecuteManualAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string name = Str(args, "name");
		RegisteredTool? tool = Runtime.Tools.Get(name)
			?? throw new InvalidOperationException($"未找到工具: {name}");
		if (tool.PermissionLevel != "safe")
		{
			throw new InvalidOperationException($"{name} 标记为 {tool.PermissionLevel}, 手动测试仅支持 safe 工具");
		}
		JsonNode? toolArgs = null;
		if (args.TryGetProperty("arguments", out JsonElement argElem) && argElem.ValueKind == JsonValueKind.Object)
		{
			toolArgs = JsonNode.Parse(argElem.GetRawText());
		}
		ToolResult result = await Runtime.Tools.ExecuteAsync(name, toolArgs);
		if (result.Error is not null) throw new InvalidOperationException(result.Error);
		return result.Result;
	}

	private async Task<object?> TtsTestAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string text = OptionalStr(args, "text") is {Length: > 0} custom ? custom : "主人好呀！我是 Nori，这是一条声音播放测试~";
		await Runtime.Voice.SpeakAsync(text);
		return null;
	}

	private async Task<object?> SttStartAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		await Runtime.Voice.StartListeningAsync();
		return null;
	}

	private async Task<object?> SttStopAsync(IBridgeSource source)
	{
		RequireMainVoid(source);
		string text = await Runtime.Voice.StopListeningAndTranscribeAsync();
		return new {text};
	}

	private async Task<object?> ModelImportLocalAsync(
		IBridgeSource source,
		JsonElement args,
		CancellationToken cancellationToken)
	{
		RequireMainVoid(source);
		return await ImportLocalResourceAsync(source, args, cancellationToken);
	}

	/// <summary>读取指定模型的互动配置; 损坏配置按空配置处理并记录日志。</summary>
	private PetInteractionConfig ReadInteractionConfig(string modelId)
	{
		if (_services.Config.Get(PetInteractionConfig.StorageKey(modelId)) is not ConfigValue.Json {Value: JsonNode node})
		{
			return PetInteractionConfig.Empty;
		}

		try
		{
			return PetInteractionConfig.Parse(node.ToJsonString(PetInteractionJson.Options));
		}
		catch (Exception exception)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"读取模型互动配置失败 [{modelId}]: {exception.Message}");
			return PetInteractionConfig.Empty;
		}
	}

	/// <summary>
	/// 写入指定模型的互动配置。
	/// 前端调用: invoke("model_set_interactions", {modelId, interactions})
	/// </summary>
	private async Task<object?> ModelSetInteractionsAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string modelId = Str(args, "modelId").Trim();
		if (modelId.Length == 0) throw new InvalidOperationException("模型 ID 不能为空");
		if (!_services.Resources.IsInstalled(ResourceType.Live2D, modelId)) throw new InvalidOperationException("模型尚未安装");
		if (!args.TryGetProperty("interactions", out JsonElement interactionsElement)
			|| interactionsElement.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidOperationException("互动配置不能为空");
		}

		PetInteractionConfig config;
		try
		{
			config = PetInteractionConfig.Parse(interactionsElement.GetRawText());
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException)
		{
			throw new InvalidOperationException($"互动配置无效: {exception.Message}", exception);
		}

		string dir = _services.Resources.ResourceDir(ResourceType.Live2D, modelId);
		Model3MetaInfo meta = Model3Meta.Read(dir);
		config.ValidateBindings(meta.Motions, meta.Expressions);
		_services.Config.Set(PetInteractionConfig.StorageKey(modelId), new ConfigValue.Json(config.ToJsonNode()));
		_services.PetRuntime?.SetInteractionConfig(modelId, config);
		PostBroadcast("nori:config-changed", new {key = PetInteractionConfig.StorageKey(modelId), value = config});
		Runtime.InvalidateSnapshot("models");
		await Task.CompletedTask;
		return null;
	}

	/// <summary>
	/// 模型显示参数写入 (缩放按模型存储, 表情列表同)
	/// </summary>
	private async Task<object?> ModelSetDisplayAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string modelId = Str(args, "modelId");
		if (args.TryGetProperty("scale", out JsonElement scaleElem) && scaleElem.ValueKind == JsonValueKind.Number)
		{
			ApplyDisplayKey($"l2d_scale_{modelId}", scaleElem.GetRawText());
		}
		if (args.TryGetProperty("opacity", out JsonElement opacityElem) && opacityElem.ValueKind == JsonValueKind.Number)
		{
			ApplyDisplayKey($"l2d_opacity_{modelId}", opacityElem.GetRawText());
		}
		if (args.TryGetProperty("renderScale", out JsonElement renderScaleElem) && renderScaleElem.ValueKind == JsonValueKind.Number)
		{
			ApplyDisplayKey($"l2d_render_scale_{modelId}", renderScaleElem.GetRawText());
		}
		if (args.TryGetProperty("qualityMode", out JsonElement qualityElem) && qualityElem.ValueKind == JsonValueKind.String)
		{
			ApplyDisplayKey($"l2d_quality_mode_{modelId}", qualityElem.GetString() ?? "adaptive");
		}
		if (args.TryGetProperty("shadow", out JsonElement shadowElem) && shadowElem.ValueKind is JsonValueKind.True or JsonValueKind.False)
		{
			ApplyDisplayKey($"l2d_shadow_{modelId}", shadowElem.GetBoolean() ? "1" : "0");
		}
		if (args.TryGetProperty("expressions", out JsonElement expElem) && expElem.ValueKind == JsonValueKind.Array)
		{
			ApplyDisplayKey($"l2d_expression_{modelId}", expElem.GetRawText());
		}
		await Task.CompletedTask;
		Runtime.InvalidateSnapshot("models");
		return null;
	}

	private void ApplyDisplayKey(string key, string storage)
	{
		UpdateConfigDirect(key, storage);
		ApplyPetConfigAndBroadcast(key, storage);
	}

	/// <summary>行为开关批量写入并热应用</summary>
	private async Task<object?> ModelSetBehaviorAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		SetBehaviorKey(args, "clickInteraction", "l2d_click_interaction");
		SetBehaviorKey(args, "autoBlink", "l2d_auto_blink");
		SetBehaviorKey(args, "eyeTracking", "l2d_eye_tracking");
		SetBehaviorKey(args, "idleEyeAnimation", "l2d_idle_eye_animation");
		SetBehaviorKey(args, "idleAnimation", "l2d_idle_animation");
		SetBehaviorKey(args, "expressionEnabled", "l2d_expression_enabled");
		SetBehaviorKey(args, "lipSync", "l2d_lip_sync");
		SetBehaviorKey(args, "shadow", "l2d_shadow");
		SetBehaviorKey(args, "beatSync", "l2d_beat_sync");
		SetBehaviorKey(args, "aiInteraction", PetInteractionConfig.AiEnabledKey);
		SetBehaviorKey(args, "renderScale", "l2d_render_scale");
		SetBehaviorKey(args, "qualityMode", "l2d_quality_mode");
		SetBehaviorKey(args, "opacity", "l2d_opacity");
		SetBehaviorKey(args, "maxFps", "l2d_max_fps");
		await Task.CompletedTask;
		Runtime.InvalidateSnapshot("behaviors");
		return null;
	}

	// ===================================================================
	// 授权辅助
	// ===================================================================

	/// <summary>
	/// 校验来源窗口后执行工厂函数
	/// </summary>
	private static object? RequireLabel(IBridgeSource source, string allowed, Func<object?> factory)
	{
		if (source.Label != allowed)
		{
			throw new InvalidOperationException($"命令只能由 {allowed} 窗口调用");
		}
		return factory();
	}

	private static object? RequireLabel(IBridgeSource source, string allowedA, string allowedB, Func<object?> factory)
	{
		if (source.Label != allowedA && source.Label != allowedB)
		{
			throw new InvalidOperationException($"命令只能由 {allowedA}/{allowedB} 窗口调用");
		}
		return factory();
	}

	private static object? RequireMain(IBridgeSource source, Func<object?> factory) => RequireLabel(source, WindowLabels.Main, factory);

	/// <summary>
	/// 首次启动完成: 只允许可见的 first-run 窗口调用。
	/// </summary>
	private async Task<object?> CompleteFirstRunAsync(
		IBridgeSource source,
		JsonElement args,
		CancellationToken cancellationToken)
	{
		if (source.Label != WindowLabels.FirstRun)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"拒绝 complete_first_run: 来源窗口 label={source.Label}");
			throw new InvalidOperationException("只能从首次运行窗口调用 complete_first_run");
		}
		bool visible = await OnUi(() => (object?)source.IsVisible) is true;
		if (!visible)
		{
			_services.Logger.Write(LogSource.Backend, "warn", "拒绝 complete_first_run: 首次运行窗口不可见");
			throw new InvalidOperationException("首次运行窗口不可见");
		}

		string modelId = RequireKnownInstalledModel(Str(args, "modelId"));
		bool telemetryEnabled = RequiredBool(args, "telemetryEnabled");
		cancellationToken.ThrowIfCancellationRequested();
		// 配置层在单个 SQLite 事务中提交所有首次运行结果, 不能留下半完成状态。
		_services.Config.CompleteFirstRun(modelId, telemetryEnabled);
		_services.Telemetry.Configure(telemetryEnabled);
		_services.Logger.Write(LogSource.Backend, "info", $"首次初始化完成: model={modelId}");

		// 先置位再广播: init 页面就绪晚于广播时可经 init_ready 回放, 不会卡在转圈
		Runtime.MarkInitStartPending();

		cancellationToken.ThrowIfCancellationRequested();
		await OnUi(() =>
		{
			_services.Windows.Close(WindowLabels.FirstRun);
			_services.Windows.Show(WindowLabels.Init);
			// 通知 init 窗口 (首次运行路径下为隐藏启动) 开始初始化流程
			_services.Windows.Broadcast("nori:init-start", null);
			return (object?)null;
		});
		return null;
	}

	/// <summary>
	/// 初始化页进入主界面: 只允许可见的 init 窗口调用。
	/// 宿主在这里统一完成 main/pet/init 的切换, 前端不直接调窗口命令。
	/// </summary>
	private async Task<object?> InitEnterMainAsync(IBridgeSource source, CancellationToken cancellationToken)
	{
		if (source.Label != WindowLabels.Init)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"拒绝 init_enter_main: 来源窗口 label={source.Label}");
			throw new InvalidOperationException("只能从初始化窗口调用 init_enter_main");
		}
		bool visible = await OnUi(() => (object?)source.IsVisible) is true;
		if (!visible) throw new InvalidOperationException("初始化窗口不可见");

		string? modelId = KnownModelIds.Normalize(_services.Config.GetStringOr(ConfigStore.KeySelectedModel, ""));
		bool modelValid = modelId is not null && IsKnownInstalledModel(modelId);
		bool autoSummon = _services.Config.GetBoolOr("pet_auto_summon", true);
		cancellationToken.ThrowIfCancellationRequested();
		await OnUi(() =>
		{
			_services.Windows.Show(WindowLabels.Main);
			if (modelValid && autoSummon) _services.Windows.Show(WindowLabels.Pet);
			else _services.Windows.Hide(WindowLabels.Pet);
			_services.Windows.Hide(WindowLabels.Init);
			return (object?)null;
		});
		return null;
	}

	/// <summary>校验已知模型 ID 且确认本地模型资源已安装。</summary>
	private string RequireKnownInstalledModel(string value)
	{
		string modelId = KnownModelIds.Normalize(value)
			?? throw new InvalidOperationException("只支持 arg-nori 或 nori 模型");
		if (!IsKnownInstalledModel(modelId)) throw new InvalidOperationException($"模型尚未安装: {modelId}");
		return modelId;
	}

	private bool IsKnownInstalledModel(string modelId)
	{
		try
		{
			return KnownModelIds.Normalize(modelId) is not null
				&& _services.Resources.IsInstalled(ResourceType.Live2D, modelId);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ResourceException)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"检查模型资源失败 [{modelId}]: {exception.Message}");
			return false;
		}
	}

	// ===================================================================
	// 配置写入 (内部直写, 不再暴露给前端通用入口)
	// ===================================================================

	private void UpdateConfigDirect(string key, string value) =>
		_services.Config.Set(key, new ConfigValue.Text(value));

	/// <summary>可选字段更新: 参数缺失时不动配置</summary>
	private void UpdateOptionalConfig(JsonElement args, string argName, string configKey)
	{
		if (args.ValueKind == JsonValueKind.Object
			&& args.TryGetProperty(argName, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
			&& value.GetString() is { } text)
		{
			UpdateConfigDirect(configKey, text);
		}
	}

	/// <summary>
	/// 秘密字段更新: 缺省不变; 显式空串表示清除; 非空则写入 (DPAPI 由 ConfigStore 自动加密)
	/// </summary>
	private void UpdateSecretConfig(JsonElement args, string argName, string configKey)
	{
		if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(argName, out JsonElement value)) return;
		if (value.ValueKind != JsonValueKind.String) return;
		string? text = value.GetString();
		if (text is {Length: > 0})
		{
			UpdateConfigDirect(configKey, text);
			_services.Logger.Write(LogSource.Backend, "info", $"已更新敏感配置: {configKey}");
		}
		else
		{
			_services.Config.Delete(configKey);
		}
	}

	private void UpdateBoolConfig(JsonElement args, string argName, string configKey)
	{
		if (args.ValueKind == JsonValueKind.Object
			&& args.TryGetProperty(argName, out JsonElement value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False)
		{
			UpdateConfigDirect(configKey, value.GetBoolean() ? "1" : "0");
		}
	}

	/// <summary>保存遥测三态同意; 首次运行的默认开启只在完成向导时确认。</summary>
	private void UpdateTelemetryConsent(IBridgeSource source, JsonElement args)
	{
		bool? enabled = OptionalBool(args, "telemetryEnabled");
		if (enabled is null) return;

		if (source.Label == WindowLabels.FirstRun)
		{
			_services.Config.SetTelemetryConsent(enabled.Value ? TelemetryConsent.Unset : TelemetryConsent.Denied);
		}
		else
		{
			_services.Config.SetTelemetryConsent(enabled.Value ? TelemetryConsent.Granted : TelemetryConsent.Denied);
		}
	}

	private void UpdateNumberConfig(JsonElement args, string argName, string configKey)
	{
		if (args.ValueKind == JsonValueKind.Object
			&& args.TryGetProperty(argName, out JsonElement value)
			&& value.ValueKind == JsonValueKind.Number)
		{
			UpdateConfigDirect(configKey, value.GetRawText());
		}
	}

	/// <summary>行为开关写入并热应用到桌宠 + 广播给预览</summary>
	private void SetBehaviorKey(JsonElement args, string argName, string configKey)
	{
		if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(argName, out JsonElement value)) return;
		string storage = value.ValueKind switch
		{
			JsonValueKind.True => "1",
			JsonValueKind.False => "0",
			JsonValueKind.Number => value.GetRawText(),
			JsonValueKind.String => value.GetString() ?? "",
			_ => "",
		};
		if (storage.Length == 0) return;
		UpdateConfigDirect(configKey, storage);
		ApplyPetConfigAndBroadcast(configKey, storage);
	}

	private void ApplyPetConfigAndBroadcast(string key, string storage)
	{
		_services.PetRuntime?.ApplyConfig(key, storage);
		PostBroadcast("nori:config-changed", new {key, value = storage});
	}

	/// <summary>持久化工具禁用清单</summary>
	private void PersistDisabledTools()
	{
		IReadOnlyList<string> disabled = Runtime.Tools.DisabledNames();
		string json = JsonSerializer.Serialize(disabled);
		JsonNode? node = JsonNode.Parse(json);
		if (node is not null)
		{
			_services.Config.Set("tools_disabled", new ConfigValue.Json(node));
		}
	}

	// ===================================================================
	// 聊天历史
	// ===================================================================

	/// <summary>
	/// 分页读取聊天历史 (服务端规范化旧协议 JSON, 前端不再解析业务内容)
	/// </summary>
	private object GetHistoryPage(int limit, long beforeId) => _services.Chat
		.GetHistory(limit, beforeId)
		.Where(row => row.Role != "user" || !row.Content.StartsWith("【系统工具执行反馈 -", StringComparison.Ordinal))
		.Select(row => new
		{
			id = row.Id,
			role = row.Role,
			content = row.Role == "assistant" ? AgentHistory.ExtractDisplayText(row.Content) : row.Content,
			createdAt = row.CreatedAt,
		})
		.ToArray();

	// ===================================================================
	// LLM / MCP / 技能辅助
	// ===================================================================

	private async Task<object?> FetchModelsWithSourceCheckAsync(IBridgeSource source, JsonElement args)
	{
		RequireLabel(source, WindowLabels.FirstRun, WindowLabels.Main, () => (object?)true);
		string apiKey = OptionalStr(args, "apiKey") ?? "";
		if (apiKey.Length == 0) apiKey = _services.Config.GetStringOr("llm_api_key", "");
		return await _services.Llm.FetchModelsAsync(
			OptionalStr(args, "provider"), Str(args, "baseUrl"), apiKey);
	}

	private object SkillServiceMarketplace() => Nori.Core.Skills.SkillPresets.All.Select(skill => new
	{
		id = skill.Id,
		name = skill.Name,
		description = skill.Description,
		author = skill.Author,
		version = skill.Version,
		icon = skill.Icon,
		tags = skill.Tags,
		category = skill.Category,
		instructions = skill.Instructions,
		tools = skill.Tools,
		source = skill.Source,
	}).ToArray();

	private bool SkillsToggle(string id, bool enabled) => Runtime.Skills.Toggle(id, enabled);

	private async Task<object?> McpImportUrlAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		string url = Str(args, "url");
		Nori.Core.Network.UrlAccessPolicy.EnsurePublicHttp(new Uri(url));
		using HttpResponseMessage response = await Nori.Core.Network.UrlAccessPolicy.GetWithSafeRedirectsAsync(
			_services.PublicHttp, new Uri(url), allowPrivate: false);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
		}
		string text = await Nori.Core.Network.UrlAccessPolicy.ReadCappedTextAsync(
			response.Content, Nori.Core.Network.UrlAccessPolicy.MaxResponseBytes);

		List<object> results = [];
		void SaveOne(McpServerConfig config) => results.Add(_services.Mcp.SaveServerAsync(config).GetAwaiter().GetResult());

		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			JsonElement root = document.RootElement.Clone();

			if (root.TryGetProperty("mcpServers", out JsonElement serversElem) && serversElem.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty server in serversElem.EnumerateObject())
				{
					SaveOne(BuildImportedConfig(server.Name, server.Value));
				}
			}
			else if (root.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in root.EnumerateArray())
				{
					SaveOne(BuildImportedConfig(OptionalGetString(item, "name") ?? "导入的 MCP 服务", item));
				}
			}
			else
			{
				SaveOne(BuildImportedConfig(
					OptionalGetString(root, "name") ?? OptionalGetString(root, "id") ?? "导入的 MCP 服务", root));
			}
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException($"未识别的 MCP 配置文件结构: {exception.Message}");
		}

		InvalidateMcpSnapshot();
		return results;
	}

	private static string? OptionalGetString(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object
			&& element.TryGetProperty(name, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static McpServerConfig BuildImportedConfig(string name, JsonElement source)
	{
		static string[] ReadArgs(JsonElement element) =>
			element.ValueKind == JsonValueKind.Object
				&& element.TryGetProperty("args", out JsonElement argsElem)
				&& argsElem.ValueKind == JsonValueKind.Array
					? argsElem.EnumerateArray().OfType<JsonElement>().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!).ToArray()
					: [];

		return new McpServerConfig
		{
			Id = $"mcp_import_{Guid.NewGuid().ToString("N")[..8]}",
			Name = name,
			Transport = OptionalGetString(source, "url") is not null ? McpTransportType.Sse : McpTransportType.Stdio,
			Command = OptionalGetString(source, "command") ?? "npx",
			Args = ReadArgs(source),
			Url = OptionalGetString(source, "url"),
			Enabled = true,
			AutoConnect = true,
		};
	}


	private async Task<object?> McpSaveServerAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		object result = await _services.Mcp.SaveServerAsync(ParseMcpConfig(args));
		await Runtime.RefreshMcpToolsAsync();
		InvalidateMcpSnapshot();
		return result;
	}

	private async Task<object?> McpDeleteServerAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		bool deleted = await _services.Mcp.DeleteServerAsync(Str(args, "id"));
		await Runtime.RefreshMcpToolsAsync();
		InvalidateMcpSnapshot();
		return deleted;
	}

	private async Task<object?> McpConnectServerAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		object result = await _services.Mcp.ConnectServerAsync(Str(args, "id"));
		await Runtime.RefreshMcpToolsAsync();
		InvalidateMcpSnapshot();
		return result;
	}

	private async Task<object?> McpDisconnectServerAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		object result = await _services.Mcp.DisconnectServerAsync(Str(args, "id"));
		await Runtime.RefreshMcpToolsAsync();
		InvalidateMcpSnapshot();
		return result;
	}

	private async Task<object?> McpTestServerAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		return await _services.Mcp.TestServerAsync(ParseMcpConfig(args));
	}

	private async Task<object?> McpCallToolAsync(IBridgeSource source, JsonElement args)
	{
		RequireMainVoid(source);
		return await CallMcpToolCoreAsync(source, args);
	}
	private void InvalidateMcpSnapshot() => Runtime.InvalidateSnapshot("mcp");

	// ===================================================================
	// 桌宠状态
	// ===================================================================

	private object GetPetState()
	{
		var pet = _services.PetRuntime;
		return new
		{
			modelId = pet?.CurrentModelId ?? "arg-nori",
			expressions = pet?.Expressions ?? [],
			motionGroups = pet?.MotionGroups ?? [],
			userScale = pet?.UserScale ?? 1.0f,
			opacity = pet?.Opacity ?? 1.0f,
			autoBlink = pet?.AutoBlinkEnabled ?? true,
			eyeTracking = pet?.EyeTrackingEnabled ?? true,
			idleEyeAnimation = pet?.IdleEyeAnimationEnabled ?? true,
			idleAnimation = pet?.IdleAnimationEnabled ?? true,
			expressionEnabled = pet?.ExpressionEnabled ?? true,
			shadow = pet?.ShadowEnabled ?? true,
			lipSync = pet?.LipSyncEnabled ?? true,
			beatSync = pet?.BeatSyncEnabled ?? false,
			clickInteraction = pet?.ClickInteraction ?? true,
			renderScale = pet?.RenderScale ?? Live2DRenderSettings.DefaultRenderScale,
			qualityMode = pet?.QualityMode ?? Live2DRenderSettings.DefaultQualityMode,
			maxFps = pet?.MaxFps ?? 0,
			render = pet?.RenderMetrics,
		};
	}

	private object? PlayPetMotion(JsonElement args)
	{
		if (_services.PetRuntime is null) return false;
		string? name = OptionalStr(args, "name");
		if (!string.IsNullOrEmpty(name))
		{
			return _services.PetRuntime.PlayMotionByName(name);
		}
		return _services.PetRuntime.PlayTapBodyOrRandomMotion();
	}

	/// <summary>
	/// 从本地 ZIP 文件或目录导入资源
	/// </summary>
	private async Task<object?> ImportLocalResourceAsync(
		IBridgeSource source,
		JsonElement args,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string? filePath = OptionalStr(args, "filePath");
		if (string.IsNullOrWhiteSpace(filePath))
		{
			Avalonia.Controls.Window? self = source.Self ?? throw new InvalidOperationException("来源窗口不可用");
			filePath = await Dispatcher.UIThread.InvokeAsync(async () =>
			{
				var files = await self.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "选择 Live2D 资源文件 (.zip)",
					AllowMultiple = false,
					FileTypeFilter =
					[
						new FilePickerFileType("Live2D 压缩包 (*.zip)") { Patterns = ["*.zip"] },
						new FilePickerFileType("所有文件 (*.*)") { Patterns = ["*.*"] },
					],
				});
				return files.Count > 0 ? files[0].Path.LocalPath : null;
			});
		}

		if (string.IsNullOrWhiteSpace(filePath))
		{
			return null;
		}

		ResourceType type = ParseResourceType(OptionalStr(args, "resourceType") ?? "live2d");
		IReadOnlyList<string> imported = await Task.Run(() => _services.Resources.Import(type, filePath), cancellationToken);
		_services.Logger.Write(LogSource.Backend, "info", $"成功导入本地资源: {filePath} -> {string.Join(", ", imported)}");

		// 广播资源更新
		PostBroadcast("nori:config-changed", new {key = "resource_imported", value = string.Join(",", imported)});
		Runtime.InvalidateSnapshot("models");
		return imported;
	}

	private async Task<object?> CallMcpToolCoreAsync(IBridgeSource source, JsonElement args)
	{
		string serverId = Str(args, "serverId");
		string toolName = Str(args, "toolName");
		JsonObject? toolArgs = null;
		if (args.TryGetProperty("arguments", out JsonElement argElem) && argElem.ValueKind == JsonValueKind.Object)
		{
			toolArgs = JsonNode.Parse(argElem.GetRawText()) as JsonObject;
		}

		// 带 sessionId 的 MCP 调用可被宿主取消注册表取消
		string? sessionId = OptionalStr(args, "sessionId");
		if (string.IsNullOrEmpty(sessionId))
		{
			return await _services.Mcp.CallToolAsync(serverId, toolName, toolArgs);
		}

		CancellationTokenSource registered = _services.AgentOperations.Register(source.Label, sessionId, CancellationToken.None);
		try
		{
			return await _services.Mcp.CallToolAsync(serverId, toolName, toolArgs, registered.Token);
		}
		finally
		{
			_services.AgentOperations.Complete(source.Label, sessionId, registered);
		}
	}

	// ===================================================================
	// 调试 / 外壳
	// ===================================================================

	/// <summary>在资源管理器中打开日志目录 (只开放固定目录)</summary>
	private static void OpenLogFolder()
	{
		Directory.CreateDirectory(AppPaths.LogDir);
		Process.Start(new ProcessStartInfo {FileName = AppPaths.LogDir, UseShellExecute = true});
	}

	private object? RunGcCollect()
	{
		long before = GC.GetTotalMemory(false);
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true);
		long after = GC.GetTotalMemory(true);
		long released = Math.Max(0, before - after);
		_services.Logger.Write(LogSource.Backend, "info", $"调试垃圾回收完成: 释放 {released} 字节");
		return new {released_bytes = released};
	}

	private object? DebugCrashTest(string mode)
	{
		if (SentryTelemetry.IsProductionBuild)
			throw new InvalidOperationException("生产环境不支持调试崩溃测试");

		switch (mode)
		{
			case "ui_thread":
				Dispatcher.UIThread.Post(() => throw new InvalidOperationException("调试崩溃测试: UI 线程未处理异常"));
				break;
			case "background_thread":
				new Thread(() => throw new InvalidOperationException("调试崩溃测试: 后台线程未处理异常")).Start();
				break;
			case "unobserved_task":
				_ = Task.Run(() => throw new InvalidOperationException("调试崩溃测试: 未观察任务异常"));
				break;
			default:
				throw new InvalidOperationException($"未知的崩溃测试模式: {mode}");
		}
		return null;
	}

	private static async Task<object?> WriteClipboardAsync(IBridgeSource source, string text)
	{
		await OnUiAsync(async () =>
		{
			Avalonia.Input.Platform.IClipboard clipboard = TopLevel.GetTopLevel(source.Self)?.Clipboard
				?? throw new InvalidOperationException("剪贴板不可用");
			await clipboard.SetTextAsync(text);
		});
		return null;
	}

	// ===================================================================
	// 通用辅助
	// ===================================================================

	/// <summary>切到 UI 线程后向所有 WebView 窗口广播事件</summary>
	private void PostBroadcast(string name, object payload) => _postUi(() => _services.Windows.Broadcast(name, payload));

	/// <summary>
	/// 目标窗口: 参数里带 label 用 label, 否则用消息来源窗口
	///
	/// 返回基类 Window: 桌宠是原生 PetWindow 而非 NoriWindow, 按 NoriWindow 取会静默回退。
	/// </summary>
	private Window Target(IBridgeSource source, JsonElement args) =>
		_services.Windows.Get(OptionalLabel(args)) ?? source.Self
		?? throw new InvalidOperationException("目标窗口不存在");

	private static nint NativeHandleOf(Window window) => window.TryGetPlatformHandle()?.Handle ?? 0;

	private static string? OptionalLabel(JsonElement args) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty("label", out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static object OuterPosition(Window window) => new {x = window.Position.X, y = window.Position.Y};

	private static object OuterSize(Window window)
	{
		double scale = window.RenderScaling;
		return new
		{
			width = (int)Math.Round(window.FrameSize?.Width * scale ?? window.Bounds.Width * scale),
			height = (int)Math.Round(window.FrameSize?.Height * scale ?? window.Bounds.Height * scale),
		};
	}

	private static object? SetSize(Window window, JsonElement args)
	{
		double scale = window.RenderScaling;
		window.Width = Num(args, "width") / scale;
		window.Height = Num(args, "height") / scale;
		return null;
	}

	private static object? SetPosition(Window window, JsonElement args)
	{
		window.Position = new PixelPoint((int)Math.Round(Num(args, "x")), (int)Math.Round(Num(args, "y")));
		return null;
	}

	private float? ReadFloatConfig(string key)
	{
		string raw = _services.Config.GetStringOr(key, "");
		if (raw.Length == 0) return null;
		if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1f;
		if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0f;
		return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : null;
	}

	private static ResourceType ParseResourceType(string value) =>
		ResourceTypeExtensions.Parse(value) ?? throw new InvalidOperationException($"未知的资源类型: {value}");

	private static string Str(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
			: throw new InvalidOperationException($"缺少参数: {name}");

	private static bool RequiredBool(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object
			&& args.TryGetProperty(name, out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: throw new InvalidOperationException($"缺少参数: {name}");

	private static bool? OptionalBool(JsonElement args, string name)
	{
		if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value))
		{
			return value.ValueKind switch
			{
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				_ => null,
			};
		}
		return null;
	}

	private static string? OptionalStr(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool HasTtsConfigurationChange(JsonElement args)
	{
		if (args.ValueKind != JsonValueKind.Object) return false;
		string[] keys = [
			"ttsProvider", "ttsBaseUrl", "ttsApiKey", "ttsVoice", "ttsSpeed",
			"gptsovitsBaseUrl", "gptsovitsRefAudio", "gptsovitsPromptText", "gptsovitsPromptLang",
		];
		return keys.Any(key => args.TryGetProperty(key, out _));
	}

	private static double Num(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? value.GetDouble()
			: throw new InvalidOperationException($"缺少参数: {name}");

	private static double? OptionalDouble(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? value.GetDouble()
			: null;

	private static int? OptionalInt(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? value.GetInt32()
			: null;

	private static int ClampLimit(int? value, int fallback) => Math.Clamp(value ?? fallback, 1, 100);

	private static McpServerConfig ParseMcpConfig(JsonElement args)
	{
		McpServerConfig? config = args.Deserialize<McpServerConfig>(BridgeJson.Options);
		return config ?? throw new InvalidOperationException("无法解析 MCP 服务器配置");
	}

	private static object? Run(Func<object?> action)
	{
		action();
		return null;
	}

	private static object? Run(Action action)
	{
		action();
		return null;
	}

	private static Task<T> OnUi<T>(Func<T> action) =>
		Dispatcher.UIThread.CheckAccess() ? Task.FromResult(action()) : Dispatcher.UIThread.InvokeAsync(action).GetTask();

	private static Task OnUiAsync(Func<Task> action) =>
		Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
}
