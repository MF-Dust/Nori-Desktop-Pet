using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;
using ModelContextProtocol.Client;

namespace Nori.Core.Mcp;

/// <summary>
/// MCP 服务器集中管理器 (配置管理、连接池调度与工具分发)
/// </summary>
public sealed class McpManager(HttpClient httpClient, ConfigStore configStore) : IAsyncDisposable
{
	public const string KeyMcpServers = "mcp_servers";

	private readonly HttpClient _httpClient = httpClient;
	private readonly ConfigStore _configStore = configStore;
	private readonly ConcurrentDictionary<string, OfficialMcpConnection> _activeClients = new();
	private readonly ConcurrentDictionary<string, McpServerStatusInfo> _serverStatuses = new();
	private readonly ConcurrentBag<Task> _backgroundTasks = new();
	private readonly SemaphoreSlim _lock = new(1, 1);
	private readonly CancellationTokenSource _lifetimeCts = new();
	private bool _disposed;

	/// <summary>
	/// 获取所有已配置的 MCP 服务器及其当前运行时状态
	/// </summary>
	public async Task<IReadOnlyList<McpServerStatusInfo>> GetServersAsync()
	{
		List<McpServerConfig> configs = LoadConfigs();
		List<McpServerStatusInfo> results = [];

		foreach (McpServerConfig conf in configs)
		{
			if (_serverStatuses.TryGetValue(conf.Id, out McpServerStatusInfo? status))
			{
				results.Add(status);
			}
			else
			{
				results.Add(new McpServerStatusInfo
				{
					ServerId = conf.Id,
					Name = conf.Name,
					Status = "disconnected",
				});
			}
		}

		return await Task.FromResult(results);
	}

	/// <summary>
	/// 获取所有已配置的服务器配置原始列表
	/// </summary>
	public IReadOnlyList<McpServerConfig> GetServerConfigs() => LoadConfigs();

