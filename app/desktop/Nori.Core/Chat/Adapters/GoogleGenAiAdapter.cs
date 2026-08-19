using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Chat.Adapters;

/// <summary>
/// Google GenAI (Gemini) 协议适配器 (/v1beta/models/{model}:generateContent)
/// </summary>
public sealed class GoogleGenAiAdapter(HttpClient httpClient) : ILlmAdapter
{
	private readonly HttpClient _httpClient = httpClient;

	public async Task<string> CompleteAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		CancellationToken cancellationToken = default)
	{
		string cleanModel = NormalizeModelName(model);
		string endpoint = FormatEndpoint(baseUrl, $"models/{cleanModel}:generateContent");

		// Google 格式: system_instruction + contents (role: user / model)
		JsonArray contents = [];
		foreach (ChatMessageInput message in messages)
		{
			string role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
			contents.Add(new JsonObject
			{
				["role"] = role,
				["parts"] = new JsonArray
				{
					new JsonObject {["text"] = message.Content},
				},
			});
		}

		JsonObject payload = new()
		{
			["system_instruction"] = new JsonObject
			{
				["parts"] = new JsonArray
				{
					new JsonObject {["text"] = systemPrompt},
				},
			},
			["contents"] = contents,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint))
		{
			Content = JsonContent.Create(payload),
		};
		request.Headers.Add("x-goog-api-key", apiKey);
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
			if (!response.IsSuccessStatusCode)
			{
				string errorText = await SafeReadErrorAsync(response, cancellationToken);
				throw new ChatException($"接口返回错误: HTTP {(int)response.StatusCode}{(errorText.Length > 0 ? $", {errorText}" : "")}");
			}

			JsonNode? body;
			try
			{
				body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			}
			catch (JsonException exception)
			{
				throw new ChatException($"解析响应失败: {exception.Message}", exception);
			}

			if (body?["candidates"] is JsonArray candidates && candidates.Count > 0)
			{
				StringBuilder sb = new();
				JsonNode? firstCandidate = candidates[0];
				if (firstCandidate?["content"]?["parts"] is JsonArray parts)
				{
					foreach (JsonNode? part in parts)
					{
						if (part?["text"] is JsonValue textVal && textVal.TryGetValue(out string? text))
						{
							sb.Append(text);
						}
					}
				}

				if (sb.Length > 0) return sb.ToString();
			}

			// 兼容可能的回退
			string? fallback = body?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
			if (fallback is not null) return fallback;

			throw new ChatException("接口响应格式异常: 未能解析出回复文本 (缺少 candidates[0].content.parts)");
		}
	}

	public async Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default)
	{
		string endpoint = FormatEndpoint(baseUrl, "models");

		using HttpRequestMessage request = new(HttpMethod.Get, new Uri(endpoint));
		request.Headers.Add("x-goog-api-key", apiKey);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

		HttpResponseMessage? response = null;
		try
		{
			response = await _httpClient.SendAsync(request, cancellationToken);
		}
		catch (Exception)
		{
			// 请求失败回退内置列表
		}

		if (response is not null)
		{
			using (response)
			{
				if (response.IsSuccessStatusCode)
				{
					try
					{
						JsonNode? body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
						if (body?["models"] is JsonArray modelsArray && modelsArray.Count > 0)
						{
							SortedSet<string> models = new(StringComparer.Ordinal);
							foreach (JsonNode? item in modelsArray)
							{
								if (item?["name"] is JsonValue nameVal && nameVal.TryGetValue(out string? name) && !string.IsNullOrWhiteSpace(name))
								{
									// 支持判断是否包含 generateContent
									if (item["supportedGenerationMethods"] is JsonArray methods)
									{
										bool supportsGenerate = methods.Any(m => m is JsonValue mv && mv.TryGetValue(out string? s) && s == "generateContent");
										if (!supportsGenerate) continue;
									}

									models.Add(NormalizeModelName(name));
								}
							}
							if (models.Count > 0) return [.. models];
						}
					}
					catch
					{
						// 解析异常回退
					}
				}
			}
		}

		// 回退内置常见 Gemini 模型列表
		return
		[
			"gemini-2.5-flash",
			"gemini-2.5-pro",
			"gemini-2.0-flash",
			"gemini-1.5-flash",
			"gemini-1.5-pro",
		];
	}

	private static string NormalizeModelName(string model)
	{
		string trimmed = model.Trim();
		if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
		{
			return trimmed["models/".Length..];
		}
		return trimmed;
	}

	private static string FormatEndpoint(string baseUrl, string path)
	{
		baseUrl = baseUrl.Trim().TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");

		// 若 baseUrl 中包含了具体 model 路径需去除
		if (path.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
		{
			if (baseUrl.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
			{
				int idx = baseUrl.IndexOf("/models/", StringComparison.OrdinalIgnoreCase);
				if (idx >= 0) baseUrl = baseUrl[..idx].TrimEnd('/');
			}
			return $"{baseUrl}/{path}";
		}

		if (path == "models")
		{
			if (baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase)) return baseUrl;
			return $"{baseUrl}/models";
		}

		return $"{baseUrl}/{path}";
	}

	private static async Task<string> SafeReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
	{
		try
		{
			string raw = await response.Content.ReadAsStringAsync(ct);
			if (string.IsNullOrWhiteSpace(raw)) return "";
			if (JsonNode.Parse(raw) is { } node)
			{
				string? msg = node["error"]?["message"]?.GetValue<string>() ?? node["message"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(msg)) return msg;
			}
			return raw.Length > 200 ? raw[..200] : raw;
		}
		catch
		{
			return "";
		}
	}
}
