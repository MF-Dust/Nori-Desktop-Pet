using System.Globalization;

namespace Nori.Core.Memory;

/// <summary>记忆半衰期和生命周期评分。</summary>
public static class DecayCalculator
{
	public static double HalfLifeDays(MemoryKind kind) => kind switch
	{
		MemoryKind.Identity => double.PositiveInfinity,
		MemoryKind.Relational or MemoryKind.Factual => 365,
		MemoryKind.Preference => 180,
		MemoryKind.Episodic => 30,
		MemoryKind.Planned => 30,
		_ => 60,
	};

	public static double TemporalScore(MemoryItem item, DateTimeOffset now)
	{
		if (item.Status is "superseded" or "archived" or "expired") return 0;
		if (MemoryKindExtensions.Parse(item.Kind) == MemoryKind.Identity) return 1;
		double? ttl = item.TtlDays;
		if (ttl is null || ttl <= 0) ttl = HalfLifeDays(MemoryKindExtensions.Parse(item.Kind));
		if (double.IsPositiveInfinity(ttl.Value)) return 1;
		DateTimeOffset reference = ParseDate(item.LastAccessedAt) ?? ParseDate(item.UpdatedAt) ?? ParseDate(item.CreatedAt) ?? now;
		double days = Math.Max(0, (now - reference).TotalDays);
		return Math.Exp(-Math.Log(2) * days / Math.Max(0.01, ttl.Value));
	}

	public static double FinalScore(double rrfScore, MemoryItem item, DateTimeOffset now)
	{
		if (rrfScore <= 0 || item.Status is "superseded" or "archived" or "expired") return 0;
		double importance = 0.75 + Math.Clamp(item.Importance, 0, 1) * 0.5;
		double confidence = 0.75 + Math.Clamp(item.Confidence, 0, 1) * 0.25;
		double reinforcement = 1 + Math.Min(item.ReinforcementCount, 10) * 0.05;
		double dormant = item.Status == "dormant" ? 0.65 : 1;
		return rrfScore * importance * TemporalScore(item, now) * confidence * reinforcement * dormant;
	}

	private static DateTimeOffset? ParseDate(string? value) =>
		DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
}
