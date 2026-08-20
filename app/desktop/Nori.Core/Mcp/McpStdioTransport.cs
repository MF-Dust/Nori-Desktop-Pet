using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Mcp;

/// <summary>
/// 基于子进程 Stdio (标准输入输出) 的 MCP 传输实现
/// </summary>
public sealed class McpStdioTransport(McpServerConfig config) : IMcpTransport
{
	private readonly McpServerConfig _config = config;
	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
	private Process? _process;
	private StreamWriter? _stdinWriter;
	private CancellationTokenSource? _cts;
	private Task? _readLoopTask;
	private bool _disposed;

	public string TransportType => McpTransportType.Stdio;

	public bool IsConnected => _process is { HasExited: false };

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(_config.Command))
		{
			throw new InvalidOperationException($"MCP 服务器 {_config.Name} 缺少启动命令 (Command)");
		}

		_cts = new CancellationTokenSource();

		ProcessStartInfo startInfo = new()
		{
			FileName = _config.Command,
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			StandardInputEncoding = new UTF8Encoding(false),
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		if (_config.Args is { Length: > 0 })
		{
			foreach (string arg in _config.Args)
			{
				startInfo.ArgumentList.Add(arg);
			}
		}

		if (_config.Env is { Count: > 0 })
		{
			foreach ((string key, string val) in _config.Env)
			{
				startInfo.Environment[key] = val;
			}
		}

		try
		{
			Process process = new() { StartInfo = startInfo };
			if (!process.Start())
			{
				throw new InvalidOperationException($"无法启动 MCP 进程: {_config.Command}");
			}

			_process = process;
			_stdinWriter = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
			{
				AutoFlush = true,
			};

			_readLoopTask = Task.Run(() => ReadLoopAsync(process, _cts.Token), _cts.Token);
			_ = Task.Run(() => ReadErrorLoopAsync(process, _cts.Token), _cts.Token);

			return Task.CompletedTask;
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException($"启动 MCP Stdio 进程失败: {exception.Message}", exception);
		}
	}

	public async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
	{
		if (_process is null || _process.HasExited || _stdinWriter is null)
		{
			throw new InvalidOperationException($"MCP 服务器 {_config.Name} 进程未运行或已退出");
		}

		string requestId = request.Id?.ToString() ?? Guid.NewGuid().ToString("N");
		TaskCompletionSource<JsonRpcResponse> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingRequests[requestId] = tcs;

		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linkedCts.CancelAfter(TimeSpan.FromSeconds(60));

		using (linkedCts.Token.Register(() =>
		{
			if (_pendingRequests.TryRemove(requestId, out TaskCompletionSource<JsonRpcResponse>? removed))
			{
				removed.TrySetCanceled(linkedCts.Token);
			}
		}))
		{
			string json = JsonSerializer.Serialize(request);
			await _stdinWriter.WriteLineAsync(json.AsMemory(), cancellationToken);

			return await tcs.Task;
		}
	}

	public async Task SendNotificationAsync(JsonRpcRequest notification, CancellationToken cancellationToken = default)
	{
		if (_process is null || _process.HasExited || _stdinWriter is null)
		{
			return;
		}

		string json = JsonSerializer.Serialize(notification);
		await _stdinWriter.WriteLineAsync(json.AsMemory(), cancellationToken);
	}

	public async Task CloseAsync()
	{
		if (_disposed) return;
		_disposed = true;

		_cts?.Cancel();

		foreach ((string _, TaskCompletionSource<JsonRpcResponse> tcs) in _pendingRequests)
		{
			tcs.TrySetCanceled();
		}
		_pendingRequests.Clear();

		if (_process is { HasExited: false } proc)
		{
			try
			{
				proc.Kill(true);
				await proc.WaitForExitAsync();
			}
			catch
			{
				/* 忽略关闭进程异常 */
			}
		}

		_process?.Dispose();
		_process = null;
		_stdinWriter?.Dispose();
		_stdinWriter = null;
		_cts?.Dispose();
		_cts = null;
	}

	public async ValueTask DisposeAsync()
	{
		await CloseAsync();
		GC.SuppressFinalize(this);
	}

	private async Task ReadLoopAsync(Process process, CancellationToken ct)
	{
		try
		{
			using StreamReader reader = process.StandardOutput;
			while (!ct.IsCancellationRequested && !process.HasExited)
			{
				string? line = await reader.ReadLineAsync(ct);
				if (line is null) break;

				line = line.Trim();
				if (line.Length == 0) continue;

				try
				{
					JsonRpcResponse? response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
					if (response?.Id is not null)
					{
						string idStr = response.Id.ToString() ?? "";
						if (_pendingRequests.TryRemove(idStr, out TaskCompletionSource<JsonRpcResponse>? tcs))
						{
							tcs.TrySetResult(response);
						}
					}
				}
				catch (JsonException)
				{
					/* 忽略非 JSON 行 (部分工具有非标准日志输出) */
				}
			}
		}
		catch
		{
			/* 进程结束退出循环 */
		}
		finally
		{
			foreach ((string _, TaskCompletionSource<JsonRpcResponse> tcs) in _pendingRequests)
			{
				tcs.TrySetException(new InvalidOperationException("MCP Stdio 进程已终止"));
			}
			_pendingRequests.Clear();
		}
	}

	private static async Task ReadErrorLoopAsync(Process process, CancellationToken ct)
	{
		try
		{
			using StreamReader reader = process.StandardError;
			while (!ct.IsCancellationRequested && !process.HasExited)
			{
				string? line = await reader.ReadLineAsync(ct);
				if (line is null) break;
			}
		}
		catch
		{
			/* 忽略 */
		}
	}
}
