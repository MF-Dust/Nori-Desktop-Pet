namespace Nori.Core.Live2D;

/// <summary>
/// Nori 内置支持的 Live2D 模型 ID.
///
/// 这里只维护已经随应用约定的两个模型, 不根据文件名动态生成新的模型 ID.
/// </summary>
public static class SupportedModelIds
{
	/// <summary>ARG Nori 模型 ID.</summary>
	public const string ArgNori = "arg-nori";

	/// <summary>Nori 模型 ID.</summary>
	public const string Nori = "nori";

	/// <summary>所有受支持的模型 ID.</summary>
	public static IReadOnlyList<string> All { get; } = [ArgNori, Nori];

	/// <summary>
	/// 判断模型 ID 是否为 Nori 已支持的固定 ID.
	/// </summary>
	public static bool IsSupported(string modelId) =>
		modelId.Equals(ArgNori, StringComparison.OrdinalIgnoreCase)
		|| modelId.Equals(Nori, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 从 model3.json 文件名或其父目录解析固定模型 ID.
	///
	/// 文件名优先支持 ARGNori.model3.json / arg-nori.model3.json 与 Nori.model3.json;
	/// 只有父目录本身就是两个固定 ID 时才使用父目录, 不做任意名称转换.
	/// </summary>
	public static string? ResolveFromModelPath(string relativePath)
	{
		string[] segments = relativePath
			.Replace('\\', '/')
			.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0) return null;

		string? fromFile = ResolveKnownFileName(segments[^1]);
		if (fromFile is not null) return fromFile;

		for (int index = segments.Length - 2; index >= 0; index--)
		{
			string? fromDirectory = ResolveKnownId(segments[index]);
			if (fromDirectory is not null) return fromDirectory;
		}

		return null;
	}

	private static string? ResolveKnownFileName(string fileName)
	{
		const string suffix = ".model3.json";
		if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
		string stem = fileName[..^suffix.Length];
		return stem.Equals("ARGNori", StringComparison.OrdinalIgnoreCase)
			|| stem.Equals(ArgNori, StringComparison.OrdinalIgnoreCase)
			? ArgNori
			: stem.Equals(Nori, StringComparison.OrdinalIgnoreCase) ? Nori : null;
	}

	private static string? ResolveKnownId(string value) => value.ToLowerInvariant() switch
	{
		ArgNori => ArgNori,
		Nori => Nori,
		_ => null,
	};
}
