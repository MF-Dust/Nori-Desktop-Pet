namespace Nori.Core.Memory;

/// <summary>记忆生命周期状态。</summary>
public enum MemoryStatus
{
	Active,
	Dormant,
	Superseded,
	Expired,
	Archived,
}

/// <summary>记忆状态的数据库文本转换。</summary>
public static class MemoryStatusExtensions
{
	public static string ToStorage(this MemoryStatus status) => status switch
	{
		MemoryStatus.Dormant => "dormant",
		MemoryStatus.Superseded => "superseded",
		MemoryStatus.Expired => "expired",
		MemoryStatus.Archived => "archived",
		_ => "active",
	};

	public static MemoryStatus Parse(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"dormant" => MemoryStatus.Dormant,
		"superseded" => MemoryStatus.Superseded,
		"expired" => MemoryStatus.Expired,
		"archived" => MemoryStatus.Archived,
		_ => MemoryStatus.Active,
	};
}
