namespace Nori.Core.Embedding;

/// <summary>
/// 向量嵌入 (Embedding) 适配器接口
/// </summary>
public interface IEmbeddingAdapter
{
	/// <summary>
	/// 为单段文本生成向量嵌入 (支持 BGE-M3 / OpenAI / Ollama 等兼容接口)
	/// </summary>
	Task<float[]> GetEmbeddingAsync(
		string baseUrl,
		string apiKey,
		string model,
		string input,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// 批量生成向量嵌入
	/// </summary>
	Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<string> inputs,
		CancellationToken cancellationToken = default);
}
