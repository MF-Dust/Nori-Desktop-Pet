using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Chat;
using Nori.Core.Chat.Adapters;

namespace Nori.Core.Tests;

public class LlmProviderTests
{
	[Theory]
	[InlineData("openai", LlmProvider.OpenAi)]
	[InlineData("OPENAI", LlmProvider.OpenAi)]
	[InlineData("openai_responses", LlmProvider.OpenAiResponses)]
	[InlineData("responses", LlmProvider.OpenAiResponses)]
	[InlineData("anthropic", LlmProvider.Anthropic)]
	[InlineData("claude", LlmProvider.Anthropic)]
	[InlineData("google", LlmProvider.Google)]
	[InlineData("gemini", LlmProvider.Google)]
	[InlineData("google_genai", LlmProvider.Google)]
	[InlineData("", LlmProvider.OpenAi)]
	[InlineData(null, LlmProvider.OpenAi)]
	public void 解析协议类型(string? input, LlmProvider expected)
	{
		Assert.Equal(expected, LlmProviderExtensions.ParseProvider(input));
	}

	[Theory]
	[InlineData(LlmProvider.OpenAi, "https://api.openai.com/v1")]
	[InlineData(LlmProvider.OpenAiResponses, "https://api.openai.com/v1")]
	[InlineData(LlmProvider.Anthropic, "https://api.anthropic.com/v1")]
	[InlineData(LlmProvider.Google, "https://generativelanguage.googleapis.com/v1beta")]
	public void 默认BaseUrl正确(LlmProvider provider, string expected)
	{
		Assert.Equal(expected, provider.DefaultBaseUrl());
	}
}

public class LlmAdapterTests
{
	private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(handler(request));
		}
	}

	[Fact]
	public async Task OpenAiResponsesAdapter解析响应()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://api.openai.com/v1/responses", req.RequestUri?.ToString());
			Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
			Assert.Equal("test-key", req.Headers.Authorization?.Parameter);

			string responseJson = """
			{
				"id": "resp_123",
				"output": [
					{
						"type": "message",
						"content": [
							{
								"type": "text",
								"text": "你好！我是 Nori。"
							}
						]
					}
				]
			}
			""";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		OpenAiResponsesAdapter adapter = new(client);

		string result = await adapter.CompleteAsync(
			"https://api.openai.com/v1",
			"test-key",
			"gpt-4o",
			"你是一只桌面宠物",
			[new ChatMessageInput {Role = "user", Content = "你好"}]);

		Assert.Equal("你好！我是 Nori。", result);
	}

	[Fact]
	public async Task AnthropicAdapter发送消息与解析响应()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri?.ToString());
			Assert.True(req.Headers.Contains("x-api-key"));
			Assert.Equal("test-claude-key", req.Headers.GetValues("x-api-key").First());
			Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").First());

			string responseJson = """
			{
				"id": "msg_123",
				"type": "message",
				"role": "assistant",
				"content": [
					{
						"type": "text",
						"text": "你好呀主人！[nori_motion:smile]"
					}
				]
			}
			""";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		AnthropicAdapter adapter = new(client);

		string result = await adapter.CompleteAsync(
			"https://api.anthropic.com/v1",
			"test-claude-key",
			"claude-3-7-sonnet-20250219",
			"你是一只桌面宠物",
			[new ChatMessageInput {Role = "user", Content = "你好"}]);

		Assert.Equal("你好呀主人！[nori_motion:smile]", result);
	}

	[Fact]
	public async Task GoogleGenAiAdapter发送消息与解析响应()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", req.RequestUri?.ToString());
			Assert.True(req.Headers.Contains("x-goog-api-key"));
			Assert.Equal("test-gemini-key", req.Headers.GetValues("x-goog-api-key").First());

			string responseJson = """
			{
				"candidates": [
					{
						"content": {
							"parts": [
								{
									"text": "喵呜~ 收到！"
								}
							],
							"role": "model"
						},
						"finishReason": "STOP"
					}
				]
			}
			""";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		GoogleGenAiAdapter adapter = new(client);

		string result = await adapter.CompleteAsync(
			"https://generativelanguage.googleapis.com/v1beta",
			"test-gemini-key",
			"gemini-2.5-flash",
			"你是一只桌面宠物",
			[new ChatMessageInput {Role = "user", Content = "你好"}]);

		Assert.Equal("喵呜~ 收到！", result);
	}

	[Fact]
	public async Task GoogleGenAiAdapter拉取模型列表()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Get, req.Method);
			Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models", req.RequestUri?.ToString());

			string responseJson = """
			{
				"models": [
					{
						"name": "models/gemini-2.5-flash",
						"supportedGenerationMethods": ["generateContent", "countTokens"]
					},
					{
						"name": "models/embedding-001",
						"supportedGenerationMethods": ["embedContent"]
					},
					{
						"name": "models/gemini-2.5-pro",
						"supportedGenerationMethods": ["generateContent"]
					}
				]
			}
			""";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		GoogleGenAiAdapter adapter = new(client);

		IReadOnlyList<string> models = await adapter.FetchModelsAsync("https://generativelanguage.googleapis.com/v1beta", "key");

		Assert.Equal(2, models.Count);
		Assert.Contains("gemini-2.5-flash", models);
		Assert.Contains("gemini-2.5-pro", models);
		Assert.DoesNotContain("embedding-001", models);
	}
}
