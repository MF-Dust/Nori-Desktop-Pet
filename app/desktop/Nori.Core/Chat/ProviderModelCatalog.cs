namespace Nori.Core.Chat;

/// <summary>各 provider 的模型目录适配；排序、去重与回退策略集中于此。</summary>
public static class ProviderModelCatalog
{
	public static Task<IReadOnlyList<string>> FetchAsync(
		LlmProvider provider,
		HttpClient httpClient,
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default) =>
		ChatClientFactory.CreateModelCatalogAdapter(provider, httpClient).FetchModelsAsync(baseUrl, apiKey, cancellationToken);
}
