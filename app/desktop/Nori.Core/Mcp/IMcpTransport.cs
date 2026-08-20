namespace Nori.Core.Mcp;

/// <summary>
/// MCP 传输层接口
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
	/// <summary>传输类型: stdio 或 sse</summary>
	string TransportType { get; }

	/// <summary>是否已建立连接</summary>
	bool IsConnected { get; }

	/// <summary>启动连接</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>发送 JSON-RPC 请求并等待响应</summary>
	Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

	/// <summary>发送通知 (无须响应)</summary>
	Task SendNotificationAsync(JsonRpcRequest notification, CancellationToken cancellationToken = default);

	/// <summary>主动关闭连接</summary>
	Task CloseAsync();
}
