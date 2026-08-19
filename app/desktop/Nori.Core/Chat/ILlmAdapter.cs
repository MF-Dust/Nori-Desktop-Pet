namespace Nori.Core.Chat;

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
	/// 获取支持的模型列表
	/// </summary>
	Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default);
}
