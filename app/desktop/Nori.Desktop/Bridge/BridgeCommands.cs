using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Logging;
using Nori.Core.Mcp;
using Nori.Core.Platform;
using Nori.Core.Resources;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Bridge;

/// <summary>
/// 桥接命令
///
/// 对应原 Rust 版 commands.rs / config.rs / chat.rs / system.rs 里所有 #[tauri::command],
/// 外加窗口操作与两个插件 (opener / clipboard) 的替代实现.
/// 命令名保持 snake_case 且动词开头, 与前端 invoke("xxx") 完全一致.
/// </summary>
public sealed class BridgeCommands(AppServices services)
{
	private readonly AppServices _services = services;

	/// <summary>
	/// 分发一次命令调用
	/// </summary>
	public async Task<object?> InvokeAsync(NoriWindow source, string cmd, JsonElement args) => cmd switch
	{
		// ---- 应用 ----
		// invoke("exit_app")
		"exit_app" => await OnUi(() =>
		{
			_services.Windows.Shutdown();
			return (object?)null;
		}),

		// invoke("complete_first_run")
		"complete_first_run" => await CompleteFirstRunAsync(source),

		// invoke("write_log", {level: "info", message: "xxx"})
		"write_log" => Run(() => _services.Logger.Write(LogSource.Frontend, Str(args, "level"), Str(args, "message"))),

		// invoke("get_system_language")
		"get_system_language" => ConfigStore.SystemLanguage(),

		// ---- 配置 ----
		// invoke("get_config", {key: "selected_model"})
		"get_config" => _services.Config.Get(Str(args, "key")),

		// invoke("set_config", {key: "l2d_scale_arg-nori", value: "1.25"})
		"set_config" => SetConfig(Str(args, "key"), args.GetProperty("value")),

		// invoke("delete_config", {key: "xxx"})
		"delete_config" => _services.Config.Delete(Str(args, "key")),

		// invoke("has_config", {key: "xxx"})
		"has_config" => _services.Config.Exists(Str(args, "key")),

		// invoke("get_all_configs")
		// 返回 [[key, value], ...], 与 Rust 的 Vec<(String, ConfigValue)> 序列化形态一致
		"get_all_configs" => _services.Config.GetAll().Select(pair => new object?[] {pair.Key, pair.Value}).ToArray(),

		// invoke("get_init_config")
		"get_init_config" => _services.Config.GetInitConfig(),

		// ---- 资源 ----
		// invoke("check_resource", {resourceType: "live2d", name: "arg-nori"})
		"check_resource" => _services.Resources.IsInstalled(ParseResourceType(Str(args, "resourceType")), Str(args, "name")),

		// invoke("import_local_resource", {filePath?: "C:/...", resourceType?: "live2d"})
		"import_local_resource" => await ImportLocalResourceAsync(source, args),

		// ---- 系统 ----
		// invoke("get_cursor_pos") → [x, y] (物理像素)
		"get_cursor_pos" => CursorPosition(),

		// ---- 聊天 ----
		// invoke("get_chat_history", {limit?: 50, beforeId?: 0})
		// 历史表随使用无限增长, 界面加载必须带 limit 分页; 不带参数时保持旧的全量行为
		"get_chat_history" => _services.Chat.GetHistory(OptionalInt(args, "limit") ?? 0, (long)(OptionalDouble(args, "beforeId") ?? 0)),

		// invoke("chat_completion", {provider?, baseUrl, apiKey, model, messages})
		"chat_completion" => await ChatCompletionAsync(args),

		// invoke("chat_completion_stream", {provider?, baseUrl, apiKey, model, messages, streamId?})
		"chat_completion_stream" => await ChatCompletionStreamAsync(source, args),

		// invoke("fetch_llm_models", {provider?, baseUrl, apiKey})
		"fetch_llm_models" => await _services.Llm.FetchModelsAsync(OptionalStr(args, "provider"), Str(args, "baseUrl"), Str(args, "apiKey")),

		// ---- 记忆库与向量 Embedding ----
		// invoke("create_embedding", {text, baseUrl?, apiKey?, model?, dimensions?})
		"create_embedding" => await CreateEmbeddingAsync(args),
		// invoke("add_memory", {type?, content, importance?, source?, tags?, embedding?})
		"add_memory" => _services.Memory.Add(
			OptionalStr(args, "type") ?? "general",
			Str(args, "content"),
			OptionalDouble(args, "importance") ?? 0.5,
			OptionalStr(args, "source") ?? "chat",
			OptionalStr(args, "tags"),
			OptionalStr(args, "embedding")),
		// invoke("get_all_memories", {limit?})
		"get_all_memories" => _services.Memory.GetAll(OptionalInt(args, "limit") ?? 100),
		// invoke("search_memories", {keyword, limit?})
		"search_memories" => _services.Memory.Search(Str(args, "keyword"), OptionalInt(args, "limit") ?? 20),
		// invoke("search_memories_semantic", {vector, limit?, minSimilarity?})
		"search_memories_semantic" => _services.Memory.SearchSemantic(
			ParseFloatArray(args, "vector") ?? throw new InvalidOperationException("缺少 vector 向量参数"),
			OptionalInt(args, "limit") ?? 10,
			OptionalDouble(args, "minSimilarity") ?? 0.25),
		// invoke("search_memories_hybrid", {keyword, vector?, limit?})
		"search_memories_hybrid" => _services.Memory.SearchHybrid(
			Str(args, "keyword"),
			ParseFloatArray(args, "vector"),
			OptionalInt(args, "limit") ?? 10),
		// invoke("update_memory_embedding", {id, embedding})
		"update_memory_embedding" => _services.Memory.UpdateEmbedding((long)Num(args, "id"), Str(args, "embedding")),
		// invoke("update_memory", {id, content, importance?, tags?})
		"update_memory" => _services.Memory.Update(
			(long)Num(args, "id"),
			Str(args, "content"),
			OptionalDouble(args, "importance"),
			OptionalStr(args, "tags")),
		// invoke("delete_memory", {id})
		"delete_memory" => _services.Memory.Delete((long)Num(args, "id")),
		// invoke("clear_memories")
		"clear_memories" => Run(() => _services.Memory.Clear()),

		// ---- 对话历史持久化 (参考 AstrBot conversation_mgr) ----
		// invoke("save_chat_message", {role, content})
		"save_chat_message" => _services.Chat.SaveMessage(Str(args, "role"), Str(args, "content")),
		// invoke("clear_chat_history")
		"clear_chat_history" => Run(() => _services.Chat.ClearHistory()),

		// ---- MCP (Model Context Protocol) ----
		// invoke("mcp_get_servers")
		"mcp_get_servers" => await _services.Mcp.GetServersAsync(),
		// invoke("mcp_save_server", {id, name, transport, command?, args?, env?, url?, enabled, autoConnect})
		"mcp_save_server" => await _services.Mcp.SaveServerAsync(ParseMcpConfig(args)),
		// invoke("mcp_delete_server", {id: "xxx"})
		"mcp_delete_server" => await _services.Mcp.DeleteServerAsync(Str(args, "id")),
		// invoke("mcp_connect_server", {id: "xxx"})
		"mcp_connect_server" => await _services.Mcp.ConnectServerAsync(Str(args, "id")),
		// invoke("mcp_disconnect_server", {id: "xxx"})
		"mcp_disconnect_server" => await _services.Mcp.DisconnectServerAsync(Str(args, "id")),
		// invoke("mcp_list_tools")
		"mcp_list_tools" => await _services.Mcp.GetAllToolsAsync(),
		// invoke("mcp_call_tool", {serverId, toolName, arguments})
		"mcp_call_tool" => await CallMcpToolAsync(args),
		// invoke("mcp_test_server", {id, name, transport, command?, args?, env?, url?})
		"mcp_test_server" => await _services.Mcp.TestServerAsync(ParseMcpConfig(args)),

		// ---- 桌宠 Live2D 原生控制 ----
		// invoke("pet_get_state")
		"pet_get_state" => GetPetState(),
		// invoke("pet_play_motion", {name?, group?, index?})
		"pet_play_motion" => PlayPetMotion(args),
		// invoke("pet_play_expression", {name: "xxx"})
		"pet_play_expression" => Run(() => _services.PetRuntime.PlayExpression(Str(args, "name"))),
		// invoke("pet_stop_expression")
		"pet_stop_expression" => Run(() => _services.PetRuntime.StopExpression()),
		// invoke("pet_toggle_expression", {name: "xxx"})
		"pet_toggle_expression" => Run(() => _services.PetRuntime.ToggleExpression(Str(args, "name"))),
		// invoke("pet_get_motions")
		"pet_get_motions" => _services.PetRuntime.MotionGroups,
		// invoke("pet_get_expressions")
		"pet_get_expressions" => _services.PetRuntime.Expressions,
		// invoke("pet_set_mouth_open", {value: 0.5, speaking: true})
		"pet_set_mouth_open" => Run(() => _services.PetRuntime.SetMouthOpen((float)Num(args, "value"), OptionalBool(args, "speaking") ?? true)),
		// invoke("pet_reload_model", {modelId?: "arg-nori"})
		"pet_reload_model" => Run(() => _services.PetRuntime.RequestModelLoad(OptionalStr(args, "modelId") ?? _services.PetRuntime.CurrentModelId)),
		// invoke("pet_trigger_beat", {timestamp?})
		"pet_trigger_beat" => Run(() => _services.PetRuntime.TriggerBeat(OptionalDouble(args, "timestamp"))),

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
		"open_url" => Run(() => OpenUrl(Str(args, "url"))),

		// invoke("fetch_remote_text", {url: "https://..."})
		"fetch_remote_text" => await _services.Http.GetStringAsync(Str(args, "url")),

		// invoke("search_anysearch", {query, tag?, params?, endpoint?, apiKey?})
		"search_anysearch" => await SearchAnySearchAsync(args),

		// invoke("clipboard_write_text", {text: "..."})
		"clipboard_write_text" => await WriteClipboardAsync(source, Str(args, "text")),

		_ => throw new InvalidOperationException($"未知的命令: {cmd}"),
	};

