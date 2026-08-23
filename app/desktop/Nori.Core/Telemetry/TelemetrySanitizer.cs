using System.Text.RegularExpressions;

namespace Nori.Core.Telemetry;

/// <summary>
/// 遥测发送前的纯函数脱敏器。
///
/// 事件只允许携带固定操作名与运行环境标签; 异常正文不跨出本机。这里的文本脱敏仍作为
/// 最后一层保险, 防止 SDK 或未来调用方把凭据放进了错误摘要、URL 或路径。
/// </summary>
public static partial class TelemetrySanitizer
{
	private const int MaxOperationLength = 80;

	/// <summary>把操作名压缩为不含用户输入的 ASCII 标识。</summary>
	public static string NormalizeOperation(string? operation)
	{
		if (string.IsNullOrWhiteSpace(operation)) return "operation";

		string normalized = NonOperationCharacterRegex().Replace(operation.Trim(), "_").Trim('_');
		if (normalized.Length == 0) return "operation";
		return normalized.Length > MaxOperationLength ? normalized[..MaxOperationLength] : normalized;
	}

	/// <summary>
	/// 脱敏普通诊断文本。
	///
	/// 不把它用于上传聊天正文; 对异常值应使用 SanitizeExceptionValue, 只保留类型。
	/// </summary>
	public static string ScrubText(string? value)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		string scrubbed = CredentialUrlRegex().Replace(value, "$1[redacted]@");
		scrubbed = QuerySecretRegex().Replace(scrubbed, "$1[redacted]");
		scrubbed = AssignmentSecretRegex().Replace(scrubbed, "$1[redacted]");
		scrubbed = PathRegex().Replace(scrubbed, "[path]");
		return scrubbed.Length > 240 ? scrubbed[..240] : scrubbed;
	}

	/// <summary>异常正文只保留类型, 不上传消息、提示词、聊天内容或请求结果。</summary>
	public static string SanitizeExceptionValue(Exception? exception) =>
		exception is null ? "Exception" : exception.GetType().FullName ?? exception.GetType().Name;

	[GeneratedRegex(@"[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
	private static partial Regex NonOperationCharacterRegex();

	[GeneratedRegex(@"(?i)(https?://)[^\s/@:]+(?::[^\s/@]*)?@", RegexOptions.CultureInvariant)]
	private static partial Regex CredentialUrlRegex();

	[GeneratedRegex(@"(?i)([?&](?:api[_-]?key|authorization|bearer|token|password|secret|cookie)=)[^&#\s]*", RegexOptions.CultureInvariant)]
	private static partial Regex QuerySecretRegex();

	[GeneratedRegex(@"(?i)((?:api[_-]?key|authorization|bearer|token|password|secret|cookie)\s*[:=]\s*)[^\s,;]+", RegexOptions.CultureInvariant)]
	private static partial Regex AssignmentSecretRegex();

	[GeneratedRegex(@"(?i)(?:[A-Za-z]:\\|/Users/|/home/|/tmp/|/var/folders/)[^\r\n\s]+", RegexOptions.CultureInvariant)]
	private static partial Regex PathRegex();
}
