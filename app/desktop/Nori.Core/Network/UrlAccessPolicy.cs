using System.Net;
using System.Net.Sockets;
using Microsoft.Security.AntiSSRF;
using System.Text;

namespace Nori.Core.Network;

/// <summary>
/// 受限的后端 URL 访问策略。
///
/// 公网请求先做 scheme/主机/IP 校验，再由 AntiSSRF 在真实连接时复核；
/// 重定向由此处逐跳跟随，避免自动重定向绕过策略。
/// </summary>
public static class UrlAccessPolicy
{
	/// <summary>抓取响应的体积上限</summary>
	public const long MaxResponseBytes = 3 * 1024 * 1024;

	/// <summary>重定向跟随上限</summary>
	public const int MaxRedirects = 5;

	/// <summary>校验公网 HTTP(S) 地址。</summary>
	public static void EnsurePublicHttp(Uri uri) => EnsureAllowed(uri, allowPrivate: false);

	/// <summary>把公网客户端的安全拦截与传输失败翻成用户可见的中文异常。</summary>
	public static InvalidOperationException Translate(Exception exception, Uri? uri = null)
	{
		if (exception is InvalidOperationException invalidOperation) return invalidOperation;
		string host = uri?.Host ?? "目标地址";
		if (ContainsAntiSsrf(exception))
		{
			return new InvalidOperationException($"公网地址被安全策略拒绝: {host}", exception);
		}
		if (exception is HttpRequestException)
		{
			return new InvalidOperationException($"访问公网地址失败: {host}", exception);
		}
		return new InvalidOperationException($"访问公网地址失败: {host}", exception);
	}

	/// <summary>校验 HTTP(S) 地址; allowPrivate 仅用于显式配置的本地端点。</summary>
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

