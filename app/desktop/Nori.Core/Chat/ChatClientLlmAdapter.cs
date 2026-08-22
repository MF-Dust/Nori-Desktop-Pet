using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;

namespace Nori.Core.Chat;

/// <summary>
/// 将官方 Microsoft.Extensions.AI IChatClient 适配到 Nori 的旧 ILlmAdapter 契约。
/// PromptBuilder、流式 JSON 解析与桥接协议因此无需改动。
/// </summary>
public sealed class ChatClientLlmAdapter(LlmProvider provider, HttpClient httpClient) : ILlmAdapter
{
	private readonly LlmProvider _provider = provider;
	private readonly HttpClient _httpClient = httpClient;

	public async Task<string> CompleteAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		CancellationToken cancellationToken = default)
	{
		using IChatClient client = ChatClientFactory.TryCreateChatClient(_provider, _httpClient, baseUrl, apiKey, model)
			?? throw new ChatException($"不支持的模型提供商: {_provider}");

		try
		{
			ChatResponse response = await client.GetResponseAsync(
				BuildMessages(systemPrompt, messages),
				new ChatOptions {ModelId = model},
				cancellationToken);
			if (string.IsNullOrEmpty(response.Text)) throw new ChatException("接口响应格式异常: 缺少回复文本");
			return response.Text;
		}
		catch (ChatException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new ChatException($"请求失败: {exception.Message}", exception);
		}
	}

	public async Task<string> StreamAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onChunk,
		Action<LlmUsageInfo>? onUsage = null,
		CancellationToken cancellationToken = default)
	{
		using IChatClient client = ChatClientFactory.TryCreateChatClient(_provider, _httpClient, baseUrl, apiKey, model)
			?? throw new ChatException($"不支持的模型提供商: {_provider}");

		System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
		System.Text.StringBuilder fullText = new();
		UsageDetails? usage = null;
		try
		{
			await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
				BuildMessages(systemPrompt, messages),
				new ChatOptions {ModelId = model},
				cancellationToken))
			{
				if (!string.IsNullOrEmpty(update.Text))
				{
					fullText.Append(update.Text);
					onChunk(update.Text);
				}
				foreach (AIContent content in update.Contents)
				{
					if (content is UsageContent usageContent) usage = usageContent.Details;
				}
			}
			stopwatch.Stop();
			UsageDetails actual = usage ?? new UsageDetails
			{
				InputTokenCount = Math.Max(1, EstimateTokens(systemPrompt, messages)),
				OutputTokenCount = Math.Max(1, (int)(fullText.Length / 3.2)),
			};
			onUsage?.Invoke(new LlmUsageInfo
			{
				PromptTokens = (int)Math.Min(int.MaxValue, actual.InputTokenCount ?? 0),
				CompletionTokens = (int)Math.Min(int.MaxValue, actual.OutputTokenCount ?? 0),
				TotalTokens = (int)Math.Min(int.MaxValue, actual.TotalTokenCount ?? (actual.InputTokenCount ?? 0) + (actual.OutputTokenCount ?? 0)),
				CachedTokens = (int)Math.Min(int.MaxValue, actual.CachedInputTokenCount ?? 0),
				DurationMs = stopwatch.ElapsedMilliseconds,
				Model = model,
			});
			return fullText.ToString();
		}
		catch (ChatException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			throw new ChatException($"请求失败: {exception.Message}", exception);
		}
	}

	public Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default) =>
		ProviderModelCatalog.FetchAsync(_provider, _httpClient, baseUrl, apiKey, cancellationToken);

	private static IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> BuildMessages(string systemPrompt, IReadOnlyList<ChatMessageInput> messages)
	{
		List<Microsoft.Extensions.AI.ChatMessage> result = [new(ChatRole.System, systemPrompt)];
		foreach (ChatMessageInput message in messages)
		{
			ChatRole role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
				? ChatRole.Assistant
				: ChatRole.User;
			result.Add(new Microsoft.Extensions.AI.ChatMessage(role, message.Content));
		}
		return result;
	}

	private static int EstimateTokens(string systemPrompt, IReadOnlyList<ChatMessageInput> messages) =>
		(int)((systemPrompt.Length + messages.Sum(message => message.Content.Length)) / 3.2);
}
