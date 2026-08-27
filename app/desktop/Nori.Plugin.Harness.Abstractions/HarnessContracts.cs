using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Harness.Abstractions;

/// <summary>Harness 工具风险等级。</summary>
public enum HarnessRiskLevel
{
	Safe,
	Sensitive,
	Destructive,
}

/// <summary>Harness 调用方信任等级。</summary>
public enum HarnessTrustLevel
{
	Untrusted,
	Standard,
	Trusted,
}

/// <summary>插件向 Harness 暴露的工具描述。</summary>
public sealed record HarnessToolDescriptor
{
	public required string Id { get; init; }
	public required string Name { get; init; }
	public required string Description { get; init; }
	public required JsonElement InputSchema { get; init; }
	public HarnessRiskLevel RiskLevel { get; init; }
}

/// <summary>Harness 工具调用上下文。</summary>
public sealed record HarnessInvocationContext
{
	public required string HarnessId { get; init; }
	public required string SessionId { get; init; }
	public HarnessTrustLevel TrustLevel { get; init; }
	public IReadOnlySet<string> GrantedScopes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Harness 工具调用结果。</summary>
public sealed record HarnessToolResult
{
	public bool Success { get; init; }
	public JsonNode? Result { get; init; }
	public IReadOnlyList<HarnessEvent> Events { get; init; } = [];
}

/// <summary>Harness 工具贡献。</summary>
public interface IHarnessTool : IPluginContribution
{
	HarnessToolDescriptor Descriptor { get; }

	ValueTask<HarnessToolResult> InvokeAsync(
		JsonElement arguments,
		HarnessInvocationContext context,
		CancellationToken cancellationToken);
}

/// <summary>Harness 资源描述。</summary>
public sealed record HarnessResourceDescriptor
{
	public required string Path { get; init; }
	public required string Name { get; init; }
	public string? Description { get; init; }
	public string? MediaType { get; init; }
}

/// <summary>Harness 资源提供贡献。URI 由宿主固定为 nori-plugin://&lt;pluginId&gt;/...</summary>
public interface IHarnessResourceProvider : IPluginContribution
{
	string Id { get; }

	ValueTask<IReadOnlyList<HarnessResourceDescriptor>> ListAsync(
		CancellationToken cancellationToken);

	ValueTask<Stream> OpenReadAsync(
		string relativePath,
		CancellationToken cancellationToken);
}

/// <summary>Harness 事件过滤条件。</summary>
public sealed record HarnessEventSubscription
{
	public IReadOnlySet<string> EventTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>Harness 事件。</summary>
public sealed record HarnessEvent
{
	public required string Type { get; init; }
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public JsonNode? Data { get; init; }
}

/// <summary>Harness 事件源贡献。</summary>
public interface IHarnessEventSource : IPluginContribution
{
	string Id { get; }

	IAsyncEnumerable<HarnessEvent> ListenAsync(
		HarnessEventSubscription subscription,
		CancellationToken cancellationToken);
}

/// <summary>Harness 全局工具 ID 规则。</summary>
public static class HarnessToolIds
{
	public static string Compose(string pluginId, string toolId)
	{
		if (!IsSegment(pluginId) || !IsSegment(toolId)) throw new ArgumentException("Harness 工具 ID 无效");
		return $"{pluginId}/{toolId}";
	}

	private static bool IsSegment(string value) =>
		!string.IsNullOrWhiteSpace(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
