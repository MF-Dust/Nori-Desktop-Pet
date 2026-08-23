using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;

namespace Nori.Core.Mcp;

/// <summary>
/// MCP stdio 环境变量的独立敏感存储。
///
/// mcp_servers 只保留服务器元数据; 环境变量整体序列化后交给 ConfigStore 加密。
/// 该服务可以在连接时恢复给后端传输层, 但任何状态 DTO 都不应携带它。
/// </summary>
public sealed class McpEnvironmentStore(ConfigStore config)
{
	private readonly ConfigStore _config = config;

	/// <summary>根据服务器 ID 生成环境变量敏感配置键。</summary>
	public static string KeyFor(string serverId)
	{
		if (!ConfigValidation.IsValidMcpServerId(serverId)) throw new ArgumentException("MCP 服务器 ID 无效", nameof(serverId));
		return ConfigStore.McpEnvironmentKeyPrefix + serverId;
	}

	/// <summary>读取给后端连接使用的环境变量; 失败时按空集合处理。</summary>
	public IReadOnlyDictionary<string, string> Read(string serverId)
	{
		string key = KeyFor(serverId);
		SecretReadResult result = _config.ReadSecret(key);
		if (!result.IsConfigured || string.IsNullOrWhiteSpace(result.Value)) return new Dictionary<string, string>();

		try
		{
			Dictionary<string, string>? values = JsonSerializer.Deserialize<Dictionary<string, string>>(result.Value);
			if (values is null) return new Dictionary<string, string>();
			return Normalize(values);
		}
		catch (JsonException)
		{
			_config.RecordSecretIssue(key, SecretIssueCategory.CorruptCiphertext);
			return new Dictionary<string, string>();
		}
	}

	/// <summary>保存环境变量; 加密失败会抛出且不会写入明文。</summary>
	public void Save(string serverId, IReadOnlyDictionary<string, string>? values)
	{
		string key = KeyFor(serverId);
		Dictionary<string, string> normalized = Normalize(values ?? new Dictionary<string, string>());
		if (normalized.Count == 0)
		{
			_config.Delete(key);
			return;
		}

		string json = JsonSerializer.Serialize(normalized);
		JsonNode node = JsonNode.Parse(json) ?? new JsonObject();
		_config.Set(key, new ConfigValue.Json(node));
	}

	/// <summary>删除一个服务器的独立环境变量。</summary>
	public void Delete(string serverId) => _config.Delete(KeyFor(serverId));

	/// <summary>当前环境变量是否有可用值, 不返回具体内容。</summary>
	public bool HasConfiguredValues(string serverId) => Read(serverId).Count > 0;

	/// <summary>当前环境变量是否存在需要用户处理的问题。</summary>
	public SecretIssue? GetIssue(string serverId) => _config.GetSecretIssue(KeyFor(serverId));

	private static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> values)
	{
		Dictionary<string, string> normalized = new(StringComparer.Ordinal);
		foreach ((string key, string value) in values)
		{
			if (!ConfigValidation.IsValidEnvironmentName(key)) continue;
			normalized[key] = value;
		}
		return normalized;
	}
}
