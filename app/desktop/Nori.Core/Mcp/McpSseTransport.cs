using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nori.Core.Mcp;

/// <summary>
/// 基于 HTTP Server-Sent Events (SSE) 的 MCP 传输实现
/// </summary>
public sealed class McpSseTransport(HttpClient httpClient, McpServerConfig config) : IMcpTransport
{
	private readonly HttpClient _httpClient = httpClient;
	private readonly McpServerConfig _config = config;
	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
	private CancellationTokenSource? _cts;
	private Task? _sseLoopTask;
	private string? _postEndpoint;
	private bool _disposed;

	public string TransportType => McpTransportType.Sse;

	public bool IsConnected => _postEndpoint is not null && !_disposed;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(_config.Url))
		{
			throw new InvalidOperationException($"MCP SSE 服务器 {_config.Name} 缺少 URL");
		}

		_cts = new CancellationTokenSource();
		TaskCompletionSource<bool> initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

		_sseLoopTask = Task.Run(() => ListenSseAsync(_config.Url, initializedTcs, _cts.Token), _cts.Token);

		// 等待 SSE 连接建立并获取到 POST 端点 (最多等 10 秒)
		using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(10));
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		try
		{
			await initializedTcs.Task.WaitAsync(linked.Token);
		}
		catch (Exception exception)
		{
			await CloseAsync();
			throw new InvalidOperationException($"连接 MCP SSE 服务 {_config.Url} 失败: {exception.Message}", exception);
		}
	}

	public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_postEndpoint))
		{
			throw new InvalidOperationException($"MCP SSE 服务器 {_config.Name} 未初始化完成或未就绪");
		}

		string requestId = request.Id?.ToString() ?? Guid.NewGuid().ToString("N");
		TaskCompletionSource<JsonRpcResponse> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingRequests[requestId] = tcs;

		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linked.CancelAfter(TimeSpan.FromSeconds(60));

		using (linked.Token.Register(() =>
		{
			if (_pendingRequests.TryRemove(requestId, out TaskCompletionSource<JsonRpcResponse>? removed))
			{
				removed.TrySetCanceled(linked.Token);
			}
		}))
		{
			using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(_postEndpoint, request, linked.Token);
			if (!response.IsSuccessStatusCode)
			{
				string error = await response.Content.ReadAsStringAsync(linked.Token);
				_pendingRequests.TryRemove(requestId, out _);
				throw new HttpRequestException($"MCP SSE 请求失败: HTTP {(int)response.StatusCode}, {error}");
			}

			// 部分 SSE 服务直接在 POST 响应里返回结果
			if (response.Content.Headers.ContentType?.MediaType == "application/json")
			{
				try
				{
					JsonRpcResponse? directResp = await response.Content.ReadFromJsonAsync<JsonRpcResponse>(cancellationToken: linked.Token);
					if (directResp is not null)
					{
						_pendingRequests.TryRemove(requestId, out _);
						return directResp;
					}
				}
				catch
				{
					/* 等待 SSE 推送 */
				}
			}

			return await tcs.Task;
		}
	}

	public async Task SendNotificationAsync(JsonRpcRequest notification, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_postEndpoint)) return;
		try
		{
			await _httpClient.PostAsJsonAsync(_postEndpoint, notification, cancellationToken);
		}
		catch
		{
			/* 忽略通知发送错误 */
		}
	}

	public Task CloseAsync()
	{
		if (_disposed) return Task.CompletedTask;
		_disposed = true;

		_cts?.Cancel();

		foreach ((string _, TaskCompletionSource<JsonRpcResponse> tcs) in _pendingRequests)
		{
			tcs.TrySetCanceled();
		}
		_pendingRequests.Clear();

		_cts?.Dispose();
		_cts = null;
		_postEndpoint = null;

		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await CloseAsync();
		GC.SuppressFinalize(this);
	}

	private async Task ListenSseAsync(string sseUrl, TaskCompletionSource<bool> initTcs, CancellationToken ct)
	{
		try
		{
			using HttpRequestMessage request = new(HttpMethod.Get, sseUrl);
			request.Headers.Accept.ParseAdd("text/event-stream");

			using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
			if (!response.IsSuccessStatusCode)
			{
				initTcs.TrySetException(new HttpRequestException($"SSE 请求返回 HTTP {(int)response.StatusCode}"));
				return;
			}

			using Stream stream = await response.Content.ReadAsStreamAsync(ct);
			using StreamReader reader = new(stream);

			string? currentEvent = null;
			while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } rawLine)
			{
				string line = rawLine.Trim();
				if (line.Length == 0)
				{
					currentEvent = null;
					continue;
				}

				if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
				{
					currentEvent = line["event:".Length..].Trim();
					continue;
				}

				if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
				{
					string data = line["data:".Length..].Trim();

					if (currentEvent == "endpoint" || (string.IsNullOrEmpty(_postEndpoint) && (data.StartsWith("http://") || data.StartsWith("https://") || data.StartsWith('/'))))
					{
						// 解析 POST 消息端点
						if (Uri.TryCreate(data, UriKind.Absolute, out Uri? absoluteUri))
						{
							_postEndpoint = absoluteUri.ToString();
						}
						else if (Uri.TryCreate(new Uri(sseUrl), data, out Uri? relativeUri))
						{
							_postEndpoint = relativeUri.ToString();
						}
						initTcs.TrySetResult(true);
						continue;
					}

					try
					{
						JsonRpcResponse? rpcResp = JsonSerializer.Deserialize<JsonRpcResponse>(data);
						if (rpcResp?.Id is not null)
						{
							string idStr = rpcResp.Id.ToString() ?? "";
							if (_pendingRequests.TryRemove(idStr, out TaskCompletionSource<JsonRpcResponse>? tcs))
							{
								tcs.TrySetResult(rpcResp);
							}
						}
					}
					catch
					{
						/* 忽略非 RPC 数据 */
					}
				}
			}
		}
		catch (Exception exception)
		{
			initTcs.TrySetException(exception);
		}
		finally
		{
			foreach ((string _, TaskCompletionSource<JsonRpcResponse> tcs) in _pendingRequests)
			{
				tcs.TrySetException(new InvalidOperationException("MCP SSE 连接已关闭"));
			}
			_pendingRequests.Clear();
		}
	}
}
