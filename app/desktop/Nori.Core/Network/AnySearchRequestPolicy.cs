namespace Nori.Core.Network;

/// <summary>
/// AnySearch 请求决策结果
/// </summary>
public sealed record AnySearchRequest(Uri Endpoint, string? ApiKey);

/// <summary>
/// AnySearch 端点/凭据策略
///
/// 无副作用地解析搜索端点与密钥的绑定关系:
/// 只有官方 HTTPS 搜索端点允许回退使用已存储的 anysearch_api_key;
/// 自定义端点必须在同一次调用中显式携带自己的 key, 否则在发送前拒绝,
/// 防止存储凭据跟随调用方指定的任意地址外发.
/// </summary>
public static class AnySearchRequestPolicy
{
	/// <summary>官方搜索端点</summary>
	public const string OfficialEndpoint = "https://api.anysearch.com/v1/search";

	/// <summary>
	/// 解析本次请求使用的端点与密钥.
	///
	/// requestedEndpoint 为空时使用官方端点; 显式 key 优先于存储 key.
	/// 非 HTTPS / 非官方路径视为自定义端点, 必须携带显式 key.
	/// </summary>
	public static AnySearchRequest Resolve(string? requestedEndpoint, string? explicitApiKey, string? storedApiKey)
	{
		if (string.IsNullOrWhiteSpace(requestedEndpoint))
		{
			return new AnySearchRequest(new Uri(OfficialEndpoint), FirstNonEmpty(explicitApiKey, storedApiKey));
		}

		if (!Uri.TryCreate(requestedEndpoint.Trim(), UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException("AnySearch 端点必须是 HTTPS 地址");
		}

		if (IsOfficialEndpoint(parsed))
		{
			return new AnySearchRequest(parsed, FirstNonEmpty(explicitApiKey, storedApiKey));
		}

		string? customKey = FirstNonEmpty(explicitApiKey);
		if (customKey is null)
		{
			throw new InvalidOperationException("自定义 AnySearch 端点必须在同一调用中提供 API Key");
		}
		return new AnySearchRequest(parsed, customKey);
	}

	/// <summary>判断 URI 是否为官方搜索端点 (HTTPS + 官方主机 + 搜索路径)</summary>
	private static bool IsOfficialEndpoint(Uri uri)
	{
		if (!uri.Host.Equals("api.anysearch.com", StringComparison.OrdinalIgnoreCase)) return false;
		string path = uri.AbsolutePath.TrimEnd('/');
		return path.Equals("/v1/search", StringComparison.OrdinalIgnoreCase);
	}

	private static string? FirstNonEmpty(params string?[] values) =>
		values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
