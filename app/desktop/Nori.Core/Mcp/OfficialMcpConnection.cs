using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Nori.Core.Mcp;

/// <summary>
/// 官方 ModelContextProtocol 客户端适配器。应用其余部分继续消费
/// <see cref="McpModels"/> 中的小型 DTO 面，因此桥接协议保持稳定，传输与协议细节交给 SDK。
/// </summary>
internal sealed class OfficialMcpConnection : IAsyncDisposable
{
	private const string ProtocolVersion = "2025-11-25";

	private readonly ModelContextProtocol.Client.McpClient _client;
	private bool _connected = true;

	private OfficialMcpConnection(
		McpServerConfig config,
		ModelContextProtocol.Client.McpClient client)
	{
		_client = client;
	}

	public bool IsConnected => _connected;

	public static async Task<OfficialMcpConnection> ConnectAsync(
		McpServerConfig config,
		HttpClient httpClient,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(httpClient);

		IClientTransport transport = CreateTransport(config, httpClient);
		try
		{
			McpClientOptions options = new()
			{
				ProtocolVersion = ProtocolVersion,
			};

			ModelContextProtocol.Client.McpClient client = await ModelContextProtocol.Client.McpClient.CreateAsync(
				transport,
				options,
				NullLoggerFactory.Instance,
				cancellationToken);

			return new OfficialMcpConnection(config, client);
		}
		catch
		{
			if (transport is IAsyncDisposable disposable)
			{
				await disposable.DisposeAsync();
			}
			throw;
		}
	}

	public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<McpToolDefinition> tools = (await _client.ListToolsAsync(cancellationToken: cancellationToken))
			.Select(tool => new McpToolDefinition
			{
				Name = tool.Name,
				Description = tool.Description,
				InputSchema = ParseObject(tool.ProtocolTool.InputSchema),
			})
			.Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
			.ToArray();

		return tools;
	}

	public async Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken)
	{
		try
		{
			return (await _client.ListResourcesAsync(cancellationToken: cancellationToken))
				.Where(resource => !string.IsNullOrWhiteSpace(resource.Uri) && !string.IsNullOrWhiteSpace(resource.Name))
				.Select(resource => new McpResourceDefinition
				{
					Uri = resource.Uri,
					Name = resource.Name,
					Description = resource.Description,
					MimeType = resource.MimeType,
				})
				.ToArray();
		}
		catch
		{
			// 服务器可能不声明 resources；保持原管理器行为，按空列表处理。
			return [];
		}
	}

	public async Task<McpToolResult> CallToolAsync(
		string toolName,
		JsonObject? arguments,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(toolName))
		{
			throw new ArgumentException("工具名称不能为空", nameof(toolName));
		}

		Dictionary<string, object?> argumentValues = [];
		if (arguments is not null)
		{
			foreach ((string key, JsonNode? value) in arguments)
			{
				argumentValues[key] = ParseElement(value);
			}
		}

		ModelContextProtocol.Protocol.CallToolResult result = await _client.CallToolAsync(
			toolName,
			argumentValues,
			progress: null,
			options: null,
			cancellationToken);

		List<McpContentItem> content = [];
		foreach (ModelContextProtocol.Protocol.ContentBlock block in result.Content ?? [])
		{
			JsonObject? json = JsonSerializer.SerializeToNode(block)?.AsObject();
			content.Add(new McpContentItem
			{
				Type = block.Type ?? "text",
				Text = GetString(json, "text"),
				Data = GetString(json, "data"),
				MimeType = GetString(json, "mimeType"),
			});
		}

		return new McpToolResult
		{
			Content = content,
			IsError = result.IsError ?? false,
		};
	}

	public async ValueTask DisposeAsync()
	{
		if (!_connected) return;
		_connected = false;

		await _client.DisposeAsync();
		GC.SuppressFinalize(this);
	}

	private static IClientTransport CreateTransport(McpServerConfig config, HttpClient httpClient)
	{
		if (string.Equals(config.Transport, McpTransportType.Sse, StringComparison.OrdinalIgnoreCase))
		{
			if (!Uri.TryCreate(config.Url, UriKind.Absolute, out Uri? endpoint) ||
				(endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
			{
				throw new InvalidOperationException($"MCP SSE 服务器 {config.Name} 的 URL 无效");
			}

			HttpClientTransportOptions options = new()
			{
				Endpoint = endpoint,
				TransportMode = HttpTransportMode.Sse,
				Name = config.Name,
			};
			return new HttpClientTransport(options, httpClient, NullLoggerFactory.Instance, ownsHttpClient: false);
		}

		if (string.IsNullOrWhiteSpace(config.Command))
		{
			throw new InvalidOperationException($"MCP 服务器 {config.Name} 缺少启动命令 (Command)");
		}

		Dictionary<string, string?> environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
		if (config.Env is not null)
		{
			foreach ((string key, string value) in config.Env)
			{
				environment[key] = value;
			}
		}

		StdioClientTransportOptions stdioOptions = new()
		{
			Command = config.Command,
			Arguments = config.Args ?? [],
			Name = config.Name,
			InheritEnvironmentVariables = false,
			EnvironmentVariables = environment,
		};
		return new StdioClientTransport(stdioOptions, NullLoggerFactory.Instance);
	}

	private static JsonObject? ParseObject(JsonElement element)
	{
		if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
		try
		{
			return JsonNode.Parse(element.GetRawText()) as JsonObject;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static JsonElement ParseElement(JsonNode? value)
	{
		return JsonSerializer.Deserialize<JsonElement>(value?.ToJsonString() ?? "null");
	}

	private static string? GetString(JsonObject? json, string propertyName)
	{
		if (json is null || json[propertyName] is null) return null;
		try
		{
			return json[propertyName]!.GetValue<string>();
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}
}
