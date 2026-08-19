using System.Text.Json;
using Avalonia.Threading;
using Nori.Core.Logging;
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

	/// <summary>
	/// 处理页面发来的一条消息
	/// </summary>
	public void Handle(NoriWindow source, string raw)
	{
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
				_ = HandleInvokeAsync(source, message);
				break;
			case "emit":
				// 前端 emit 与 Tauri 一致: 全局广播给所有窗口
				if (message.Event is { Length: > 0 } name)
				{
					object? payload = message.Payload.ValueKind == JsonValueKind.Undefined ? null : message.Payload.Clone();
					Dispatcher.UIThread.Post(() => _services.Windows.Broadcast(name, payload));
				}
				break;
			default:
				_services.Logger.Write(LogSource.Backend, "warn", $"未知的桥接消息种类: {message.Kind}");
				break;
		}
	}

	/// <summary>
	/// 执行一次命令调用并把结果回给页面
	/// </summary>
	private async Task HandleInvokeAsync(NoriWindow source, BridgeMessage message)
	{
		string cmd = message.Cmd ?? "";
		try
		{
			object? value = await _services.Commands.InvokeAsync(source, cmd, message.Args);
			source.PostResult(message.Id, value, null);
		}
		catch (Exception exception)
		{
			// 命令错误一律以可读字符串回给前端展示, 与 Rust 版 Result<T, String> 等价
			_services.Logger.Write(LogSource.Backend, "error", $"命令执行失败: {cmd}: {exception.Message}");
			source.PostResult(message.Id, null, exception.Message);
		}
	}
}
