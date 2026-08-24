using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Mcp;

/// <summary>
/// 单个 MCP 服务器客户端实例 (封装 JSON-RPC 2.0 协议交互与工具/资源调用)
/// </summary>
public sealed class McpClient : IAsyncDisposable
{
	private const string McpProtocolVersion = "2024-11-05";

	private readonly McpServerConfig _config;
	private readonly IMcpTransport _transport;
	private int _nextRequestId;
	private bool _initialized;
	private bool _disposed;
	private readonly HashSet<string> _knownTools = new(StringComparer.Ordinal);

	public McpServerConfig Config => _config;

	public bool IsConnected => _transport.IsConnected && _initialized;

	public McpClient(McpServerConfig config, HttpClient httpClient)
	{
		_config = McpConfigValidator.Validate(config);
		_transport = config.Transport == McpTransportType.Sse
			? new McpSseTransport(httpClient, config)
			: new McpStdioTransport(config);
	}

	public McpClient(McpServerConfig config, IMcpTransport transport)
	{
		_config = McpConfigValidator.Validate(config);
		_transport = transport;
	}

	/// <summary>
	/// 连接并执行 MCP 协议初始化握手
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (IsConnected) return;
		_initialized = false;
		_knownTools.Clear();
		await _transport.StartAsync(cancellationToken);

		// 1. 发送 initialize 请求
		JsonRpcRequest initRequest = new()
		{
			Id = Interlocked.Increment(ref _nextRequestId).ToString(),
			Method = "initialize",
			Params = new
			{
				protocolVersion = McpProtocolVersion,
				capabilities = new
				{
					tools = new { },
					resources = new { },
					prompts = new { },
				},
				clientInfo = new
				{
					name = "NoriDesktopPet",
					version = ProductVersion.Current,
				},
			},
		};

		JsonRpcResponse response = await _transport.SendRequestAsync(initRequest, cancellationToken);
		if (response.Error is not null)
		{
			throw new InvalidOperationException($"MCP 初始化失败: {response.Error.Message} (Code: {response.Error.Code})");
		}

		// 2. 发送 notifications/initialized 确认
		JsonRpcRequest initializedNotification = new()
		{
			Method = "notifications/initialized",
		};
		await _transport.SendNotificationAsync(initializedNotification, cancellationToken);

