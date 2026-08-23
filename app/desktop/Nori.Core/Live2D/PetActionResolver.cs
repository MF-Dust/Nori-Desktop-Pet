namespace Nori.Core.Live2D;

/// <summary>将模型文件名和自然语言动作名解析为当前模型的真实名称。</summary>
public static class PetActionResolver
{
	private static readonly (string Alias, string Hint)[] MotionAliases =
	[
		("idle", "idle"), ("sleep", "sleep"), ("nod", "nod"), ("shake", "shake"),
		("wakuwaku", "wakuwaku"), ("excited", "wakuwaku"), ("angry", "angry"),
		("troubled", "troubled"), ("sad", "troubled"), ("dizzy", "dizzy"),
		("glitch", "glitch"), ("back", "back"),
	];

	private static readonly (string Alias, string Hint)[] ExpressionAliases =
	[
		("default", "default"), ("smile", "smile"), ("happy", "happy"),
		("angry", "angry"), ("shy", "shy"), ("dark", "dark"),
		("speechless", "speechless"), ("tears", "tears"), ("troubled", "troubled"),
		("sad", "troubled"), ("doubt", "doubt"), ("disgust", "disgust"),
		("serious", "serious"), ("surprised", "surprised"), ("sleep", "sleep"),
	];

	public static string? ResolveMotion(IReadOnlyList<MotionGroupInfo> groups, string requested)
	{
		IReadOnlyList<string> names = groups.SelectMany(group => group.Names).ToArray();
		return Resolve(names, requested, MotionAliases);
	}

	public static string? ResolveExpression(IReadOnlyList<string> names, string requested) =>
		Resolve(names, requested, ExpressionAliases);

	private static string? Resolve(IReadOnlyList<string> names, string requested, IReadOnlyList<(string Alias, string Hint)> aliases)
	{
		if (string.IsNullOrWhiteSpace(requested)) return null;
		string input = Normalize(requested);
		string? exact = names.FirstOrDefault(name => Normalize(name) == input);
		if (exact is not null) return exact;

		string? suffix = names.FirstOrDefault(name => StripPrefix(Normalize(name)) == input);
		if (suffix is not null) return suffix;

		(string Alias, string Hint) alias = aliases.FirstOrDefault(item => input.Contains(item.Alias, StringComparison.Ordinal));
		if (!string.IsNullOrEmpty(alias.Hint))
		{
			string? matched = names.FirstOrDefault(name => StripPrefix(Normalize(name)).Contains(alias.Hint, StringComparison.Ordinal));
			if (matched is not null) return matched;
		}

		return names.FirstOrDefault(name => Normalize(name).Contains(input, StringComparison.Ordinal));
	}

	private static string Normalize(string value) =>
		new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

	private static string StripPrefix(string value)
	{
		int index = 0;
		while (index < value.Length && char.IsDigit(value[index])) index++;
		return value[index..];
	}
}
