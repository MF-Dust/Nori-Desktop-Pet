namespace Nori.Core.Memory;

/// <summary>长期记忆的语义类型。</summary>
public enum MemoryKind
{
	General,
	Episodic,
	Factual,
	Preference,
	Relational,
	Planned,
	Identity,
}

/// <summary>记忆类型的数据库文本转换。</summary>
public static class MemoryKindExtensions
{
	public static string ToStorage(this MemoryKind kind) => kind switch
	{
		MemoryKind.Episodic => "episodic",
		MemoryKind.Factual => "factual",
		MemoryKind.Preference => "preference",
		MemoryKind.Relational => "relational",
		MemoryKind.Planned => "planned",
		MemoryKind.Identity => "identity",
		_ => "general",
	};

	public static MemoryKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"episodic" or "event" => MemoryKind.Episodic,
		"factual" or "fact" => MemoryKind.Factual,
		"preference" or "prefer" => MemoryKind.Preference,
		"relational" or "relationship" => MemoryKind.Relational,
		"planned" or "plan" => MemoryKind.Planned,
		"identity" or "name" => MemoryKind.Identity,
		_ => MemoryKind.General,
	};
}
