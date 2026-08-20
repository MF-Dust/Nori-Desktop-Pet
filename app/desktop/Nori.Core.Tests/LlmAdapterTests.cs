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

	[Fact]
	public async Task OpenAiChatAdapter流式分片读取()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri?.ToString());

			string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"，我是 Nori\"}}]}\n\ndata: [DONE]\n\n";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		OpenAiChatAdapter adapter = new(client);

		List<string> chunks = [];
		string full = await adapter.StreamAsync(
			"https://api.openai.com/v1",
			"key",
			"gpt-4o",
			"系统提示",
			[new ChatMessageInput {Role = "user", Content = "hi"}],
			chunk => chunks.Add(chunk));

		Assert.Equal("你好，我是 Nori", full);
		Assert.Equal(["你好", "，我是 Nori"], chunks);
	}

	[Fact]
	public async Task AnthropicAdapter流式分片读取()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri?.ToString());

			string sse = "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"你好呀\"}}\n\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"！\"}}\n\n";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		AnthropicAdapter adapter = new(client);

		List<string> chunks = [];
		string full = await adapter.StreamAsync(
			"https://api.anthropic.com/v1",
			"key",
			"claude-3-5-sonnet-20241022",
			"系统提示",
			[new ChatMessageInput {Role = "user", Content = "hi"}],
			chunk => chunks.Add(chunk));

		Assert.Equal("你好呀！", full);
		Assert.Equal(["你好呀", "！"], chunks);
	}

	[Fact]
	public async Task GoogleGenAiAdapter流式分片读取()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Contains("streamGenerateContent?alt=sse", req.RequestUri?.ToString());

			string sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"喵呜\"}]}}]}\n\ndata: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"~\"}]}}]}\n\n";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		GoogleGenAiAdapter adapter = new(client);

		List<string> chunks = [];
		string full = await adapter.StreamAsync(
			"https://generativelanguage.googleapis.com/v1beta",
			"key",
			"gemini-2.5-flash",
			"系统提示",
			[new ChatMessageInput {Role = "user", Content = "hi"}],
			chunk => chunks.Add(chunk));

		Assert.Equal("喵呜~", full);
		Assert.Equal(["喵呜", "~"], chunks);
	}

	[Fact]
	public async Task OpenAiChatAdapter流式分片包含空choices与usage分片时不崩溃()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\n:ping\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":20}}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"，我是 Nori\"}}]}\n\ndata: [DONE]\n\n";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		OpenAiChatAdapter adapter = new(client);

		List<string> chunks = [];
		string full = await adapter.StreamAsync(
			"https://api.openai.com/v1",
			"key",
			"gpt-4o",
			"系统提示",
			[new ChatMessageInput {Role = "user", Content = "hi"}],
			chunk => chunks.Add(chunk));

		Assert.Equal("你好，我是 Nori", full);
		Assert.Equal(["你好", "，我是 Nori"], chunks);
	}

	[Fact]
	public async Task OpenAiResponsesAdapter流式分片包含空choices时不崩溃()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			string sse = "data: {\"choices\":[],\"usage\":{\"total_tokens\":15}}\n\ndata: {\"delta\":\"收到啦\"}\n\ndata: {\"choices\":[]}\n\ndata: {\"delta\":\"主人\"}\n\ndata: [DONE]\n\n";

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		OpenAiResponsesAdapter adapter = new(client);

		List<string> chunks = [];
		string full = await adapter.StreamAsync(
			"https://api.openai.com/v1",
			"key",
			"gpt-4o",
			"系统提示",
			[new ChatMessageInput {Role = "user", Content = "hi"}],
			chunk => chunks.Add(chunk));

		Assert.Equal("收到啦主人", full);
		Assert.Equal(["收到啦", "主人"], chunks);
	}
}
