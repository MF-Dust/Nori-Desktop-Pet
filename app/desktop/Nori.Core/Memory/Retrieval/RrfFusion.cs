namespace Nori.Core.Memory;

/// <summary>不比较异构分数、只合并排名的 Reciprocal Rank Fusion。</summary>
public static class RrfFusion
{
	public const int DefaultK = 60;

	public static IReadOnlyList<RetrievalHit> Fuse(
		IReadOnlyList<IReadOnlyList<RetrievalHit>> rankings,
		int k = DefaultK)
	{
		Dictionary<long, double> scores = MemoryStore.FuseRrf(rankings, k);
		return scores
			.OrderByDescending(pair => pair.Value)
			.ThenBy(pair => pair.Key)
			.Select((pair, index) => new RetrievalHit(pair.Key, pair.Value, index + 1))
			.ToList();
	}
}
