using System.Text;
using System.Text.RegularExpressions;
using Nori.Core.Tools;

namespace Nori.Core.Network;

/// <summary>
/// 受限网页正文抓取
///
/// SSRF 防护 + 安全重定向 + 体积上限 + 标签剥离, 输出截断到 3000 字符的正文摘要。
/// </summary>
public sealed class WebPageFetcher(HttpClient httpClient) : IWebPageFetcher
{
	/// <summary>正文摘要长度上限</summary>
	public const int SummaryLength = 3000;

	/// <inheritdoc />
	public async Task<object> FetchAsync(string url, CancellationToken cancellationToken = default)
	{
		Uri uri = new(url);
		using HttpResponseMessage response = await UrlAccessPolicy.GetWithSafeRedirectsAsync(
			httpClient, uri, allowPrivate: false, cancellationToken: cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"无法获取网页内容: HTTP {(int)response.StatusCode}");
		}

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		string text = await ReadCappedAsync(stream, UrlAccessPolicy.MaxResponseBytes, cancellationToken);

		// 移除 script/style 标签与 HTML 标记
		string cleaned = Regex.Replace(text, "<script\\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		cleaned = Regex.Replace(cleaned, "<style\\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		cleaned = Regex.Replace(cleaned, "<style\\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
		cleaned = Regex.Replace(cleaned, "<[^>]+>", " ");
		cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();

		return new {url, content = cleaned[..Math.Min(cleaned.Length, SummaryLength)]};
	}

	private static async Task<string> ReadCappedAsync(Stream stream, long cap, CancellationToken ct)
	{
		using StreamReader reader = new(stream, Encoding.UTF8);
		char[] buffer = new char[64 * 1024];
		StringBuilder builder = new();
		while (true)
		{
			int read = await reader.ReadBlockAsync(buffer, ct);
			if (read <= 0) break;
			builder.Append(buffer, 0, read);
			if (builder.Length > cap / 2) // 字符数近似字节上限的一半, 留出多字节余量
			{
				throw new InvalidOperationException($"远程文件超过大小上限 ({cap / 1024 / 1024} MB)");
			}
		}
		return builder.ToString();
	}
}
