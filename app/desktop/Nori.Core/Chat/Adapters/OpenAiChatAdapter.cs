using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Chat.Adapters;

/// <summary>
/// OpenAI Chat Completions 协议适配器 (/chat/completions)
/// </summary>
public sealed class OpenAiChatAdapter(HttpClient httpClient) : ILlmAdapter
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
		string endpoint = FormatEndpoint(baseUrl, "chat/completions");

		JsonArray payloadMessages = [new JsonObject {["role"] = "system", ["content"] = systemPrompt}];
		foreach (ChatMessageInput message in messages)
		{
			payloadMessages.Add(new JsonObject {["role"] = message.Role, ["content"] = message.Content});
		}

		JsonObject payload = new()
		{
			["model"] = model,
			["messages"] = payloadMessages,
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

			string? content = body?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
			if (content is null) throw new ChatException("接口响应格式异常: 缺少 choices[0].message.content");

			return content;
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

		if (path == "chat/completions")
		{
			if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return baseUrl;
			return $"{baseUrl}/chat/completions";
		}

		if (path == "models")
		{
			if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^"/chat/completions".Length].TrimEnd('/');
			}
			else if (baseUrl.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^"/responses".Length].TrimEnd('/');
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
