namespace Nori.Core.Live2D;

/// <summary>
/// 当前版本支持的桌宠模型目录。
///
/// 模型资源仍然只从本地导入；这里的白名单只负责启动与窗口生命周期契约，
/// 不负责解析模型文件内容。
/// </summary>
public static class KnownModelIds
{
	/// <summary>ARG Nori 模型 ID</summary>
	public const string ArgNori = "arg-nori";

	/// <summary>Nori 模型 ID</summary>
	public const string Nori = "nori";

	/// <summary>当前版本可选择的模型 ID</summary>
	public static IReadOnlyList<string> All { get; } = [ArgNori, Nori];

	/// <summary>
	/// 将外部模型 ID 规范化为已知 ID；未知或空值返回 null。
	/// </summary>
	public static string? Normalize(string? modelId)
	{
		if (string.Equals(modelId?.Trim(), ArgNori, StringComparison.OrdinalIgnoreCase)) return ArgNori;
		if (string.Equals(modelId?.Trim(), Nori, StringComparison.OrdinalIgnoreCase)) return Nori;
		return null;
	}
}
