namespace Nori.Core.Memory;

/// <summary>一次后台 Reflection 请求。</summary>
public sealed record ReflectionJob(string Reason = "chat");

/// <summary>Reflection 提取出的事实。</summary>
public sealed record ReflectionFact
{
	public required MemoryKind Kind { get; init; }
	public required string Content { get; init; }
	public double Importance { get; init; } = 0.5;
	public double Confidence { get; init; } = 0.8;
	public IReadOnlyList<int> Evidence { get; init; } = [];
	public string? ExpiresAt { get; init; }
}

/// <summary>严格结构化的 Reflection 结果。</summary>
public sealed record ReflectionResult
{
	public bool ShouldStore { get; init; }
	public string Summary { get; init; } = "";
	public string PersonaSummary { get; init; } = "";
	public IReadOnlyList<string> Topics { get; init; } = [];
	public double Importance { get; init; } = 0.5;
	public IReadOnlyList<ReflectionFact> KeyFacts { get; init; } = [];
}
