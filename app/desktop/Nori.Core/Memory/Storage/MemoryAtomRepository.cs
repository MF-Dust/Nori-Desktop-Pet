namespace Nori.Core.Memory;

/// <summary>事实原子读取仓储；写入仍由 MemoryStore 聚合事务完成。</summary>
public sealed class MemoryAtomRepository
{
	private readonly MemoryStore _store;

	public MemoryAtomRepository(MemoryStore store) => _store = store;

	public MemoryAtom? Get(long id) => _store.GetAtom(id);

	public IReadOnlyList<MemoryAtom> List(long? parentMemoryId = null, MemoryStatus? status = null, int limit = 100, int offset = 0) =>
		_store.GetAtoms(parentMemoryId, status, limit, offset);
}
