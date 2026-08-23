using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nori.Core.Mcp;

/// <summary>
/// MCP 服务器传输协议类型
/// </summary>
public static class McpTransportType
{
	public const string Stdio = "stdio";
	public const string Sse = "sse";
}

/// <summary>
/// MCP 服务器配置模型
/// </summary>
public sealed record McpServerConfig
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("transport")]
	public string Transport { get; init; } = McpTransportType.Stdio;

	[JsonPropertyName("command")]
	public string? Command { get; init; }

	[JsonPropertyName("args")]
	public string[]? Args { get; init; }

	[JsonPropertyName("env")]
	public Dictionary<string, string>? Env { get; init; }

	[JsonPropertyName("url")]
	public string? Url { get; init; }

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; } = false;

	[JsonPropertyName("autoConnect")]
	public bool AutoConnect { get; init; } = false;
}

/// <summary>
/// MCP 工具定义 (对应 tools/list)
/// </summary>
public sealed record McpToolDefinition
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("inputSchema")]
	public JsonObject? InputSchema { get; init; }
}

/// <summary>
/// MCP 资源定义 (对应 resources/list)
/// </summary>
public sealed record McpResourceDefinition
{
	[JsonPropertyName("uri")]
	public required string Uri { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("mimeType")]
	public string? MimeType { get; init; }
}

/// <summary>
/// MCP 工具调用内容项
/// </summary>
public sealed record McpContentItem
{
	[JsonPropertyName("type")]
	public string Type { get; init; } = "text";

	[JsonPropertyName("text")]
	public string? Text { get; init; }

	[JsonPropertyName("data")]
	public string? Data { get; init; }

	[JsonPropertyName("mimeType")]
	public string? MimeType { get; init; }
}

/// <summary>
/// MCP 工具执行结果
/// </summary>
public sealed record McpToolResult
{
	[JsonPropertyName("content")]
	public List<McpContentItem> Content { get; init; } = [];

	[JsonPropertyName("isError")]
	public bool IsError { get; init; }

	/// <summary>
	/// 提取所有文本内容为一个可读字符串
	/// </summary>
	public string AsText()
	{
		List<string> texts = [];
		foreach (McpContentItem item in Content)
		{
			if (!string.IsNullOrEmpty(item.Text))
			{
				texts.Add(item.Text);
			}
		}
		string result = texts.Count > 0
			? string.Join("\n", texts)
			: (IsError ? "MCP 工具执行失败 (无详细输出)" : "MCP 工具执行成功");
		return result.Length <= McpConfigValidator.MaxResultCharacters
			? result
			: result[..McpConfigValidator.MaxResultCharacters];
	}
}

/// <summary>
/// MCP 服务器运行时状态信息
/// </summary>
public sealed record McpServerStatusInfo
{
	[JsonPropertyName("serverId")]
	public required string ServerId { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("status")]
	public required string Status { get; init; } // "disconnected" | "connecting" | "connected" | "error"

	[JsonPropertyName("errorMessage")]
	public string? ErrorMessage { get; init; }

	[JsonPropertyName("tools")]
	public IReadOnlyList<McpToolDefinition> Tools { get; init; } = [];

	[JsonPropertyName("resources")]
	public IReadOnlyList<McpResourceDefinition> Resources { get; init; } = [];
}

/// <summary>
/// JSON-RPC 2.0 请求
/// </summary>
public sealed record JsonRpcRequest
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonPropertyName("id")]
	public object? Id { get; init; }

	[JsonPropertyName("method")]
	public required string Method { get; init; }

	[JsonPropertyName("params")]
	public object? Params { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 响应
/// </summary>
public sealed record JsonRpcResponse
{
	[JsonPropertyName("jsonrpc")]
	public string JsonRpc { get; init; } = "2.0";

	[JsonPropertyName("id")]
	public object? Id { get; init; }

	[JsonPropertyName("result")]
	public JsonNode? Result { get; init; }

	[JsonPropertyName("error")]
	public JsonRpcError? Error { get; init; }
}

/// <summary>
/// JSON-RPC 2.0 错误
/// </summary>
public sealed record JsonRpcError
{
	[JsonPropertyName("code")]
	public int Code { get; init; }

	[JsonPropertyName("message")]
	public required string Message { get; init; }

	[JsonPropertyName("data")]
	public JsonNode? Data { get; init; }
}