	/// <summary>
	/// 保存/更新服务器配置并根据启用状态自动连接
	/// </summary>
	public async Task<McpServerStatusInfo> SaveServerAsync(McpServerConfig config)
	{
		config = McpConfigValidator.Validate(config);
		await _lock.WaitAsync();
		try
		{
			List<McpServerConfig> configs = LoadConfigs();
			int existingIndex = configs.FindIndex(c => c.Id == config.Id);
			if (existingIndex >= 0)
			{
				configs[existingIndex] = config;
			}
			else
			{
				configs.Add(config);
			}
			SaveConfigs(configs);

			// 若原先已在运行则先断开
			if (_activeClients.TryRemove(config.Id, out OfficialMcpConnection? oldClient))
			{
				await oldClient.DisposeAsync();
			}

			if (config.Enabled && config.AutoConnect)
			{
				return await ConnectServerInternalAsync(config);
			}

			McpServerStatusInfo disconnectedStatus = new()
			{
				ServerId = config.Id,
				Name = config.Name,
				Status = "disconnected",
			};
			_serverStatuses[config.Id] = disconnectedStatus;
			return disconnectedStatus;
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// 删除指定 MCP 服务器配置并终止连接
	/// </summary>
	public async Task<bool> DeleteServerAsync(string serverId)
	{
		await _lock.WaitAsync();
		try
		{
			List<McpServerConfig> configs = LoadConfigs();
			int count = configs.RemoveAll(c => c.Id == serverId);
			if (count > 0)
			{
				SaveConfigs(configs);
			}

			if (_activeClients.TryRemove(serverId, out OfficialMcpConnection? client))
			{
				await client.DisposeAsync();
			}
			_serverStatuses.TryRemove(serverId, out _);

			return count > 0;
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// 主动连接指定服务器并拉取工具与资源
	/// </summary>
	public async Task<McpServerStatusInfo> ConnectServerAsync(string serverId)
	{
		if (string.IsNullOrWhiteSpace(serverId)) throw new InvalidOperationException("MCP 服务器 ID 不能为空");
		McpServerConfig? config = LoadConfigs().FirstOrDefault(c => c.Id == serverId);
		if (config is null)
		{
			throw new InvalidOperationException($"找不到 ID 为 {serverId} 的 MCP 服务器配置");
		}

		await _lock.WaitAsync();
		try
		{
			return await ConnectServerInternalAsync(config);
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// 主动断开指定服务器连接
	/// </summary>
	public async Task<McpServerStatusInfo> DisconnectServerAsync(string serverId)
	{
		await _lock.WaitAsync();
		try
		{
			if (_activeClients.TryRemove(serverId, out OfficialMcpConnection? client))
			{
				await client.DisposeAsync();
			}

			McpServerConfig? config = LoadConfigs().FirstOrDefault(c => c.Id == serverId);
			McpServerStatusInfo status = new()
			{
				ServerId = serverId,
				Name = config?.Name ?? serverId,
				Status = "disconnected",
			};
			_serverStatuses[serverId] = status;
			return status;
		}
		finally
		{
			_lock.Release();
		}
	}

	/// <summary>
	/// 获取所有在线 MCP 服务器暴露的全部工具清单 (带命名空间与所属服务器标识)
	/// </summary>
	public async Task<IReadOnlyList<object>> GetAllToolsAsync()
	{
		List<object> allTools = [];
		foreach ((string serverId, McpServerStatusInfo status) in _serverStatuses)
		{
			if (status.Status != "connected") continue;

			foreach (McpToolDefinition tool in status.Tools)
			{
				allTools.Add(new
				{
					serverId,
					serverName = status.Name,
					toolName = tool.Name,
					fullName = $"mcp__{serverId}__{tool.Name}",
					description = McpConfigValidator.CapDescription(tool.Description),
					inputSchema = McpConfigValidator.CapSchema(tool.InputSchema),
				});
			}
		}
		return await Task.FromResult(allTools);
	}

	/// <summary>
	/// 调用指定 MCP 服务器上的工具
	/// </summary>
	public async Task<McpToolResult> CallToolAsync(string serverId, string toolName, JsonObject? arguments, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(serverId)) throw new InvalidOperationException("MCP 服务器 ID 不能为空");
		JsonObject safeArguments = McpConfigValidator.ValidateToolCall(toolName, arguments);
		if (!_activeClients.TryGetValue(serverId, out OfficialMcpConnection? client) || !client.IsConnected)
		{
			return new McpToolResult
			{
				IsError = true,
				Content = [new McpContentItem { Text = $"MCP 服务器 [{serverId}] 未连接或不在线" }],
			};
		}

		try
		{
			return await client.CallToolAsync(toolName, safeArguments, cancellationToken);
		}
		catch (Exception exception)
		{
			return new McpToolResult
			{
				IsError = true,
				Content = [new McpContentItem { Text = $"调用 MCP 工具失败: {exception.Message}" }],
			};
		}
	}

	/// <summary>
	/// 测试指定服务器配置连接有效性并返回工具列表 (不持久化保存)
	/// </summary>
	public async Task<McpServerStatusInfo> TestServerAsync(McpServerConfig config)
	{
		config = McpConfigValidator.Validate(config);
		OfficialMcpConnection? client = null;
		try
		{
			using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
			client = await OfficialMcpConnection.ConnectAsync(config, _httpClient, cts.Token);
			IReadOnlyList<McpToolDefinition> tools = await client.ListToolsAsync(cts.Token);
			IReadOnlyList<McpResourceDefinition> resources = await client.ListResourcesAsync(cts.Token);

			return new McpServerStatusInfo
			{
				ServerId = config.Id,
				Name = config.Name,
				Status = "connected",
				Tools = tools,
				Resources = resources,
			};
		}
		catch (Exception exception)
		{
			return new McpServerStatusInfo
			{
				ServerId = config.Id,
				Name = config.Name,
				Status = "error",
				ErrorMessage = exception.Message,
			};
		}
		finally
		{
			if (client is not null)
			{
				await client.DisposeAsync();
			}
		}
	}

	/// <summary>
	/// 启动时异步尝试自动连接所有配置为启用的服务器
	/// </summary>
	public async Task AutoConnectEnabledAsync()
	{
		if (_disposed) return;
		List<McpServerConfig> configs = LoadConfigs();
		foreach (McpServerConfig config in configs)
		{
			if (config.Enabled && config.AutoConnect)
			{
				Task task = Task.Run(async () =>
				{
					try
					{
						await _lock.WaitAsync(_lifetimeCts.Token);
						try
						{
							await ConnectServerInternalAsync(config);
						}
						finally
						{
							_lock.Release();
						}
					}
					catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
					{
					}
					catch
					{
						/* 异步自启失败已记录在状态中 */
					}
				}, _lifetimeCts.Token);
				_backgroundTasks.Add(task);
			}
		}
		await Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;
		_lifetimeCts.Cancel();
		try
		{
			Task all = Task.WhenAll(_backgroundTasks.ToArray());
			try { await all.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
			catch (TimeoutException) { /* transports below still get a bounded close */ }
		}
		catch
		{
			// Exit cancellation must not block other resource cleanup.
		}

		foreach ((string _, OfficialMcpConnection client) in _activeClients)
		{
			try { await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
			catch { /* Transport closure during exit is expected. */ }
		}
		_activeClients.Clear();
		_serverStatuses.Clear();
		_lock.Dispose();
		_lifetimeCts.Dispose();
	}

	private async Task<McpServerStatusInfo> ConnectServerInternalAsync(McpServerConfig config)
	{
		config = McpConfigValidator.Validate(config);
		if (_activeClients.TryRemove(config.Id, out OfficialMcpConnection? existing))
		{
			await existing.DisposeAsync();
		}

		_serverStatuses[config.Id] = new McpServerStatusInfo
		{
			ServerId = config.Id,
			Name = config.Name,
			Status = "connecting",
		};

		OfficialMcpConnection? client = null;
		try
		{
			using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
			using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, timeout.Token);
			client = await OfficialMcpConnection.ConnectAsync(config, _httpClient, cts.Token);
			IReadOnlyList<McpToolDefinition> tools = await client.ListToolsAsync(cts.Token);
			IReadOnlyList<McpResourceDefinition> resources = await client.ListResourcesAsync(cts.Token);

			_activeClients[config.Id] = client;

			McpServerStatusInfo connectedStatus = new()
			{
				ServerId = config.Id,
				Name = config.Name,
				Status = "connected",
				Tools = tools,
				Resources = resources,
			};
			_serverStatuses[config.Id] = connectedStatus;
			return connectedStatus;
		}
		catch (Exception exception)
		{
			if (client is not null)
			{
				try { await client.DisposeAsync(); }
				catch { /* Keep the original connection error. */ }
			}

			McpServerStatusInfo errorStatus = new()
			{
				ServerId = config.Id,
				Name = config.Name,
				Status = "error",
				ErrorMessage = DescribeConnectionError(exception),
			};
			_serverStatuses[config.Id] = errorStatus;
			return errorStatus;
		}
	}

	private static string DescribeConnectionError(Exception exception)
	{
		ClientTransportClosedException? transport = exception as ClientTransportClosedException
			?? (exception as AggregateException)?.Flatten().InnerExceptions.OfType<ClientTransportClosedException>().FirstOrDefault();
		if (transport?.Details is StdioClientCompletionDetails stdio)
		{
			string stderr = stdio.StandardErrorTail is not {Count: > 0} ? "" : $" stderr: {string.Join(" | ", stdio.StandardErrorTail)}";
			return $"MCP 进程已退出 (code={stdio.ExitCode?.ToString() ?? "unknown"}).{stderr}";
		}
		return exception.Message;
	}

	private List<McpServerConfig> LoadConfigs()
	{
		ConfigValue? val = _configStore.Get(KeyMcpServers);
		if (val is not ConfigValue.Json { Value: JsonArray array }) return [];
		try
		{
			List<McpServerConfig> parsed = array.Deserialize<List<McpServerConfig>>() ?? [];
			List<McpServerConfig> valid = [];
			foreach (McpServerConfig config in parsed)
			{
				try { valid.Add(McpConfigValidator.Validate(config)); }
				catch (InvalidOperationException) { /* 损坏或不安全配置不进入运行时连接池 */ }
			}
			return valid;
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private void SaveConfigs(List<McpServerConfig> configs)
	{
		string json = JsonSerializer.Serialize(configs);
		JsonNode? node = JsonNode.Parse(json);
		if (node is JsonArray array)
		{
			_configStore.Set(KeyMcpServers, new ConfigValue.Json(array));
		}
	}
}
