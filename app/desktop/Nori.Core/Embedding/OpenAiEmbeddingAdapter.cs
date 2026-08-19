using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nori.Core.Embedding;

/// <summary>
/// OpenAI 兼容规范 Embedding 适配器 (支持 BGE-M3 / OpenAI / SiliconFlow / Ollama / LocalAI 等)
/// </summary>
public sealed class OpenAiEmbeddingAdapter(HttpClient httpClient) : IEmbeddingAdapter
{
	private readonly HttpClient _httpClient = httpClient;

	public async Task<float[]> GetEmbeddingAsync(
		string baseUrl,
		string apiKey,
		string model,
		string input,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<float[]> results = await GetEmbeddingsAsync(baseUrl, apiKey, model, [input], cancellationToken);
		if (results.Count == 0)
		{
			throw new InvalidOperationException("Embedding 服务未返回任何向量数据。");
		}
		return results[0];
	}

	public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<string> inputs,
		CancellationToken cancellationToken = default)
	{
		string normalizedBase = baseUrl.Trim().TrimEnd('/');
		string endpoint = normalizedBase.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase)
			? normalizedBase
			: $"{normalizedBase}/embeddings";

		using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
		if (!string.IsNullOrWhiteSpace(apiKey))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
		}

		var payload = new
		{
			input = inputs.Count == 1 ? (object)inputs[0] : inputs,
			model = string.IsNullOrWhiteSpace(model) ? "BAAI/bge-m3" : model.Trim()
		};

		request.Content = new StringContent(
			JsonSerializer.Serialize(payload),
			Encoding.UTF8,
			"application/json");

		using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
		string body = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Embedding 请求失败 ({response.StatusCode}): {body}");
		}

		using JsonDocument doc = JsonDocument.Parse(body);
		if (!doc.RootElement.TryGetProperty("data", out JsonElement dataElement) ||
		    dataElement.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException($"Embedding 响应格式不正确，缺少 data 数组: {body}");
		}

		List<(int index, float[] vector)> list = [];
		foreach (JsonElement item in dataElement.EnumerateArray())
		{
			int index = item.TryGetProperty("index", out JsonElement idxElem) ? idxElem.GetInt32() : list.Count;
			if (item.TryGetProperty("embedding", out JsonElement embElem) && embElem.ValueKind == JsonValueKind.Array)
			{
				int len = embElem.GetArrayLength();
				float[] vec = new float[len];
				int i = 0;
				foreach (JsonElement val in embElem.EnumerateArray())
				{
					vec[i++] = (float)val.GetDouble();
				}
				list.Add((index, vec));
			}
		}

		// 按 index 排序确保输入与输出对应
		list.Sort((a, b) => a.index.CompareTo(b.index));
		return list.ConvertAll(x => x.vector);
	}
}
