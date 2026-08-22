using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Nori.Core.Tools;

namespace Nori.Core.Network;

/// <summary>
/// 受限网页正文抓取。
/// SSRF 防护 + 安全重定向 + 体积上限 + HTML DOM 清理，输出截断到 3000 字符。
/// </summary>
public sealed class WebPageFetcher(HttpClient httpClient) : IWebPageFetcher
{
	/// <summary>正文摘要长度上限</summary>
	public const int SummaryLength = 3000;

	/// <inheritdoc />
	public async Task<object> FetchAsync(string url, CancellationToken cancellationToken = default)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
		{
			throw new InvalidOperationException($"不允许访问的地址: {url}");
		}

		using HttpResponseMessage response = await UrlAccessPolicy.GetWithSafeRedirectsAsync(
			httpClient, uri, allowPrivate: false, cancellationToken: cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"无法获取网页内容: HTTP {(int)response.StatusCode}");
		}

		string html = await UrlAccessPolicy.ReadCappedTextAsync(response.Content, UrlAccessPolicy.MaxResponseBytes, cancellationToken);

		HtmlParser parser = new();
		IDocument document = await parser.ParseDocumentAsync(html, cancellationToken);
		foreach (IElement element in document.QuerySelectorAll("script,style,noscript,template,iframe").ToArray())
		{
			element.Remove();
		}

		string cleaned = (document.Body?.TextContent ?? document.TextContent)
			.Replace('\u00a0', ' ')
			.Trim();
		cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		if (cleaned.Length > SummaryLength) cleaned = cleaned[..SummaryLength];

		return new {url, content = cleaned};
	}

}
