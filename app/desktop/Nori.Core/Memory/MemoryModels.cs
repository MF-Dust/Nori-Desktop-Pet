namespace Nori.Core.Memory;

/// <summary>待后台批量生成向量的轻量记录。</summary>
public sealed record MemoryEmbeddingWorkItem
{
	public required long Id { get; init; }
	public required string UpdatedAt { get; init; }
	public required string Text { get; init; }
}

/// <summary>待写回数据库的向量。</summary>
public sealed record MemoryEmbeddingUpdate
{
	public required long Id { get; init; }
	public required string UpdatedAt { get; init; }
	public required float[] Vector { get; init; }
}

/// <summary>记忆事实原子。</summary>
public sealed record MemoryAtom
{
	public required long Id { get; init; }
	public required long ParentMemoryId { get; init; }
	public required string AtomType { get; init; }
	public required string Content { get; init; }
	public required double Importance { get; init; }
	public required double Confidence { get; init; }
	public required MemoryStatus Status { get; init; }
	public required string CreatedAt { get; init; }
	public string? LastAccessedAt { get; init; }
	public string? LastReinforcedAt { get; init; }
	public double? TtlDays { get; init; }
	public string? ExpiresAt { get; init; }
	public int ReinforcementCount { get; init; }
	public long? SupersededBy { get; init; }
	public string DecayType { get; init; } = "exponential";
	public string? Entities { get; init; }
}

/// <summary>重要记忆保留的原始来源消息。</summary>
public sealed record MemorySource
{
	public required long Id { get; init; }
	public required long MemoryId { get; init; }
	public required string Role { get; init; }
	public required string Content { get; init; }
	public string? MessageTime { get; init; }
	public required int Sequence { get; init; }
}

/// <summary>知识库检索结果。</summary>
public sealed record RetrievedKnowledge
{
	public required long Id { get; init; }
	public required string Heading { get; init; }
	public string? Subheading { get; init; }
	public required string Content { get; init; }
	public required KnowledgeAwareness Awareness { get; init; }
	public string? KnowledgeType { get; init; }
	public required double Score { get; init; }
}

/// <summary>由 ARG 残响生成的安全短提示。</summary>
public sealed record MemoryEcho
{
	public required string Content { get; init; }
	public required double Score { get; init; }
}

/// <summary>注入 Agent 的完整记忆上下文。</summary>
public sealed record MemoryContext
{
	public IReadOnlyList<MemoryItem> Personal { get; init; } = [];
	public IReadOnlyList<MemoryAtom> Atoms { get; init; } = [];
	public IReadOnlyList<RetrievedKnowledge> Knowledge { get; init; } = [];
	public IReadOnlyList<MemoryEcho> Echoes { get; init; } = [];
	public RecallDebugTrace? Debug { get; init; }
}

/// <summary>检索命中的统一记录。</summary>
public sealed record RetrievalHit(long MemoryId, double Score, int Rank);

/// <summary>Recall Debugger 展示的检索过程。</summary>
public sealed record RecallDebugTrace
{
	public required string Query { get; init; }
	public required string ExpandedQuery { get; init; }
	public IReadOnlyList<RetrievalHit> KeywordHits { get; init; } = [];
	public IReadOnlyList<RetrievalHit> VectorHits { get; init; } = [];
	public IReadOnlyList<RetrievalHit> AtomHits { get; init; } = [];
	public IReadOnlyList<RetrievalHit> RrfHits { get; init; } = [];
	public IReadOnlyList<long> FilteredIds { get; init; } = [];
	public IReadOnlyList<long> InjectedIds { get; init; } = [];
}

/// <summary>索引状态摘要。</summary>
public sealed record MemoryIndexStatus
{
	public MemoryIndexState State { get; init; } = MemoryIndexState.Ready;
	public int Processed { get; init; }
	public int Total { get; init; }
	public string? LastError { get; init; }
	public string? LastMaintenanceAt { get; init; }
	public string? LastReflectionAt { get; init; }
}

/// <summary>记忆设置的领域 DTO。</summary>
public sealed record MemorySettings
{
	public bool Enabled { get; init; } = true;
	public bool ReflectionEnabled { get; init; } = true;
	public int ReflectionRounds { get; init; } = 8;
	public int ReflectionMinChars { get; init; } = 2500;
	public int RecallTopK { get; init; } = 6;
	public int KeywordTopK { get; init; } = 20;
	public int VectorTopK { get; init; } = 20;
	public int RrfK { get; init; } = 60;
	public double MinSimilarity { get; init; } = 0.25;
	public bool DecayEnabled { get; init; } = true;
	public bool ArchiveEnabled { get; init; } = true;
	public double SourceRetentionThreshold { get; init; } = 0.75;
	public double ArchiveThreshold { get; init; } = 0.15;
	public bool KnowledgeEnabled { get; init; } = true;
	public bool KnowledgeWatch { get; init; } = true;
	public bool DebugRetrieval { get; init; }
}
