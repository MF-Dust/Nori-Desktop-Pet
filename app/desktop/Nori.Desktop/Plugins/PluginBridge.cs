using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nori.Core.Logging;
using Nori.Core.Security;
using Nori.Desktop.Bridge;
using Nori.Plugin.Abstractions;

namespace Nori.Desktop.Plugins;

/// <summary>
/// 插件 Web 视图窗口宿主源抽象
///
/// 供 PluginBridge 与窗口进行解耦交互 (回推结果、发送事件、请求关窗),
/// 便于单元测试提供纯逻辑替身.
/// </summary>
public interface IPluginBridgeSource
{
	/// <summary>所属插件 ID</summary>
	string PluginId { get; }

	/// <summary>窗口 ID</summary>
	string WindowId { get; }

	/// <summary>窗口全局标签 (plugin:{pluginId}:{windowId})</summary>
	string Label { get; }

	/// <summary>窗口当前是否可见</summary>
	bool IsVisible { get; }

	/// <summary>向插件页面回推调用结果</summary>
	void PostResult(long id, object? value, string? error);

	/// <summary>向插件页面推送事件</summary>
	void PostEvent(string name, object? payload);

	/// <summary>请求关闭此窗口</summary>
	Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 插件页面发给宿主的消息信封
/// </summary>
public sealed record PluginBridgeMessage
{
	/// <summary>消息种类: invoke / emit</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; init; } = "";

	/// <summary>调用序号</summary>
	[JsonPropertyName("id")]
	public long Id { get; init; }

	/// <summary>命令名</summary>
	[JsonPropertyName("cmd")]
	public string? Cmd { get; init; }

	/// <summary>命令参数</summary>
	[JsonPropertyName("args")]
	public JsonElement Args { get; init; }

	/// <summary>事件名</summary>
	[JsonPropertyName("event")]
	public string? Event { get; init; }

	/// <summary>事件载荷</summary>
	[JsonPropertyName("payload")]
	public JsonElement Payload { get; init; }
}

/// <summary>
/// 宿主回推给插件页面的调用结果
/// </summary>
public sealed record PluginBridgeResult
{
	/// <summary>结果种类: resolve / reject</summary>
	[JsonPropertyName("kind")]
	public required string Kind { get; init; }

	/// <summary>调用序号</summary>
	[JsonPropertyName("id")]
	public required long Id { get; init; }

	/// <summary>成功返回值</summary>
	[JsonPropertyName("value")]
	public object? Value { get; init; }

	/// <summary>失败信息 (脱敏文本)</summary>
	[JsonPropertyName("error")]
	public string? Error { get; init; }
}

/// <summary>
/// 插件独立桥接通信内核
///
/// 遵循最小特权原则:
/// 1. 构造时强绑定 pluginId 与 windowId, 绝不信任前端传入的身份标识;
/// 2. 仅开放极少数安全命令白名单 (插件信息、能力状态、关闭窗口、心跳);
/// 3. 绝不转发给 NoriBridge, 不向插件暴露宿主核心命令 (如 AI/配置/主窗口操控);
/// 4. 异常统一包装为脱敏后的稳定错误响应.
/// </summary>
public sealed class PluginBridge : IAsyncDisposable
{
	private readonly string _pluginId;
	private readonly string _windowId;
	private readonly PluginDescriptorSummary _descriptor;
	private readonly Func<string, IReadOnlyList<string>>? _capabilityProvider;
	private readonly Func<CancellationToken, Task>? _closeSelfHandler;
	private readonly FileLogger? _logger;
	private readonly ConcurrentDictionary<Task, byte> _pendingInvokes = new();
	private readonly CancellationTokenSource _cts = new();
	private int _disposed;

	/// <summary>绑定的插件 ID</summary>
	public string PluginId => _pluginId;

	/// <summary>绑定的窗口 ID</summary>
	public string WindowId => _windowId;

	/// <summary>脱敏的插件描述符摘要</summary>
	public PluginDescriptorSummary Descriptor => _descriptor;

	public PluginBridge(
		string pluginId,
		string windowId,
		PluginDescriptorSummary descriptor,
		Func<string, IReadOnlyList<string>>? capabilityProvider = null,
		Func<CancellationToken, Task>? closeSelfHandler = null,
		FileLogger? logger = null)
	{
		PluginWindowHost.ValidatePluginId(pluginId, nameof(pluginId));
		PluginWindowHost.ValidateId(windowId, nameof(windowId));
		ArgumentNullException.ThrowIfNull(descriptor);

		_pluginId = pluginId;
		_windowId = windowId;
		_descriptor = descriptor;
		_capabilityProvider = capabilityProvider;
		_closeSelfHandler = closeSelfHandler;
		_logger = logger;
	}

