namespace Nori.Core.Chat;

/// <summary>
/// LLM 对话 Token 用量与缓存命中统计信息
/// </summary>
public sealed record LlmUsageInfo
{
	/// <summary>提示词输入 Token 数</summary>
	public int PromptTokens { get; init; }

	/// <summary>回答生成 Token 数</summary>
	public int CompletionTokens { get; init; }

	/// <summary>总 Token 数</summary>
	public int TotalTokens { get; init; }

	/// <summary>命中缓存的 Prompt Token 数</summary>
	public int CachedTokens { get; init; }

	/// <summary>缓存命中率百分比 (0.0 ~ 100.0)</summary>
	public double CacheHitRate => PromptTokens > 0 ? Math.Round((double)CachedTokens / PromptTokens * 100.0, 1) : 0.0;

	/// <summary>生成耗时 (毫秒)</summary>
	public long DurationMs { get; init; }

	/// <summary>模型标识</summary>
	public string? Model { get; init; }
}

/// <summary>
/// LLM 协议适配器接口
/// </summary>
public interface ILlmAdapter
{
	/// <summary>
	/// 发起单次对话请求并返回原始文本
	/// </summary>
	Task<string> CompleteAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// 发起流式对话请求, 逐分片回调产出文本并回调用量指标
	/// </summary>
	Task<string> StreamAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onChunk,
		Action<LlmUsageInfo>? onUsage = null,
		CancellationToken cancellationToken = default);
}
