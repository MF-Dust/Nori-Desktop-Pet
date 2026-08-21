namespace Nori.Core.Live2D;

/// <summary>
/// Live2D 模型声明的动作组信息
/// </summary>
public sealed record MotionGroupInfo
{
	public required string Group { get; init; }
	public required List<string> Names { get; init; }
}

/// <summary>
/// Live2D 点击互动动作选择器（纯函数）
///
/// 模型作者并不一定使用 Cubism 示例中的 TapBody 组名。这里保留原始组名，
/// 只负责按互动语义排序并排除待机/背景组；真正的播放与失败回退由宿主执行。
/// </summary>
public static class MotionSelector
{
	/// <summary>
	/// 返回可用于点击互动的动作组，优先级依次为 TapBody、点击/触摸、反应、动作/交互、其余非待机组。
	/// </summary>
	public static IReadOnlyList<MotionGroupInfo> GetInteractionCandidates(IEnumerable<MotionGroupInfo> groups)
	{
		return groups
			.Select((group, index) => new Candidate(group, index, Classify(group)))
			.Where(candidate => candidate.Priority >= 0)
			.OrderBy(candidate => candidate.Priority)
			.ThenBy(candidate => candidate.Index)
			.Select(candidate => candidate.Group)
			.ToArray();
	}

	private static int Classify(MotionGroupInfo group)
	{
		if (string.IsNullOrWhiteSpace(group.Group)
			|| group.Names is null
			|| !group.Names.Any(name => !string.IsNullOrWhiteSpace(name))) return -1;

		string normalized = Normalize(group.Group);
		if (normalized.Length == 0 ||
		    normalized.StartsWith("idle", StringComparison.Ordinal) ||
		    normalized.StartsWith("background", StringComparison.Ordinal))
		{
			return -1;
		}

		if (normalized == "tapbody") return 0;
		if (normalized.Contains("tap", StringComparison.Ordinal)
			|| normalized.Contains("touch", StringComparison.Ordinal)
			|| normalized.Contains("click", StringComparison.Ordinal)) return 1;
		if (normalized.Contains("reaction", StringComparison.Ordinal)) return 2;
		if (normalized.Contains("action", StringComparison.Ordinal) || normalized.Contains("interaction", StringComparison.Ordinal)) return 3;
		return 4;
	}

	private static string Normalize(string value)
	{
		Span<char> buffer = stackalloc char[value.Length];
		int length = 0;
		foreach (char character in value)
		{
			if (!char.IsLetterOrDigit(character)) continue;
			buffer[length++] = char.ToLowerInvariant(character);
		}
		return new string(buffer[..length]);
	}

	private sealed record Candidate(MotionGroupInfo Group, int Index, int Priority);
}
