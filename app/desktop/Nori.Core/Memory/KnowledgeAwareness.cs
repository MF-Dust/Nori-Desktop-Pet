namespace Nori.Core.Memory;

/// <summary>ARG 知识对当前 Nori 的认知可见性。</summary>
public enum KnowledgeAwareness
{
	WorldFact,
	ArchiveRecord,
	Inference,
	Unresolved,
	NoriKnows,
	NoriEcho,
	NoriUnknown,
	Recovered,
	UserSharedMemory,
}

/// <summary>知识认知标签的数据库文本转换。</summary>
public static class KnowledgeAwarenessExtensions
{
	public static string ToStorage(this KnowledgeAwareness awareness) => awareness switch
	{
		KnowledgeAwareness.ArchiveRecord => "archive_record",
		KnowledgeAwareness.Inference => "inference",
		KnowledgeAwareness.Unresolved => "unresolved",
		KnowledgeAwareness.NoriKnows => "nori_knows",
		KnowledgeAwareness.NoriEcho => "nori_echo",
		KnowledgeAwareness.NoriUnknown => "nori_unknown",
		KnowledgeAwareness.Recovered => "recovered",
		KnowledgeAwareness.UserSharedMemory => "user_shared_memory",
		_ => "world_fact",
	};

	public static KnowledgeAwareness Parse(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"archive_record" or "archive" => KnowledgeAwareness.ArchiveRecord,
		"inference" or "high_confidence_inference" => KnowledgeAwareness.Inference,
		"unresolved" => KnowledgeAwareness.Unresolved,
		"nori_knows" or "knows" => KnowledgeAwareness.NoriKnows,
		"nori_echo" or "echo" => KnowledgeAwareness.NoriEcho,
		"nori_unknown" or "unknown" => KnowledgeAwareness.NoriUnknown,
		"recovered" or "recovered_memory" => KnowledgeAwareness.Recovered,
		"user_shared_memory" => KnowledgeAwareness.UserSharedMemory,
		_ => KnowledgeAwareness.WorldFact,
	};
}
