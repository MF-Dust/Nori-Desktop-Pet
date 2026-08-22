using System.Net;
using System.Net.Sockets;

namespace Nori.Core.Network;

/// <summary>
/// 受限的后端 URL 访问策略
///
/// 网页抓取、技能/MCP URL 导入与搜索统一走这里:
/// 拒绝非 HTTP(S)、私网/回环 SSRF、危险重定向与超大响应;
/// 显式配置的本地 LLM / GPT-SoVITS 端点单独允许 (allowPrivate)。
/// </summary>
public static class UrlAccessPolicy
{
	/// <summary>抓取响应的体积上限</summary>
	public const long MaxResponseBytes = 3 * 1024 * 1024;

	/// <summary>重定向跟随上限</summary>
	public const int MaxRedirects = 5;

	/// <summary>
	/// 校验公网 HTTP(S) 地址, 违规抛 InvalidOperationException (消息面向用户)
	/// </summary>
	public static void EnsurePublicHttp(Uri uri) => EnsureAllowed(uri, allowPrivate: false);

	/// <summary>
	/// 校验 HTTP(S) 地址; allowPrivate 时放行回环/私网 (本地 LLM、GPT-SoVITS 等)
	/// </summary>
	public static void EnsureAllowed(Uri uri, bool allowPrivate)
	{
		if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
		{
			throw new InvalidOperationException($"不允许访问的地址 (仅支持 http/https): {uri}");
		}
		if (string.IsNullOrEmpty(uri.Host))
		{
			throw new InvalidOperationException($"不允许访问的地址 (缺少主机名): {uri}");
		}
		if (allowPrivate) return;

		foreach (IPAddress address in ResolveAddresses(uri.Host))
		{
			if (IsRestricted(address))
			{
				throw new InvalidOperationException($"不允许访问私网或保留地址: {uri.Host}");
			}
		}
	}

	/// <summary>
	/// 解析主机名的全部 IP; 解析失败按主机名字面量判断
	/// </summary>
	private static IReadOnlyList<IPAddress> ResolveAddresses(string host)
	{
		if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? literal))
		{
			return [literal];
		}
		try
		{
			return Dns.GetHostAddresses(host);
		}
		catch
		{
			// DNS 失败时交给后续真正的请求去报错
			return [];
		}
	}

	/// <summary>内部判定, 测试可见: 地址是否回环/私网/链路本地/保留</summary>
	public static bool IsRestricted(IPAddress address) => address.AddressFamily switch
	{
		AddressFamily.InterNetwork => IsRestrictedIPv4(address.GetAddressBytes()),
		AddressFamily.InterNetworkV6 => IsRestrictedIPv6(address),
		_ => true,
	};

	private static bool IsRestrictedIPv4(byte[] bytes) =>
		bytes[0] == 127                                  // 回环
		|| bytes[0] == 10                                // 10/8
		|| (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
		|| (bytes[0] == 192 && bytes[1] == 168)          // 192.168/16
		|| bytes[0] == 169 && bytes[1] == 254            // 链路本地
		|| bytes[0] == 0                                 // 未指定
		|| bytes[0] >= 224;                              // 组播 / 保留

	private static bool IsRestrictedIPv6(IPAddress address)
	{
		if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
		// IPv4 映射地址按 IPv4 规则判断
		if (address.IsIPv4MappedToIPv6)
		{
			byte[] mapped = address.MapToIPv4().GetAddressBytes();
			return IsRestrictedIPv4(mapped);
		}
		byte[] v6 = address.GetAddressBytes();
		// 回环 ::1 与未指定 ::
		bool allZero = v6.All(b => b == 0);
		if (allZero) return true;
		return v6[15] == 1 && v6[..15].All(b => b == 0);
	}

	/// <summary>
	/// GET 抓取: 手动跟随重定向并逐跳校验, 规避自动重定向绕过 SSRF 检查。
	/// 返回的响应由调用方负责释放。
	/// </summary>
	public static async Task<HttpResponseMessage> GetWithSafeRedirectsAsync(
		HttpClient httpClient,
		Uri uri,
		bool allowPrivate,
		int maxRedirects = MaxRedirects,
		CancellationToken cancellationToken = default)
	{
		EnsureAllowed(uri, allowPrivate);
		Uri current = uri;

		for (int hop = 0; ; hop++)
		{
			HttpResponseMessage response = await httpClient.GetAsync(
				current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } next)
			{
				return response;
			}
			if (hop >= maxRedirects)
			{
				response.Dispose();
				throw new InvalidOperationException($"重定向次数超过上限 ({maxRedirects}): {current}");
			}

			current = Uri.IsWellFormedUriString(next.ToString(), UriKind.Absolute)
				? next
				: new Uri(current, next);
			// 每一跳都重新校验: 防止公网服务把请求重定向进私网
			try
			{
				EnsureAllowed(current, allowPrivate);
			}
			catch
			{
				response.Dispose();
				throw;
			}
		}
	}
}
