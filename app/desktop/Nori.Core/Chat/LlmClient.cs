using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Chat;

/// <summary>
/// LLM 接口客户端
///
/// 对应 Rust 版 commands.rs 的 fetch_llm_models
/// </summary>
public sealed class LlmClient(HttpClient httpClient)
{
	private readonly HttpClient _httpClient = httpClient;

	/// <summary>
	/// 拉取 OpenAI-compatible /models 列表 (排序去重)
	/// </summary>
	public async Task<IReadOnlyList<string>> FetchModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
	{
		baseUrl = baseUrl.TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");

		using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"{baseUrl}/models"));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

		HttpResponseMessage response;
		try
		{
			response = await _httpClient.SendAsync(request, cancellationToken);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
		{
			throw new ChatException($"请求失败: {exception.Message}", exception);
		}

		using (response)
		{
			if (!response.IsSuccessStatusCode) throw new ChatException($"接口返回错误: HTTP {(int)response.StatusCode}");
			JsonNode? body;
			try
			{
				body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			}
			catch (JsonException exception)
			{
				throw new ChatException($"解析响应失败: {exception.Message}", exception);
			}
			if (body?["data"] is not JsonArray data) throw new ChatException("接口返回成功，但缺少 data 字段");

			SortedSet<string> models = new(StringComparer.Ordinal);
			foreach (JsonNode? item in data)
			{
				// data 里可能直接是字符串, 也可能是 {id: "..."}
				if (item is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0)
				{
					models.Add(text);
					continue;
				}
				if (item?["id"] is JsonValue idValue && idValue.TryGetValue(out string? id) && id.Length > 0) models.Add(id);
			}
			if (models.Count == 0) throw new ChatException("接口返回成功，但没有解析到任何模型");
			return [.. models];
		}
	}
}
