namespace Nori.Core.Memory;

/// <summary>记忆索引和后台维护状态。</summary>
public enum MemoryIndexState
{
	Ready,
	Checking,
	Rebuilding,
	Partial,
	Failed,
}
