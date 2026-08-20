using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Chat.Adapters;

/// <summary>
/// OpenAI Responses 协议适配器 (/responses)
/// </summary>
public sealed class OpenAiResponsesAdapter(HttpClient httpClient) : ILlmAdapter
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
		string endpoint = FormatEndpoint(baseUrl, "responses");

		JsonArray inputList = [];
		foreach (ChatMessageInput message in messages)
		{
			inputList.Add(new JsonObject
			{
				["role"] = message.Role,
				["content"] = message.Content,
			});
		}

		JsonObject payload = new()
		{
			["model"] = model,
			["instructions"] = systemPrompt,
			["input"] = inputList,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint))
		{
			Content = JsonContent.Create(payload),
		};
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

			// 1. 优先尝试直接从 output_text 字段获取
			if (body?["output_text"] is JsonValue outputTextVal && outputTextVal.TryGetValue(out string? outputText) && !string.IsNullOrEmpty(outputText))
			{
				return outputText;
			}

			// 2. 解析 output 数组: output[].content[].text
			if (body?["output"] is JsonArray outputArray)
			{
				StringBuilder sb = new();
				foreach (JsonNode? item in outputArray)
				{
					if (item is null) continue;
					if (item["content"] is JsonArray contentArray)
					{
						foreach (JsonNode? contentItem in contentArray)
						{
							if (contentItem?["text"] is JsonValue textVal && textVal.TryGetValue(out string? text))
							{
								sb.Append(text);
							}
						}
					}
					else if (item["text"] is JsonValue itemTextVal && itemTextVal.TryGetValue(out string? itemText))
					{
						sb.Append(itemText);
					}
				}

				if (sb.Length > 0) return sb.ToString();
			}

			// 3. 兼容 choices 回退
			if (body?["choices"] is JsonArray choices && choices.Count > 0)
			{
				string? fallback = choices[0]?["message"]?["content"]?.GetValue<string>()
					?? choices[0]?["text"]?.GetValue<string>();
				if (fallback is not null) return fallback;
			}

			throw new ChatException("接口响应格式异常: 未能解析出回复文本 (缺少 output / output_text)");
		}
	}

	public async Task<string> StreamAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onChunk,
		CancellationToken cancellationToken = default)
	{
		string endpoint = FormatEndpoint(baseUrl, "responses");

		JsonArray inputList = [];
		foreach (ChatMessageInput message in messages)
		{
			inputList.Add(new JsonObject
			{
				["role"] = message.Role,
				["content"] = message.Content,
			});
		}

		JsonObject payload = new()
		{
			["model"] = model,
			["instructions"] = systemPrompt,
			["input"] = inputList,
			["stream"] = true,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint))
		{
			Content = JsonContent.Create(payload),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

		HttpResponseMessage response;
		try
		{
			response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

			using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			using StreamReader reader = new(stream);
			StringBuilder fullText = new();

			while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken) is { } rawLine)
			{
				string line = rawLine.Trim();
				if (line.Length == 0 || line.StartsWith(':')) continue;

				if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
				{
					string data = line["data:".Length..].Trim();
					if (data == "[DONE]") break;

					try
					{
						JsonNode? node = JsonNode.Parse(data);
						// responses 格式可能为 response.output_text.delta 或 choices[0].delta.content
						string? delta = node?["delta"]?.GetValue<string>();
						if (string.IsNullOrEmpty(delta) && node?["choices"] is JsonArray chunkChoices && chunkChoices.Count > 0)
						{
							delta = chunkChoices[0]?["delta"]?["content"]?.GetValue<string>()
								?? chunkChoices[0]?["text"]?.GetValue<string>();
						}

						if (!string.IsNullOrEmpty(delta))
						{
							fullText.Append(delta);
							onChunk(delta);
						}
					}
					catch (Exception)
					{
						/* 忽略不完整或格式异常分片 */
					}
				}
			}

			return fullText.ToString();
		}
	}

	public async Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default)
	{
		string endpoint = FormatEndpoint(baseUrl, "models");

		using HttpRequestMessage request = new(HttpMethod.Get, new Uri(endpoint));
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

			if (body?["data"] is not JsonArray data) throw new ChatException("接口返回成功，但缺少 data 字段");

			SortedSet<string> models = new(StringComparer.Ordinal);
			foreach (JsonNode? item in data)
			{
				if (item is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
				{
					models.Add(text);
					continue;
				}
				if (item?["id"] is JsonValue idValue && idValue.TryGetValue(out string? id) && !string.IsNullOrWhiteSpace(id))
				{
					models.Add(id);
				}
			}

			if (models.Count == 0) throw new ChatException("接口返回成功，但没有解析到任何模型");
			return [.. models];
		}
	}

	private static string FormatEndpoint(string baseUrl, string path)
	{
		baseUrl = baseUrl.Trim().TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");

		if (path == "responses")
		{
			if (baseUrl.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)) return baseUrl;
			return $"{baseUrl}/responses";
		}

		if (path == "models")
		{
			if (baseUrl.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^"/responses".Length].TrimEnd('/');
			}
			else if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^"/chat/completions".Length].TrimEnd('/');
			}
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
