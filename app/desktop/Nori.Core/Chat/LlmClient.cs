namespace Nori.Core.Chat;

/// <summary>
/// LLM 接口客户端 (模型获取与管理)
/// </summary>
public sealed class LlmClient(HttpClient httpClient)
{
	private readonly HttpClient _httpClient = httpClient;

	/// <summary>
	/// 拉取指定协议的模型列表 (排序去重)
	/// </summary>
	public Task<IReadOnlyList<string>> FetchModelsAsync(
		string? providerStr,
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default)
	{
		LlmProvider provider = LlmProviderExtensions.ParseProvider(providerStr);
		return ProviderModelCatalog.FetchAsync(provider, _httpClient, baseUrl, apiKey, cancellationToken);
	}

	public static ILlmAdapter CreateAdapter(LlmProvider provider, HttpClient httpClient) =>
		ChatClientFactory.Create(provider, httpClient);
}
