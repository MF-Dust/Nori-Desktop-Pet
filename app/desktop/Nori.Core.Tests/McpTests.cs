using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Mcp;

namespace Nori.Core.Tests;

public class McpTests : IDisposable
{
	private sealed class MockMcpTransport : IMcpTransport
	{
		public string TransportType => "mock";
		public bool IsConnected { get; private set; }

		public Func<JsonRpcRequest, JsonRpcResponse>? OnRequest { get; set; }

		public Task StartAsync(CancellationToken cancellationToken = default)
		{
			IsConnected = true;
			return Task.CompletedTask;
		}

		public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
		{
			if (OnRequest != null)
			{
				return Task.FromResult(OnRequest(request));
			}

			if (request.Method == "initialize")
			{
				return Task.FromResult(new JsonRpcResponse
				{
					Id = request.Id,
					Result = new JsonObject
					{
						["protocolVersion"] = "2024-11-05",
						["capabilities"] = new JsonObject
						{
							["tools"] = new JsonObject(),
						},
						["serverInfo"] = new JsonObject
						{
							["name"] = "MockServer",
							["version"] = "1.0.0",
						},
					},
				});
			}

			if (request.Method == "tools/list")
			{
				return Task.FromResult(new JsonRpcResponse
				{
					Id = request.Id,
					Result = new JsonObject
					{
						["tools"] = new JsonArray
						{
							new JsonObject
							{
								["name"] = "readFile",
								["description"] = "读取指定文件内容",
								["inputSchema"] = new JsonObject
								{
									["type"] = "object",
									["properties"] = new JsonObject
									{
										["path"] = new JsonObject { ["type"] = "string" },
									},
									["required"] = new JsonArray { "path" },
								},
							},
						},
					},
				});
			}

			if (request.Method == "tools/call")
			{
				return Task.FromResult(new JsonRpcResponse
				{
					Id = request.Id,
					Result = new JsonObject
					{
						["content"] = new JsonArray
						{
							new JsonObject
							{
								["type"] = "text",
								["text"] = "文件内容测试: Hello Nori!",
							},
						},
						["isError"] = false,
					},
				});
			}

			return Task.FromResult(new JsonRpcResponse
			{
				Id = request.Id,
				Error = new JsonRpcError
				{
					Code = -32601,
					Message = "Method not found",
				},
			});
		}

		public Task SendNotificationAsync(JsonRpcRequest notification, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task CloseAsync()
		{
			IsConnected = false;
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync()
		{
			IsConnected = false;
			return ValueTask.CompletedTask;
		}
	}

	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nori-mcp-test-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _configStore;

	public McpTests()
	{
		_database = NoriDatabase.Open(_dbPath);
		_configStore = new ConfigStore(_database);
		_configStore.InitDefaults("0.1.0");
	}

	public void Dispose()
	{
		_database.Dispose();
		try
		{
			File.Delete(_dbPath);
		}
		catch (IOException)
		{
		}
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task McpClient_握手与工具拉取及执行()
	{
		McpServerConfig config = new()
		{
			Id = "test-server",
			Name = "测试服务",
			Transport = "stdio",
			Command = "test.exe",
		};

		MockMcpTransport transport = new();
		await using McpClient client = new(config, transport);

		await client.InitializeAsync();
		Assert.True(client.IsConnected);

		IReadOnlyList<McpToolDefinition> tools = await client.ListToolsAsync();
		Assert.Single(tools);
		Assert.Equal("readFile", tools[0].Name);
		Assert.Equal("读取指定文件内容", tools[0].Description);

		McpToolResult result = await client.CallToolAsync("readFile", new JsonObject { ["path"] = "C:/test.txt" });
		Assert.False(result.IsError);
		Assert.Equal("文件内容测试: Hello Nori!", result.AsText());
	}

	[Fact]
	public async Task McpManager_服务器配置增删查()
	{
		using HttpClient httpClient = new();
		await using McpManager manager = new(httpClient, _configStore);

		McpServerConfig config1 = new()
		{
			Id = "fs-server",
			Name = "文件系统服务",
			Transport = McpTransportType.Stdio,
			Command = "npx",
			Args = ["-y", "@modelcontextprotocol/server-filesystem"],
			Enabled = false,
			AutoConnect = false,
		};

		McpServerStatusInfo status = await manager.SaveServerAsync(config1);
		Assert.Equal("fs-server", status.ServerId);
		Assert.Equal("disconnected", status.Status);

		IReadOnlyList<McpServerStatusInfo> servers = await manager.GetServersAsync();
		Assert.Single(servers);
		Assert.Equal("fs-server", servers[0].ServerId);
		Assert.Equal("文件系统服务", servers[0].Name);

		bool deleted = await manager.DeleteServerAsync("fs-server");
		Assert.True(deleted);

		IReadOnlyList<McpServerStatusInfo> serversAfterDelete = await manager.GetServersAsync();
		Assert.Empty(serversAfterDelete);
	}

	[Fact]
	public void JsonRpcResponse_序列化与反序列化()
	{
		string json = """
		{
			"jsonrpc": "2.0",
			"id": "123",
			"result": {
				"tools": [
					{
						"name": "calc",
						"description": "计算器"
					}
				]
			}
		}
		""";

		JsonRpcResponse? resp = JsonSerializer.Deserialize<JsonRpcResponse>(json);
		Assert.NotNull(resp);
		Assert.Equal("2.0", resp.JsonRpc);
		Assert.Equal("123", resp.Id?.ToString());
		Assert.NotNull(resp.Result);
		Assert.Equal("calc", resp.Result["tools"]?[0]?["name"]?.GetValue<string>());
	}
}