		_initialized = true;
	}

	/// <summary>
	/// 获取当前 MCP 服务器提供的工具列表 (tools/list)
	/// </summary>
	public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
	{
		EnsureInitialized();

		JsonRpcRequest request = new()
		{
			Id = Interlocked.Increment(ref _nextRequestId).ToString(),
			Method = "tools/list",
			Params = new { },
		};

		JsonRpcResponse response = await _transport.SendRequestAsync(request, cancellationToken);
		if (response.Error is not null)
		{
			throw new InvalidOperationException($"拉取工具列表失败: {response.Error.Message}");
		}

		List<McpToolDefinition> tools = [];
		if (response.Result?["tools"] is JsonArray toolsArray)
		{
			foreach (JsonNode? item in toolsArray)
			{
				if (item is null) continue;
				string? name = item["name"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(name)) continue;

				string? description = item["description"]?.GetValue<string>();
				JsonObject? inputSchema = item["inputSchema"] as JsonObject;

				tools.Add(new McpToolDefinition
				{
					Name = name,
					Description = description,
					InputSchema = inputSchema,
				});
			}
		}

		lock (_knownTools)
		{
			_knownTools.Clear();
			foreach (McpToolDefinition tool in tools) _knownTools.Add(tool.Name);
		}
		return tools;
	}

	/// <summary>
	/// 调用指定工具 (tools/call)
	/// </summary>
	public async Task<McpToolResult> CallToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken = default)
	{
		EnsureInitialized();
		JsonObject safeArguments = McpConfigValidator.ValidateToolCall(toolName, arguments);
		lock (_knownTools)
		{
			if (_knownTools.Count > 0 && !_knownTools.Contains(toolName))
			{
				return new McpToolResult
				{
					IsError = true,
					Content = [new McpContentItem {Text = $"MCP 工具不存在或未在 tools/list 中声明: {toolName}"}],
				};
			}
		}

		JsonRpcRequest request = new()
		{
			Id = Interlocked.Increment(ref _nextRequestId).ToString(),
			Method = "tools/call",
			Params = new
			{
				name = toolName,
				arguments = safeArguments,
			},
		};

		JsonRpcResponse response = await _transport.SendRequestAsync(request, cancellationToken);
		if (response.Error is not null)
		{
			return new McpToolResult
			{
				IsError = true,
				Content = [new McpContentItem { Text = $"MCP 工具报错: {response.Error.Message}" }],
			};
		}

		if (response.Result is null)
		{
			return new McpToolResult
			{
				IsError = true,
				Content = [new McpContentItem {Text = "MCP 工具返回了空结果"}],
			};
		}

		List<McpContentItem> contentList = [];
		bool isError = response.Result["isError"]?.GetValue<bool>() ?? false;

		if (response.Result?["content"] is JsonArray contentArray)
		{
			foreach (JsonNode? node in contentArray)
			{
				if (node is null) continue;
				string type = node["type"]?.GetValue<string>() ?? "text";
				string? text = node["text"]?.GetValue<string>();
				string? data = node["data"]?.GetValue<string>();
				string? mimeType = node["mimeType"]?.GetValue<string>();

				contentList.Add(new McpContentItem
				{
					Type = type,
					Text = text,
					Data = data,
					MimeType = mimeType,
				});
			}
		}

		return new McpToolResult
		{
			Content = contentList,
			IsError = isError,
		};
	}

	/// <summary>
	/// 获取当前 MCP 服务器提供的资源列表 (resources/list)
	/// </summary>
	public async Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
	{
		EnsureInitialized();

		JsonRpcRequest request = new()
		{
			Id = Interlocked.Increment(ref _nextRequestId).ToString(),
			Method = "resources/list",
			Params = new { },
		};

		try
		{
			JsonRpcResponse response = await _transport.SendRequestAsync(request, cancellationToken);
			if (response.Error is not null || response.Result?["resources"] is not JsonArray resArray)
			{
				return [];
			}

			List<McpResourceDefinition> resources = [];
			foreach (JsonNode? item in resArray)
			{
				if (item is null) continue;
				string? uri = item["uri"]?.GetValue<string>();
				string? name = item["name"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(name)) continue;

				resources.Add(new McpResourceDefinition
				{
					Uri = uri,
					Name = name,
					Description = item["description"]?.GetValue<string>(),
					MimeType = item["mimeType"]?.GetValue<string>(),
				});
			}

			return resources;
		}
		catch
		{
			return [];
		}
	}

	/// <summary>
	/// 发送心跳检测 (ping)
	/// </summary>
	public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
	{
		if (!IsConnected) return false;
		try
		{
			JsonRpcRequest request = new()
			{
				Id = Interlocked.Increment(ref _nextRequestId).ToString(),
				Method = "ping",
				Params = new { },
			};
			JsonRpcResponse resp = await _transport.SendRequestAsync(request, cancellationToken);
			return resp.Error is null;
		}
		catch
		{
			return false;
		}
	}

	public async Task CloseAsync()
	{
		if (_disposed) return;
		_initialized = false;
		lock (_knownTools) _knownTools.Clear();
		await _transport.CloseAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;
		_initialized = false;
		lock (_knownTools) _knownTools.Clear();
		try { await _transport.CloseAsync(); }
		finally { await _transport.DisposeAsync(); }
		GC.SuppressFinalize(this);
	}

	private void EnsureInitialized()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!_initialized || !_transport.IsConnected)
		{
			throw new InvalidOperationException($"MCP 客户端 {_config.Name} 尚未初始化或连接已断开");
		}
	}
}
