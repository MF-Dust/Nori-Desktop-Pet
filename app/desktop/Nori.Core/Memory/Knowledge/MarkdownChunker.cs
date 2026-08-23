using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Nori.Core.Memory;

/// <summary>按 Markdown 标题和语义标签切分 Memory.md。</summary>
public static partial class MarkdownChunker
{
	public sealed record Chunk(
		string ChunkKey,
		int Sequence,
		string Heading,
		string? Subheading,
		string Content,
		string KnowledgeType,
		KnowledgeAwareness Awareness,
		string ContentHash);

	public static IReadOnlyList<Chunk> Parse(string markdown)
	{
		List<Section> sections = [];
		List<string> lines = [];
		string? h1 = null;
		string? h2 = null;
		string? currentHeading = null;
		bool fenced = false;
		void Flush()
		{
			if (currentHeading is null && lines.Count == 0) return;
			string content = string.Join("\n", lines).Trim();
			if (content.Length > 0)
			{
				string[] breadcrumb = new[] {h1, h2, currentHeading ?? h1 ?? "Memory"}
					.OfType<string>().Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
				sections.Add(new Section(currentHeading ?? h1 ?? "Memory", h2, string.Join(" / ", breadcrumb), content));
			}
			lines.Clear();
		}

		foreach (string line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
		{
			if (line.TrimStart().StartsWith("```") && !line.TrimStart().StartsWith("````")) fenced = !fenced;
			if (!fenced && TryHeading(line, out int level, out string heading))
			{
				Flush();
				if (level == 1) { h1 = heading; h2 = null; }
				else if (level == 2) h2 = heading;
				currentHeading = heading;
				continue;
			}
			lines.Add(line);
		}
		Flush();

		List<Chunk> result = [];
		int sequence = 0;
		foreach (Section section in sections)
		{
			KnowledgeAwareness awareness = DetectAwareness(section.Content);
			string knowledgeType = DetectKnowledgeType(section.Content, awareness);
			foreach (string part in SplitContent(section.Content, 1200, 800))
			{
				string content = $"{section.HeadingPath}\n{part}".Trim();
				string key = StableKey(section.HeadingPath, sequence);
				result.Add(new Chunk(key, sequence++, section.Heading, section.Subheading, content, knowledgeType, awareness, Hash(content)));
			}
		}
		return result;
	}

	private static bool TryHeading(string line, out int level, out string heading)
	{
		Match match = HeadingRegex().Match(line);
		if (!match.Success) { level = 0; heading = ""; return false; }
		level = match.Groups[1].Value.Length;
		heading = match.Groups[2].Value.Trim();
		return level <= 3 && heading.Length > 0;
	}

	private static IEnumerable<string> SplitContent(string content, int max, int preferred)
	{
		if (content.Length <= max) { yield return content; yield break; }
		string[] paragraphs = content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		StringBuilder current = new();
		foreach (string paragraph in paragraphs)
		{
			if (current.Length > 0 && current.Length + paragraph.Length + 2 > preferred)
			{
				yield return current.ToString();
				current.Clear();
			}
			if (paragraph.Length > max)
			{
				for (int index = 0; index < paragraph.Length; index += preferred)
				{
					int take = Math.Min(preferred, paragraph.Length - index);
					if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
					yield return paragraph.Substring(index, take);
				}
				continue;
			}
			if (current.Length > 0) current.Append("\n\n");
			current.Append(paragraph);
		}
		if (current.Length > 0) yield return current.ToString();
	}

	private static KnowledgeAwareness DetectAwareness(string content)
	{
		Match match = TagRegex().Match(content);
		return match.Success ? KnowledgeAwarenessExtensions.Parse(match.Groups[1].Value) : KnowledgeAwareness.WorldFact;
	}

	private static string DetectKnowledgeType(string content, KnowledgeAwareness awareness) =>
		content.Contains("[ARCHIVE_RECORD]", StringComparison.Ordinal) ? "archive_record" : awareness.ToStorage();

	private static string StableKey(string headingPath, int sequence) =>
		$"{sequence:x8}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(headingPath))).ToLowerInvariant()[..12]}";

	private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

	[GeneratedRegex("^\\s*(#{1,6})\\s+(.+?)\\s*$")]
	private static partial Regex HeadingRegex();

	[GeneratedRegex("\\[([A-Z][A-Z_]+)(?:\\s*/[^\\]]*)?\\]")]
	private static partial Regex TagRegex();

	private sealed record Section(string Heading, string? Subheading, string HeadingPath, string Content);
}
