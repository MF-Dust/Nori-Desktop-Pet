namespace Nori.Core.Memory;

/// <summary>重要记忆来源消息读取仓储。</summary>
public sealed class MemorySourceRepository
{
	private readonly MemoryStore _store;

	public MemorySourceRepository(MemoryStore store) => _store = store;

	public IReadOnlyList<MemorySource> List(long memoryId) => _store.GetSources(memoryId);
}
