namespace Nori.PluginRuntime;

/// <summary>第一阶段支持的简单 AND 版本范围。</summary>
internal static class PluginRange
{
	private static readonly string[] Operators = [">=", "<=", ">", "<", "="];

	public static bool TryParse(string? range, out IReadOnlyList<Constraint> constraints)
	{
		constraints = [];
		if (string.IsNullOrWhiteSpace(range)) return false;
		List<Constraint> parsed = [];
		foreach (string token in range.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			string? operation = Operators.FirstOrDefault(candidate => token.StartsWith(candidate, StringComparison.Ordinal));
			operation ??= "=";
			string value = operation == "=" ? token : token[operation.Length..];
			if (!PluginVersion.TryParse(value, out PluginVersion version)) return false;
			parsed.Add(new(operation, version));
		}
		if (parsed.Count == 0) return false;
		constraints = parsed;
		return true;
	}

	public static bool Satisfies(PluginVersion version, string range)
	{
		if (!TryParse(range, out IReadOnlyList<Constraint> constraints)) return false;
		return constraints.All(constraint => constraint.Operator switch
		{
			">=" => version.CompareTo(constraint.Version) >= 0,
			">" => version.CompareTo(constraint.Version) > 0,
			"<=" => version.CompareTo(constraint.Version) <= 0,
			"<" => version.CompareTo(constraint.Version) < 0,
			_ => version.CompareTo(constraint.Version) == 0,
		});
	}

	internal readonly record struct Constraint(string Operator, PluginVersion Version);
}
