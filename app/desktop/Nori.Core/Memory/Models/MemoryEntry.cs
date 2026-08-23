namespace Nori.Core.Memory;

/// <summary>Living Memory 领域名称；MemoryItem 保留旧桥接 DTO，MemoryEntry 用于新业务代码。</summary>
public sealed record MemoryEntry
{
	public required long Id { get; init; }
	public required MemoryKind Kind { get; init; }
	public required string CanonicalSummary { get; init; }
	public string PersonaSummary { get; init; } = "";
	public double Importance { get; init; } = 0.5;
	public double Confidence { get; init; } = 0.8;
	public MemoryStatus Status { get; init; } = MemoryStatus.Active;
	public int AccessCount { get; init; }
	public int ReinforcementCount { get; init; }
	public string CreatedAt { get; init; } = "";
	public string UpdatedAt { get; init; } = "";
}
