using Nori.Core.Security;

namespace Nori.Core.Chat;

/// <summary>
/// 聊天服务端点地址校验。
///
/// Base URL 来自用户配置, 必须严格校验: 只接受绝对 http/https 地址,
/// 不猜测补协议头。错误消息只含清洗后的地址, 不携带查询串与凭据。
/// </summary>
public static class ChatEndpoint
{
	/// <summary>解析绝对 http/https 地址; 非法时抛 ChatException, 供前端直接提示。</summary>
	public static Uri CreateHttpUri(string endpoint)
	{
		if (!TryCreateHttpUri(endpoint, out Uri? uri) || uri is null)
			throw new ChatException($"Base URL 格式无效: {Describe(endpoint)}");
		return uri;
	}

	/// <summary>尝试解析绝对 http/https 地址; 空串、相对地址与非 http(s) 协议都视为非法。</summary>
	public static bool TryCreateHttpUri(string? endpoint, out Uri? uri)
	{
		uri = null;
		if (string.IsNullOrWhiteSpace(endpoint)) return false;
		if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? parsed)) return false;
		if (parsed.Scheme is not ("http" or "https")) return false;
		uri = parsed;
		return true;
	}

	private static string Describe(string? endpoint)
	{
		if (string.IsNullOrWhiteSpace(endpoint)) return "地址为空";
		string trimmed = endpoint.Trim();
		if (TryCreateHttpUri(trimmed, out Uri? parsed) && parsed is not null)
		{
			// 保留协议/主机/路径用于用户自查, 丢掉查询串与用户信息, 避免把带 key 的地址写进错误消息。
			UriComponents safeComponents = UriComponents.AbsoluteUri & ~UriComponents.Query & ~UriComponents.UserInfo;
			return parsed.GetComponents(safeComponents, UriFormat.SafeUnescaped);
		}
		return SensitiveDataRedactor.Redact(trimmed);
	}
}