	/// <summary>
	/// 处理插件页面发来的原始 JSON 消息
	/// </summary>
	public void Handle(IPluginBridgeSource source, string raw)
	{
		if (Volatile.Read(ref _disposed) != 0) return;
		PluginBridgeMessage? message;
		try
		{
			message = JsonSerializer.Deserialize<PluginBridgeMessage>(raw, BridgeJson.Options);
		}
		catch (JsonException exception)
		{
			_logger?.Write(LogSource.Backend, "warn", $"插件桥接消息解析失败 [{_pluginId}:{_windowId}]: {exception.Message}");
			return;
		}

		if (message is null) return;

		switch (message.Kind)
		{
			case "invoke":
				TrackInvoke(source, message);
				break;
			case "emit":
				// 当前阶段插件 Web 视图的 emit 暂不向全局广播，避免污染主宿主事件总线
				_logger?.Write(LogSource.Backend, "debug", $"插件事件发射 [{_pluginId}:{_windowId}]: {message.Event}");
				break;
			default:
				_logger?.Write(LogSource.Backend, "warn", $"未知的插件桥接消息种类 [{_pluginId}:{_windowId}]: {message.Kind}");
				break;
		}
	}

	private void TrackInvoke(IPluginBridgeSource source, PluginBridgeMessage message)
	{
		Task task = Task.Run(() => HandleInvokeObservedAsync(source, message), CancellationToken.None);
		_pendingInvokes.TryAdd(task, 0);
		_ = task.ContinueWith(
			completed => _pendingInvokes.TryRemove(completed, out _),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private async Task HandleInvokeObservedAsync(IPluginBridgeSource source, PluginBridgeMessage message)
	{
		try
		{
			await HandleInvokeAsync(source, message, _cts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
			// 退出或销毁时静默忽略
		}
		catch (Exception exception)
		{
			_logger?.Write(LogSource.Backend, "error", $"插件桥接执行发生未捕获异常 [{_pluginId}:{_windowId}]: {SensitiveDataRedactor.ExceptionSummary(exception)}");
		}
	}

	/// <summary>
	/// 执行命令路由 (安全白名单校验)
	/// </summary>
	private async Task HandleInvokeAsync(IPluginBridgeSource source, PluginBridgeMessage message, CancellationToken cancellationToken)
	{
		string cmd = message.Cmd ?? "";
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			object? result = await ExecuteCommandAsync(source, cmd, message.Args, cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			source.PostResult(message.Id, result, null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// 窗口或插件上下文关闭时不回写结果
		}
		catch (Exception exception)
		{
			string code = exception is PluginException pluginException ? pluginException.Code : "plugin.bridge_failed";
			string error = SensitiveDataRedactor.Redact(exception.Message);
			_logger?.Write(LogSource.Backend, "warn", $"插件命令执行失败 [{_pluginId}:{_windowId}] stage=bridge code={code} '{cmd}': {error}");
			source.PostResult(message.Id, null, error);
		}
	}

	/// <summary>
	/// 仅执行受信任的极窄安全白名单命令
	/// </summary>
	public async Task<object?> ExecuteCommandAsync(
		IPluginBridgeSource source,
		string cmd,
		JsonElement args,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(source.PluginId, _pluginId, StringComparison.Ordinal) ||
			!string.Equals(source.WindowId, _windowId, StringComparison.Ordinal) ||
			!string.Equals(source.Label, PluginWindowHost.BuildLabel(_pluginId, _windowId), StringComparison.Ordinal))
			throw new PluginException("plugin.bridge_denied", "插件桥接来源身份无效。");

		switch (cmd)
		{
			case "plugin_get_info":
			case "plugin.getInfo":
			case "get_info":
				// 返回脱敏摘要，确保不暴露 InstallPath 等路径
				return new
				{
					id = _descriptor.Id,
					name = _descriptor.Name,
					version = _descriptor.Version,
					description = _descriptor.Description,
					author = _descriptor.Author,
					capabilities = _descriptor.Capabilities,
				};

			case "plugin_get_capabilities":
			case "plugin.getCapabilities":
			case "capability_status":
				IReadOnlyList<string> caps = _capabilityProvider?.Invoke(_pluginId) ?? _descriptor.Capabilities;
				return new
				{
					capabilities = caps,
				};

			case "window_get_info":
			case "window.getInfo":
				return new
				{
					pluginId = _pluginId,
					windowId = _windowId,
					label = PluginWindowHost.BuildLabel(_pluginId, _windowId),
					isVisible = source.IsVisible,
				};

			case "window_close":
			case "window.close":
			case "close":
				if (_closeSelfHandler != null)
				{
					await _closeSelfHandler(cancellationToken).ConfigureAwait(false);
				}
				else
				{
					await source.CloseAsync(cancellationToken).ConfigureAwait(false);
				}
				return new { closed = true };

			case "ping":
			case "window_ping":
				return new
				{
					pong = true,
					timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				};

			default:
				throw new PluginException("plugin.bridge_denied", $"命令 '{cmd}' 未被允许或不在插件 Web 视图安全白名单中。");
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_cts.Cancel();
		Task[] pending = _pendingInvokes.Keys.ToArray();
		if (pending.Length > 0)
		{
			Task all = Task.WhenAll(pending);
			await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
		}
		_cts.Dispose();
	}
}
