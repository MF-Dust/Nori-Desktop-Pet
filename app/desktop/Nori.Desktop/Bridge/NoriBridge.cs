using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Threading;
using Nori.Core.Logging;
using Nori.Core.Telemetry;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Bridge;

/// <summary>
/// 前端 ↔ 宿主 桥接内核
///
/// NativeWebView 只提供 JS→宿主的 invokeCSharpAction(string) 与宿主→JS 的 InvokeScript,
/// 这里在其上实现 invoke 的请求/响应关联与 emit 的事件广播.
/// </summary>
public sealed class NoriBridge(AppServices services)
{
	private readonly AppServices _services = services;
	private readonly CancellationTokenSource _shutdownCts = new();
	private readonly ConcurrentDictionary<Task, byte> _pendingInvokes = new();
	private int _disposed;

	/// <summary>
	/// 处理页面发来的一条消息
	/// </summary>
	public void Handle(NoriWindow source, string raw)
	{
		if (Volatile.Read(ref _disposed) != 0) return;
		BridgeMessage? message;
		try
		{
			message = JsonSerializer.Deserialize<BridgeMessage>(raw, BridgeJson.Options);
		}
		catch (JsonException exception)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"桥接消息解析失败: {exception.Message}");
			return;
		}
		if (message is null) return;

			switch (message.Kind)
		{
			case "invoke":
				TrackInvoke(source, message);
				break;
			case "emit":
				// 前端 emit 与 Tauri 一致: 全局广播给所有窗口
				if (message.Event is { Length: > 0 } name)
				{
					object? payload = message.Payload.ValueKind == JsonValueKind.Undefined ? null : message.Payload.Clone();
					Dispatcher.UIThread.Post(() =>
					{
						if (Volatile.Read(ref _disposed) != 0) return;
						try { _services.Windows.Broadcast(name, payload); }
						catch { /* closing windows must not fault the dispatcher */ }
					});
				}
				break;
			default:
				_services.Logger.Write(LogSource.Backend, "warn", $"未知的桥接消息种类: {message.Kind}");
				break;
		}
	}

	private void TrackInvoke(NoriWindow source, BridgeMessage message)
	{
		Task task = HandleInvokeObservedAsync(source, message);
		_pendingInvokes.TryAdd(task, 0);
		_ = task.ContinueWith(
			completed => _pendingInvokes.TryRemove(completed, out _),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private async Task HandleInvokeObservedAsync(NoriWindow source, BridgeMessage message)
	{
		try
		{
			await HandleInvokeAsync(source, message, _shutdownCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			try
			{
				_services.Telemetry.CaptureException(exception, "bridge.invoke");
				_services.Logger.Write(LogSource.Backend, "error", $"桥接调用后台任务失败: {exception.Message}");
			}
			catch
			{
				// Shutdown may already have released logging/telemetry dependencies.
			}
		}
	}

	/// <summary>
	/// 执行一次命令调用并把结果回给页面
	/// </summary>
	private async Task HandleInvokeAsync(NoriWindow source, BridgeMessage message, CancellationToken cancellationToken)
	{
		string cmd = message.Cmd ?? "";
		using ITelemetryTransaction transaction = _services.Telemetry.StartTransaction($"bridge.{cmd}");
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			object? value = await _services.Commands.InvokeAsync(source, cmd, message.Args);
			cancellationToken.ThrowIfCancellationRequested();
			source.PostResult(message.Id, value, null);
		}
		catch (Exception exception)
		{
			_services.Telemetry.CaptureException(exception, $"bridge.{cmd}");
			// 命令错误一律以可读字符串回给前端展示, 与 Rust 版 Result<T, String> 等价
			_services.Logger.Write(LogSource.Backend, "error", $"命令执行失败: {cmd}: {exception.Message}");
			source.PostResult(message.Id, null, exception.Message);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_shutdownCts.Cancel();
		Task[] pending = _pendingInvokes.Keys.ToArray();
		if (pending.Length > 0)
		{
			Task all = Task.WhenAll(pending);
			await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
		}
		_shutdownCts.Dispose();
	}
}
