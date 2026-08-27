using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Arcade.Abstractions;

/// <summary>纯 reducer 街机 cartridge。</summary>
public interface IArcadeCartridge : IPluginContribution
{
	string Id { get; }
	JsonNode CreateInitialState();

	ValueTask<ArcadeReduceResult> ReduceAsync(
		ArcadeReduceContext context,
		JsonElement state,
		JsonElement command,
		CancellationToken cancellationToken);
}

/// <summary>一次 reducer 调用的最小上下文。</summary>
public sealed record ArcadeReduceContext
{
	public required string SessionId { get; init; }
	public required string ActorId { get; init; }
	public required string CommandId { get; init; }
}

/// <summary>reducer 输出的新状态、命令结果和领域事件。</summary>
public sealed record ArcadeReduceResult
{
	public required JsonNode State { get; init; }
	public JsonNode? Result { get; init; }
	public IReadOnlyList<ArcadeEvent> Events { get; init; } = [];
}

/// <summary>由 cartridge 产生的领域事件。</summary>
public sealed record ArcadeEvent
{
	public required string Type { get; init; }
	public JsonNode? Data { get; init; }
}
