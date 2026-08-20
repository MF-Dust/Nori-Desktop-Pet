using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Chat.Adapters;

/// <summary>
/// Anthropic Messages 协议适配器 (/messages)
/// </summary>
public sealed class AnthropicAdapter(HttpClient httpClient) : ILlmAdapter
{
	private const string AnthropicVersion = "2023-06-01";
	private readonly HttpClient _httpClient = httpClient;

	public async Task<string> CompleteAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		CancellationToken cancellationToken = default)
	{
		string endpoint = FormatEndpoint(baseUrl, "messages");

		// Anthropic 要求 messages 中只能包含 user/assistant 且第一条必须是 user，并且不能有相邻的相同 role
		JsonArray anthropicMessages = NormalizeMessages(messages);
		if (anthropicMessages.Count == 0)
		{
			throw new ChatException("有效消息列表为空");
		}

		JsonObject payload = new()
		{
			["model"] = model,
			["max_tokens"] = 4096,
			["system"] = systemPrompt,
			["messages"] = anthropicMessages,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint))
		{
			Content = JsonContent.Create(payload),
		};
		request.Headers.Add("x-api-key", apiKey);
		request.Headers.Add("anthropic-version", AnthropicVersion);
		// 兼顾部分自建反代
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

			if (body?["content"] is JsonArray contentArray)
			{
				StringBuilder sb = new();
				foreach (JsonNode? item in contentArray)
				{
					if (item?["type"]?.GetValue<string>() == "text" && item["text"] is JsonValue textVal && textVal.TryGetValue(out string? text))
					{
						sb.Append(text);
					}
					else if (item?["text"] is JsonValue directText && directText.TryGetValue(out string? directTextStr))
					{
						sb.Append(directTextStr);
					}
				}

				if (sb.Length > 0) return sb.ToString();
			}

			// 兼容其他格式回退
			if (body?["choices"] is JsonArray choices && choices.Count > 0)
			{
				string? fallback = choices[0]?["message"]?["content"]?.GetValue<string>()
					?? choices[0]?["text"]?.GetValue<string>();
				if (fallback is not null) return fallback;
			}

			throw new ChatException("接口响应格式异常: 未能解析出回复文本 (缺少 content[].text)");
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
		string endpoint = FormatEndpoint(baseUrl, "messages");

		JsonArray anthropicMessages = NormalizeMessages(messages);
		if (anthropicMessages.Count == 0)
		{
			throw new ChatException("有效消息列表为空");
		}

		JsonObject payload = new()
		{
			["model"] = model,
			["max_tokens"] = 4096,
			["system"] = systemPrompt,
			["messages"] = anthropicMessages,
			["stream"] = true,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint))
		{
			Content = JsonContent.Create(payload),
		};
		request.Headers.Add("x-api-key", apiKey);
		request.Headers.Add("anthropic-version", AnthropicVersion);
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
					try
					{
						JsonNode? node = JsonNode.Parse(data);
						string? type = node?["type"]?.GetValue<string>();
						if (type == "content_block_delta")
						{
							string? text = node?["delta"]?["text"]?.GetValue<string>();
							if (!string.IsNullOrEmpty(text))
							{
								fullText.Append(text);
								onChunk(text);
							}
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
		request.Headers.Add("x-api-key", apiKey);
		request.Headers.Add("anthropic-version", AnthropicVersion);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

		HttpResponseMessage? response = null;
		try
		{
			response = await _httpClient.SendAsync(request, cancellationToken);
		}
		catch (Exception)
		{
			// 如果网络或端点请求失败，将使用内置常用模型列表
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
						if (body?["data"] is JsonArray data && data.Count > 0)
						{
							SortedSet<string> models = new(StringComparer.Ordinal);
							foreach (JsonNode? item in data)
							{
								if (item?["id"] is JsonValue idVal && idVal.TryGetValue(out string? id) && !string.IsNullOrWhiteSpace(id))
								{
									models.Add(id);
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

		// 回退内置常用模型
		return
		[
			"claude-3-7-sonnet-20250219",
			"claude-3-5-sonnet-20241022",
			"claude-3-5-haiku-20241022",
			"claude-3-opus-20240229",
		];
	}

	/// <summary>
	/// 规范化 messages 列表以符合 Anthropic 要求 (交替角色，首条必须为 user)
	/// </summary>
	private static JsonArray NormalizeMessages(IReadOnlyList<ChatMessageInput> rawMessages)
	{
		List<ChatMessageInput> filtered = [];
		foreach (ChatMessageInput msg in rawMessages)
		{
			string role = msg.Role.ToLowerInvariant();
			if (role is "user" or "assistant")
			{
				filtered.Add(msg);
			}
		}

		// 移除开头的 assistant
		while (filtered.Count > 0 && filtered[0].Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
		{
			filtered.RemoveAt(0);
		}

		if (filtered.Count == 0) return [];

		// 合并连续相同角色的消息
		JsonArray result = [];
		string? currentRole = null;
		StringBuilder currentContent = new();

		foreach (ChatMessageInput item in filtered)
		{
			string role = item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
			if (role == currentRole)
			{
				currentContent.Append("\n\n").Append(item.Content);
			}
			else
			{
				if (currentRole is not null)
				{
					result.Add(new JsonObject
					{
						["role"] = currentRole,
						["content"] = currentContent.ToString(),
					});
				}
				currentRole = role;
				currentContent.Clear();
				currentContent.Append(item.Content);
			}
		}

		if (currentRole is not null)
		{
			result.Add(new JsonObject
			{
				["role"] = currentRole,
				["content"] = currentContent.ToString(),
			});
		}

		return result;
	}

	private static string FormatEndpoint(string baseUrl, string path)
	{
		baseUrl = baseUrl.Trim().TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");

		if (path == "messages")
		{
			if (baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)) return baseUrl;
			return $"{baseUrl}/messages";
		}

		if (path == "models")
		{
			if (baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^"/messages".Length].TrimEnd('/');
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