	/// <summary>
	/// 首次启动完成: 只允许可见的 first-run 窗口调用
	/// </summary>
	private async Task<object?> CompleteFirstRunAsync(NoriWindow source)
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
		_services.Config.MarkFirstRunCompleted();
		_services.Config.MarkInitialized();
		_services.Logger.Write(LogSource.Backend, "info", "首次初始化完成");

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
	/// 获取桌宠运行时状态
	/// </summary>
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
			maxFps = pet?.MaxFps ?? 0,
		};
	}

	/// <summary>
	/// 播放桌宠动作
	/// </summary>
	private object? PlayPetMotion(JsonElement args)
	{
		if (_services.PetRuntime is null) return false;
		string? name = OptionalStr(args, "name");
		if (!string.IsNullOrEmpty(name))
		{
			return _services.PetRuntime.PlayMotionByName(name);
		}
		string? group = OptionalStr(args, "group");
		int? index = OptionalInt(args, "index");
		if (!string.IsNullOrEmpty(group) && index.HasValue)
		{
			return _services.PetRuntime.PlayMotionByIndex(group, index.Value);
		}
		_services.PetRuntime.PlayRandomMotion();
		return true;
	}

	/// <summary>
	/// 写配置并全局广播, 桌宠窗口据此热更新模型与显示参数
	/// </summary>
	private object? SetConfig(string key, JsonElement rawValue)
	{
		ConfigValue? value = rawValue.Deserialize<ConfigValue>(BridgeJson.Options);
		if (value is null) throw new InvalidOperationException("配置值不能为空");
		_services.Config.Set(key, value);
		string storage = value.ToStorage();
		_services.PetRuntime?.ApplyConfig(key, storage);
		Dispatcher.UIThread.Post(() => _services.Windows.Broadcast("nori:config-changed", new {key, value = storage}));
		return null;
	}

	/// <summary>
	/// 一次对话, 动作标记剥离后广播给桌宠播放
	/// </summary>
	private async Task<object?> ChatCompletionAsync(JsonElement args)
	{
		ChatMessageInput[] messages = args.GetProperty("messages").Deserialize<ChatMessageInput[]>(BridgeJson.Options) ?? [];
		return await _services.Chat.CompleteAsync(
			OptionalStr(args, "provider"),
			Str(args, "baseUrl"),
			Str(args, "apiKey"),
			Str(args, "model"),
			messages,
			motion => Dispatcher.UIThread.Post(() =>
			{
				_services.PetRuntime?.PlayMotionByName(motion);
				_services.Windows.Broadcast("nori:play-motion", new {name = motion});
			}),
			OptionalBool(args, "persist") ?? true);
	}

	/// <summary>
	/// 一次流式对话, 逐 chunk 通过 nori:chat-chunk 事件向来源窗口推送
	///
	/// LLM 高速输出时每个 chunk 都走一次 UI Post + 序列化 + InvokeScript,
	/// 开销全花在小信封上, 这里用 ChunkBatcher 按 ~33ms 合帧。
	/// </summary>
	private async Task<object?> ChatCompletionStreamAsync(NoriWindow source, JsonElement args)
	{
		ChatMessageInput[] messages = args.GetProperty("messages").Deserialize<ChatMessageInput[]>(BridgeJson.Options) ?? [];
		string streamId = OptionalStr(args, "streamId") ?? Guid.NewGuid().ToString("N");
		ChunkBatcher batcher = new(source, streamId);
		string final = await _services.Chat.StreamAsync(
			OptionalStr(args, "provider"),
			Str(args, "baseUrl"),
			Str(args, "apiKey"),
			Str(args, "model"),
			messages,
			batcher.Append,
			motion => Dispatcher.UIThread.Post(() =>
			{
				_services.PetRuntime?.PlayMotionByName(motion);
				_services.Windows.Broadcast("nori:play-motion", new {name = motion});
			}),
			usage => Dispatcher.UIThread.Post(() => source.PostEvent("nori:chat-usage", new
			{
				streamId,
				promptTokens = usage.PromptTokens,
				completionTokens = usage.CompletionTokens,
				totalTokens = usage.TotalTokens,
				cachedTokens = usage.CachedTokens,
				cacheHitRate = usage.CacheHitRate,
				durationMs = usage.DurationMs,
				model = usage.Model,
			})),
			OptionalBool(args, "persist") ?? true);

		// 流结束: 残余缓冲必须在 invoke 返回前发出, 否则前端会丢尾巴。
		// PostResult 也走 UI 线程 FIFO 队列, 这里的先发保证 chunk 先于 resolve 到达页面。
		batcher.FlushNow();
		return final;
	}

	/// <summary>
	/// 聊天流 chunk 合批器
	///
	/// 把到达的 chunk 攒进缓冲, 首个 chunk 到达后排一次 UI 线程刷新,
	/// 刷新时把期间积压的一并带走; 流结束时 FlushNow 保证不丢尾。
	/// </summary>
	private sealed class ChunkBatcher(NoriWindow source, string streamId)
	{
		private readonly object _gate = new();
		private readonly System.Text.StringBuilder _buffer = new();
		private bool _flushQueued;

		public void Append(string chunk)
		{
			bool schedule = false;
			lock (_gate)
			{
				_buffer.Append(chunk);
				if (!_flushQueued)
				{
					_flushQueued = true;
					schedule = true;
				}
			}
			if (schedule)
			{
				Dispatcher.UIThread.Post(Flush);
			}
		}

		/// <summary>流结束前调用: 立即送出残余缓冲</summary>
		public void FlushNow() => Dispatch(Drain());

		private void Flush() => Dispatch(Drain());

		/// <summary>取走全部积压内容; 无内容返回 null 并允许下次重新排队</summary>
		private string? Drain()
		{
			lock (_gate)
			{
				_flushQueued = false;
				if (_buffer.Length == 0) return null;
				string payload = _buffer.ToString();
				_buffer.Clear();
				return payload;
			}
		}

		private void Dispatch(string? payload)
		{
			if (string.IsNullOrEmpty(payload)) return;
			source.PostEvent("nori:chat-chunk", new {streamId, chunk = payload, done = false});
		}
	}

	/// <summary>
	/// 用系统默认程序打开链接
	/// </summary>
	private static void OpenUrl(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || parsed.Scheme is not ("http" or "https"))
		{
			throw new InvalidOperationException($"不允许打开的链接: {url}");
		}
		Process.Start(new ProcessStartInfo(parsed.ToString()) {UseShellExecute = true});
	}

	/// <summary>
	/// 写入剪贴板
	/// </summary>
	private static async Task<object?> WriteClipboardAsync(NoriWindow source, string text)
	{
		IClipboard clipboard = await OnUi(() => TopLevel.GetTopLevel(source)?.Clipboard)
			?? throw new InvalidOperationException("剪贴板不可用");
		await clipboard.SetTextAsync(text);
		return null;
	}

	/// <summary>
	/// 从本地 ZIP 文件或目录导入资源
	/// </summary>
	private async Task<object?> ImportLocalResourceAsync(NoriWindow source, JsonElement args)
	{
		string? filePath = OptionalStr(args, "filePath");
		if (string.IsNullOrWhiteSpace(filePath))
		{
			filePath = await Dispatcher.UIThread.InvokeAsync(async () =>
			{
				var files = await source.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
		IReadOnlyList<string> imported = _services.Resources.Import(type, filePath);
		_services.Logger.Write(LogSource.Backend, "info", $"成功导入本地资源: {filePath} -> {string.Join(", ", imported)}");

		// 广播资源更新
		_services.Windows.Broadcast("nori:config-changed", new { key = "resource_imported", value = string.Join(",", imported) });

		return imported;
	}

	/// <summary>
	/// 目标窗口: 参数里带 label 用 label, 否则用消息来源窗口
	///
	/// 返回基类 Window: 桌宠是原生 PetWindow 而非 NoriWindow, 如果这里按 NoriWindow 取,
	/// {label: "pet"} 会取不到而静默回退到调用方窗口, 让 window_is_visible 之类的命令
	/// 报告主窗口的状态 (主界面的唤出按钮因此永远以为桌宠已显示).
	/// </summary>
	private Window Target(NoriWindow source, JsonElement args) =>
		_services.Windows.Get(OptionalLabel(args)) ?? source;

	/// <summary>
	/// 窗口原生句柄 (拖动用), 取不到返回 0
	/// </summary>
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

	/// <summary>
	/// 按物理像素设置窗口尺寸 (前端传的是 PhysicalSize)
	/// </summary>
	private static object? SetSize(Window window, JsonElement args)
	{
		double scale = window.RenderScaling;
		window.Width = Num(args, "width") / scale;
		window.Height = Num(args, "height") / scale;
		return null;
	}

	/// <summary>
	/// 按物理像素设置窗口位置
	/// </summary>
	private static object? SetPosition(Window window, JsonElement args)
	{
		window.Position = new PixelPoint((int)Math.Round(Num(args, "x")), (int)Math.Round(Num(args, "y")));
		return null;
	}

	private static ResourceType ParseResourceType(string value) =>
		ResourceTypeExtensions.Parse(value) ?? throw new InvalidOperationException($"未知的资源类型: {value}");

	private static string Str(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
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

	private static float[]? ParseFloatArray(JsonElement args, string name)
	{
		if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement elem) && elem.ValueKind == JsonValueKind.Array)
		{
			int count = elem.GetArrayLength();
			float[] arr = new float[count];
			int i = 0;
			foreach (JsonElement item in elem.EnumerateArray())
			{
				arr[i++] = (float)item.GetDouble();
			}
			return arr;
		}
		return null;
	}

	private static McpServerConfig ParseMcpConfig(JsonElement args)
	{
		McpServerConfig? config = args.Deserialize<McpServerConfig>(BridgeJson.Options);
		return config ?? throw new InvalidOperationException("无法解析 MCP 服务器配置");
	}

	private async Task<object?> CallMcpToolAsync(JsonElement args)
	{
		string serverId = Str(args, "serverId");
		string toolName = Str(args, "toolName");
		JsonObject? toolArgs = null;
		if (args.TryGetProperty("arguments", out JsonElement argElem) && argElem.ValueKind == JsonValueKind.Object)
		{
			toolArgs = JsonNode.Parse(argElem.GetRawText()) as JsonObject;
		}
		return await _services.Mcp.CallToolAsync(serverId, toolName, toolArgs);
	}

	private async Task<object?> SearchAnySearchAsync(JsonElement args)
	{
		string query = Str(args, "query");
		string tag = OptionalStr(args, "tag") ?? "general";
		string baseUrl = OptionalStr(args, "endpoint") ?? _services.Config.Get("anysearch_api_base")?.ToStorage() ?? "https://api.anysearch.com/v1/search";
		string? apiKey = OptionalStr(args, "apiKey") ?? _services.Config.Get("anysearch_api_key")?.ToStorage();

		JsonObject payload = new()
		{
			["query"] = query,
			["tag"] = tag,
		};

		if (args.TryGetProperty("params", out JsonElement paramsElem) && paramsElem.ValueKind == JsonValueKind.Object)
		{
			payload["params"] = JsonNode.Parse(paramsElem.GetRawText());
		}

		using HttpRequestMessage req = new(HttpMethod.Post, baseUrl)
		{
			Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
		};

		if (!string.IsNullOrEmpty(apiKey))
		{
			req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
		}

		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
		using HttpResponseMessage resp = await _services.Http.SendAsync(req, cts.Token);
		string responseText = await resp.Content.ReadAsStringAsync(cts.Token);

		if (!resp.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"AnySearch API 返回 HTTP {(int)resp.StatusCode}: {responseText}");
		}

		try
		{
			return JsonNode.Parse(responseText);
		}
		catch
		{
			return responseText;
		}
	}

	private async Task<object?> CreateEmbeddingAsync(JsonElement args)
	{
		string text = Str(args, "text");
		string? baseUrl = OptionalStr(args, "baseUrl") ?? _services.Config.Get("embedding_api_base")?.ToStorage() ?? _services.Config.Get("llm_api_base")?.ToStorage() ?? "https://api.openai.com/v1";
		string? apiKey = OptionalStr(args, "apiKey") ?? _services.Config.Get("embedding_api_key")?.ToStorage() ?? _services.Config.Get("llm_api_key")?.ToStorage() ?? "";
		string? model = OptionalStr(args, "model") ?? _services.Config.Get("embedding_model")?.ToStorage() ?? "BAAI/bge-m3";

		// 维数: 调用方显式传入优先, 否则读配置; 非法值一律视为不指定 (用模型默认维数)
		int? dimensions = OptionalInt(args, "dimensions") ?? ParsePositiveConfigInt("embedding_dimensions");

		return await _services.Embedding.GetEmbeddingAsync(baseUrl, apiKey, model, text, dimensions);
	}

	/// <summary>
	/// 读取正整数配置, 缺失或非法时返回 null
	/// </summary>
	private int? ParsePositiveConfigInt(string key)
	{
		string raw = _services.Config.GetStringOr(key, "").Trim();
		return int.TryParse(raw, out int value) && value > 0 ? value : null;
	}

	/// <summary>
	/// 执行一个无返回值的动作, 统一成 object? 返回
	/// </summary>
	private static object? Run(Action action)
	{
		action();
		return null;
	}

	/// <summary>
	/// 切到 UI 线程执行 (窗口操作与 InvokeScript 都必须在 UI 线程)
	/// </summary>
	private static Task<T> OnUi<T>(Func<T> action) =>
		Dispatcher.UIThread.CheckAccess() ? Task.FromResult(action()) : Dispatcher.UIThread.InvokeAsync(action).GetTask();
}
