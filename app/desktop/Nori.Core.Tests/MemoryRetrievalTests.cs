using Nori.Core.Memory;

namespace Nori.Core.Tests;

public sealed class MemoryRetrievalTests
{
	[Fact]
	public void Rrf融合不直接比较异构分数()
	{
		IReadOnlyList<RetrievalHit> result = RrfFusion.Fuse(
		[
			[ new RetrievalHit(1, 100, 1), new RetrievalHit(2, 1, 2) ],
			[ new RetrievalHit(2, 0.99, 1), new RetrievalHit(1, 0.2, 2) ],
		]);

		Assert.Equal(1, result[0].MemoryId);
		Assert.Equal(2, result[1].MemoryId);
		Assert.Equal(1, result[0].Rank);
	}

	[Fact]
	public void 重要度置信度强化和衰减参与最终评分()
	{
		MemoryItem item = new()
		{
			Id = 1,
			Type = "fact",
			Content = "测试",
			Importance = 0.9,
			Source = "chat",
			CreatedAt = DateTimeOffset.UtcNow.AddDays(-10).ToString("o"),
			UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10).ToString("o"),
			Kind = "episodic",
			Confidence = 1,
			TtlDays = 30,
			ReinforcementCount = 2,
		};

		double score = DecayCalculator.FinalScore(1, item, DateTimeOffset.UtcNow);
		Assert.InRange(score, 0.5, 2.0);
		Assert.True(DecayCalculator.TemporalScore(item, DateTimeOffset.UtcNow.AddDays(30)) < 0.6);
	}

	[Fact]
	public void QueryBuilder保留当前问题并带入最近上下文()
	{
		string query = MemoryQueryBuilder.Build("那她后来怎么样了？", [("user", "我们正在讨论研究员和八月十五日事故")]);
		Assert.Contains("那她后来怎么样了", query, StringComparison.Ordinal);
		Assert.Contains("研究员", query, StringComparison.Ordinal);
	}
}
