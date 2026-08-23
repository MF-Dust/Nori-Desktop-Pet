using Nori.Core.Data;

namespace Nori.Core.Memory;

/// <summary>
/// Living Memory 聚合仓储。
/// 通过 MemoryStore 兼容层集中写入，避免调用方绕过索引、Atom 和 Source 同步。
/// </summary>
public sealed class MemoryRepository
{
	private readonly MemoryStore _store;

	public MemoryRepository(NoriDatabase database)
		: this(new MemoryStore(database))
	{
	}

	public MemoryRepository(MemoryStore store) => _store = store;

	public MemoryStore Store => _store;

	public MemoryItem Add(
		string content,
		MemoryKind kind = MemoryKind.General,
		double importance = 0.5,
		string? tags = null,
		string source = "chat",
		string? canonicalSummary = null,
		string? personaSummary = null,
		double confidence = 0.8,
		double? ttlDays = null,
		string? expiresAt = null,
		string? embedding = null,
		string? embeddingFingerprint = null)
	{
		return _store.AddAggregate(
			kind.ToStorage(), content, importance, source, tags, embedding, kind,
			canonicalSummary, personaSummary, confidence, ttlDays, expiresAt, embeddingFingerprint);
	}

	public MemoryItem? Get(long id) => _store.Get(id);

	public IReadOnlyList<MemoryItem> List(int limit = 100) => _store.GetAll(limit);

	public bool Update(long id, string content, double? importance = null, string? tags = null, MemoryKind? kind = null,
		string? canonicalSummary = null, string? personaSummary = null, double? confidence = null) =>
		_store.Update(id, content, importance, tags, kind: kind, canonicalSummary: canonicalSummary, personaSummary: personaSummary, confidence: confidence);

	public bool Archive(long id) => _store.Archive(id);

	public bool Restore(long id) => _store.Restore(id);

	public bool Delete(long id) => _store.Delete(id);

	public void Clear() => _store.Clear();
}