		// 域名的最终 IP 由 AntiSSRF connect-time handler 校验；这里仅拦截
		// 可直接识别的字面量与 localhost，避免 DNS 预解析造成 TOCTOU。
		if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"不允许访问私网或保留地址: {uri.Host}");
		}
		if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? literal) && IsRestricted(literal))
		{
			throw new InvalidOperationException($"不允许访问私网或保留地址: {uri.Host}");
		}
	}

	/// <summary>
	/// 公网请求必须直连。系统代理无法被 AntiSSRFHandler 配置时，若该 URL 会走代理则拒绝，
	/// 防止代理成为 SSRF 绕过路径。
	/// </summary>
	public static void EnsureDirectRoute(Uri uri, IWebProxy? proxy = null)
	{
		proxy ??= HttpClient.DefaultProxy;
		if (proxy is null) return;
		try
		{
			if (!proxy.IsBypassed(uri))
			{
				throw new InvalidOperationException($"公网请求被系统代理接管，已拒绝: {uri.Host}");
			}
		}
		catch (InvalidOperationException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new InvalidOperationException($"无法确认公网请求是否经过代理，已拒绝: {uri.Host}", exception);
		}
	}

	/// <summary>内部判定, 测试可见: 地址是否回环/私网/链路本地/保留</summary>
	public static bool IsRestricted(IPAddress address) => address.AddressFamily switch
	{
		AddressFamily.InterNetwork => IsRestrictedIPv4(address.GetAddressBytes()),
		AddressFamily.InterNetworkV6 => IsRestrictedIPv6(address),
		_ => true,
	};

	/// <summary>
	/// GET 抓取: 手动跟随重定向并逐跳校验。返回的响应由调用方负责释放。
	/// </summary>
	public static async Task<HttpResponseMessage> GetWithSafeRedirectsAsync(
		HttpClient httpClient,
		Uri uri,
		bool allowPrivate,
		int maxRedirects = MaxRedirects,
		CancellationToken cancellationToken = default)
	{
		EnsureAllowed(uri, allowPrivate);
		if (!allowPrivate) EnsureDirectRoute(uri);
		Uri current = uri;

		for (int hop = 0; ; hop++)
		{
			HttpResponseMessage response;
			try
			{
				response = await httpClient.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (AntiSSRFException exception)
			{
				throw Translate(exception, current);
			}
			catch (HttpRequestException exception)
			{
				throw Translate(exception, current);
			}

			if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } next)
			{
				return response;
			}
			if (hop >= maxRedirects)
			{
				response.Dispose();
				throw new InvalidOperationException($"重定向次数超过上限 ({maxRedirects}): {current}");
			}

			current = next.IsAbsoluteUri ? next : new Uri(current, next);
			try
			{
				EnsureAllowed(current, allowPrivate);
				if (!allowPrivate) EnsureDirectRoute(current);
			}
			catch
			{
				response.Dispose();
				throw;
			}
			response.Dispose();
		}
	}

	/// <summary>按 UTF-8 字节上限读取响应文本，避免错误服务耗尽内存。</summary>
	public static async Task<string> ReadCappedTextAsync(
		HttpContent content,
		long cap = MaxResponseBytes,
		CancellationToken cancellationToken = default)
	{
		await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
		using MemoryStream output = new();
		byte[] buffer = new byte[64 * 1024];
		long total = 0;
		while (true)
		{
			int read = await stream.ReadAsync(buffer, cancellationToken);
			if (read <= 0) break;
			total += read;
			if (total > cap)
			{
				throw new InvalidOperationException($"远程文件超过大小上限 ({cap / 1024 / 1024} MB)");
			}
			output.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(output.ToArray());
	}

	private static bool ContainsAntiSsrf(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is AntiSSRFException) return true;
		}
		return false;
	}

	private static bool IsRestrictedIPv4(byte[] bytes) =>
		bytes[0] == 127
		|| bytes[0] == 10
		|| (bytes[0] == 100 && (bytes[1] & 0xc0) == 0x40)
		|| (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
		|| (bytes[0] == 192 && bytes[1] == 168)
		|| (bytes[0] == 192 && bytes[1] == 0)
		|| (bytes[0] == 192 && bytes[1] == 31 && bytes[2] == 196)
		|| (bytes[0] == 192 && bytes[1] == 52 && bytes[2] == 193)
		|| (bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
		|| (bytes[0] == 192 && bytes[1] == 175 && bytes[2] == 48)
		|| (bytes[0] == 169 && bytes[1] == 254)
		|| (bytes[0] == 168 && bytes[1] == 63 && bytes[2] == 129 && bytes[3] == 16)
		|| (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
		|| (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
		|| (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
		|| bytes[0] == 0
		|| bytes[0] >= 224;

	private static bool IsRestrictedIPv6(IPAddress address)
	{
		if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
		if (address.IsIPv4MappedToIPv6) return IsRestrictedIPv4(address.MapToIPv4().GetAddressBytes());
		byte[] bytes = address.GetAddressBytes();
		if (bytes.All(value => value == 0)) return true;
		if (bytes[15] == 1 && bytes[..15].All(value => value == 0)) return true;
		if ((bytes[0] & 0xfe) == 0xfc || bytes[0] == 0xff) return true;
		if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
		// 2001::/23（仅覆盖该保留范围，不能把 2001:4860 等公网地址一并拒绝）。
		if (bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] & 0x80) == 0) return true;
		if (bytes[0] == 0x01 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0) return true;
		if (bytes[0] == 0x01 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 1 && bytes[8..].All(value => value == 0)) return true;
		if (bytes[0] == 0 && bytes[1] == 0x40 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes[4..].All(value => value == 0)) return true;
		if (bytes[0] == 0 && bytes[1] == 0x40 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes[4] == 0 && bytes[5] == 1 && bytes[6..].All(value => value == 0)) return true;
		if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return true;
		if (bytes[0] == 0x20 && bytes[1] == 0x02) return true;
		if (bytes[0] == 0x26 && bytes[1] == 0x20 && bytes[2] == 0 && bytes[3] == 0x4f && bytes[4] == 0x80 && bytes[5] == 0) return true;
		if (bytes[0] == 0x3f && bytes[1] >= 0xf0) return true;
		if (bytes[0] == 0x5f && bytes[1] == 0) return true;
		return false;
	}
}
