using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Logging;
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
	/// <summary>下载进度事件节流间隔: Rust 版每 64KiB 发一次, 经 JSON 桥会成为热点</summary>
	private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(100);

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

		// invoke("ensure_resource", {resourceType: "live2d", name: "arg-nori"})
		"ensure_resource" => await EnsureResourceAsync(Str(args, "resourceType"), Str(args, "name")),

		// invoke("import_local_resource", {filePath?: "C:/...", resourceType?: "live2d"})
		"import_local_resource" => await ImportLocalResourceAsync(source, args),

		// ---- 系统 ----
		// invoke("get_cursor_pos") → [x, y] (物理像素)
		"get_cursor_pos" => CursorPosition(),

		// ---- 聊天 ----
		// invoke("get_chat_history")
		"get_chat_history" => _services.Chat.GetHistory(),

		// invoke("chat_completion", {provider?, baseUrl, apiKey, model, messages})
		"chat_completion" => await ChatCompletionAsync(args),

		// invoke("chat_completion_stream", {provider?, baseUrl, apiKey, model, messages, streamId?})
		"chat_completion_stream" => await ChatCompletionStreamAsync(source, args),

		// invoke("fetch_llm_models", {provider?, baseUrl, apiKey})
		"fetch_llm_models" => await _services.Llm.FetchModelsAsync(OptionalStr(args, "provider"), Str(args, "baseUrl"), Str(args, "apiKey")),

		// ---- 记忆库 ----
		// invoke("add_memory", {type?, content, importance?, source?, tags?})
		"add_memory" => _services.Memory.Add(
			OptionalStr(args, "type") ?? "general",
			Str(args, "content"),
			OptionalDouble(args, "importance") ?? 0.5,
			OptionalStr(args, "source") ?? "chat",
			OptionalStr(args, "tags")),
		// invoke("get_all_memories", {limit?})
		"get_all_memories" => _services.Memory.GetAll(OptionalInt(args, "limit") ?? 100),
		// invoke("search_memories", {keyword, limit?})
		"search_memories" => _services.Memory.Search(Str(args, "keyword"), OptionalInt(args, "limit") ?? 20),
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
		"window_start_drag" => await OnUi(() => Run(() => PlatformServices.Current.StartWindowDrag(Target(source, args).NativeHandle))),

		// ---- 插件替代 ----
		// invoke("open_url", {url: "https://..."})
		"open_url" => Run(() => OpenUrl(Str(args, "url"))),

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
	/// 写配置并全局广播, 桌宠窗口据此热更新模型与显示参数
	/// </summary>
	private object? SetConfig(string key, JsonElement rawValue)
	{
		ConfigValue? value = rawValue.Deserialize<ConfigValue>(BridgeJson.Options);
		if (value is null) throw new InvalidOperationException("配置值不能为空");
		_services.Config.Set(key, value);
		string storage = value.ToStorage();
		Dispatcher.UIThread.Post(() => _services.Windows.Broadcast("nori:config-changed", new {key, value = storage}));
		return null;
	}

	/// <summary>
	/// 确保资源就位, 各阶段实时推 resource-download 事件
	/// </summary>
	private async Task<object?> EnsureResourceAsync(string rawType, string name)
	{
		ResourceType type = ParseResourceType(rawType);
		string typeName = type.AsString();
		DateTime lastProgress = DateTime.MinValue;

		void Emit(ResourceStep step)
		{
			// 只有下载中需要节流, 其余阶段每个都必须送达 (前端靠它们推进文案)
			if (step.Step == "downloading")
			{
				DateTime now = DateTime.UtcNow;
				bool finished = step.Progress is >= 100f;
				if (!finished && now - lastProgress < ProgressThrottle) return;
				lastProgress = now;
			}
			object payload = new
			{
				resourceType = typeName,
				step = step.Step,
				progress = step.Progress,
				downloaded = step.Downloaded,
				total = step.Total,
				message = step.Message,
			};
			Dispatcher.UIThread.Post(() => _services.Windows.Broadcast("resource-download", payload));
		}

		try
		{
			_services.Logger.Write(LogSource.Backend, "info", $"确保资源: type={typeName} name={name}");
			await _services.Resources.EnsureAsync(type, name, Emit);
			return null;
		}
		catch (Exception exception)
		{
			_services.Logger.Write(LogSource.Backend, "error", $"资源就位失败: type={typeName} name={name} error={exception.Message}");
			Emit(ResourceStep.Error(exception.Message));
			throw;
		}
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
			motion => Dispatcher.UIThread.Post(() => _services.Windows.Broadcast("nori:play-motion", new {name = motion})));
	}

	/// <summary>
	/// 一次流式对话, 逐 chunk 通过 nori:chat-chunk 事件向来源窗口推送
	/// </summary>
	private async Task<object?> ChatCompletionStreamAsync(NoriWindow source, JsonElement args)
	{
		ChatMessageInput[] messages = args.GetProperty("messages").Deserialize<ChatMessageInput[]>(BridgeJson.Options) ?? [];
		string streamId = OptionalStr(args, "streamId") ?? Guid.NewGuid().ToString("N");
		return await _services.Chat.StreamAsync(
			OptionalStr(args, "provider"),
			Str(args, "baseUrl"),
			Str(args, "apiKey"),
			Str(args, "model"),
			messages,
			chunk => Dispatcher.UIThread.Post(() => source.PostEvent("nori:chat-chunk", new {streamId, chunk, done = false})),
			motion => Dispatcher.UIThread.Post(() => _services.Windows.Broadcast("nori:play-motion", new {name = motion})));
	}

	/// <summary>
	/// 全局光标位置与鼠标按键状态, 返回 [x, y, isDown] (物理像素)
	/// </summary>
	private object CursorPosition()
	{
		(double x, double y) = PlatformServices.Current.GetCursorPosition();
		bool isDown = PlatformServices.Current.IsMouseButtonDown(0);
		return new object[] {x, y, isDown};
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
	/// </summary>
	private NoriWindow Target(NoriWindow source, JsonElement args) =>
		_services.Windows.Get(OptionalLabel(args)) ?? source;

	private static string? OptionalLabel(JsonElement args) =>
		args.ValueKind == JsonValueKind.Object && args.TryGetProperty("label", out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static object OuterPosition(NoriWindow window) => new {x = window.Position.X, y = window.Position.Y};

	private static object OuterSize(NoriWindow window)
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
	private static object? SetSize(NoriWindow window, JsonElement args)
	{
		double scale = window.RenderScaling;
		window.Width = Num(args, "width") / scale;
		window.Height = Num(args, "height") / scale;
		return null;
	}

	/// <summary>
	/// 按物理像素设置窗口位置
	/// </summary>
	private static object? SetPosition(NoriWindow window, JsonElement args)
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
