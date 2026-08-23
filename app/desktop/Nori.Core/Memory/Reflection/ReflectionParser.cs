using System.Text.Json;

namespace Nori.Core.Memory;

/// <summary>Reflection JSON 的安全解析器。</summary>
public static class ReflectionParser
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true,
	};

	public static ReflectionResult Parse(string raw)
	{
		string json = StripFence(raw);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Reflection 输出必须是 JSON 对象");

		ReflectionWire? wire = root.Deserialize<ReflectionWire>(Options);
		if (wire is null) throw new FormatException("Reflection 输出为空");
		string summary = Limit(wire.Summary, 2000, "summary");
		string persona = Limit(wire.PersonaSummary, 1200, "personaSummary");
		List<string> topics = wire.Topics ?? [];
		List<FactWire> keyFacts = wire.KeyFacts ?? [];
		if (topics.Count > 20) throw new FormatException("Reflection topics 数量过多");
		if (!double.IsFinite(wire.Importance) || wire.Importance is < 0 or > 1) throw new FormatException("Reflection importance 无效");
		List<ReflectionFact> facts = [];
		foreach (FactWire fact in keyFacts.Take(20))
		{
			string content = Limit(fact.Content, 600, "keyFacts.content");
			if (content.Length == 0) continue;
			if (!double.IsFinite(fact.Importance) || fact.Importance is < 0 or > 1) continue;
			if (!double.IsFinite(fact.Confidence) || fact.Confidence is < 0 or > 1 || fact.Confidence < 0.6) continue;
			MemoryKind kind = MemoryKindExtensions.Parse(fact.Type ?? "general");
			facts.Add(new ReflectionFact
			{
				Kind = kind,
				Content = content,
				Importance = fact.Importance,
				Confidence = fact.Confidence,
				Evidence = (fact.Evidence ?? []).Take(20).ToArray(),
				ExpiresAt = fact.ExpiresAt,
			});
		}
		return new ReflectionResult
		{
			ShouldStore = wire.ShouldStore,
			Summary = summary,
			PersonaSummary = persona,
			Topics = topics.Where(topic => !string.IsNullOrWhiteSpace(topic)).Select(topic => Limit(topic, 120, "topic")).ToArray(),
			Importance = wire.Importance,
			KeyFacts = facts,
		};
	}

	private static string StripFence(string raw)
	{
		string value = raw.Trim();
		if (!value.StartsWith("```", StringComparison.Ordinal)) return value;
		int firstLine = value.IndexOf('\n');
		int end = value.LastIndexOf("```", StringComparison.Ordinal);
		if (firstLine < 0 || end <= firstLine) throw new FormatException("Reflection 代码围栏不完整");
		return value[(firstLine + 1)..end].Trim();
	}

	private static string Limit(string? value, int max, string field)
	{
		string result = value?.Trim() ?? "";
		if (result.Length > max) throw new FormatException($"Reflection {field} 过长");
		return result;
	}

	private sealed record ReflectionWire
	{
		public bool ShouldStore { get; init; }
		public string? Summary { get; init; }
		public string? PersonaSummary { get; init; }
		public List<string>? Topics { get; init; }
		public double Importance { get; init; } = 0.5;
		public List<FactWire>? KeyFacts { get; init; }
	}

	private sealed record FactWire
	{
		public string? Type { get; init; }
		public string? Content { get; init; }
		public double Importance { get; init; } = 0.5;
		public double Confidence { get; init; } = 0.8;
		public List<int>? Evidence { get; init; }
		public string? ExpiresAt { get; init; }
	}
}
