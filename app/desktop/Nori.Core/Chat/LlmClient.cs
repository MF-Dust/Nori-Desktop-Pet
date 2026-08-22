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
		ILlmAdapter adapter = CreateAdapter(provider, _httpClient);
		return adapter.FetchModelsAsync(baseUrl, apiKey, cancellationToken);
	}

	/// <summary>
	/// 兼容老接口 (默认 OpenAI 协议)
	/// </summary>
	public Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default)
	{
		return FetchModelsAsync(null, baseUrl, apiKey, cancellationToken);
	}

	public static ILlmAdapter CreateAdapter(LlmProvider provider, HttpClient httpClient) =>
		ChatClientFactory.Create(provider, httpClient);
}
