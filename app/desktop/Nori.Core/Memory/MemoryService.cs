using System.Text.Json;
using Nori.Core.Configuration;
using Nori.Core.Embedding;

namespace Nori.Core.Memory;

/// <summary>
/// 记忆服务
///
/// 在 MemoryStore 之上补齐前端原有职责:
/// - 写入/更新时自动计算向量嵌入 (缓存由 IEmbeddingGenerator 中间件负责)
/// - 混合语义检索与 Prompt 注入用相关记忆提取
/// - 全量向量重建循环
/// </summary>
public sealed class MemoryService(MemoryStore store, IEmbeddingAdapter embedding, ConfigStore config)
{
	/// <summary>旧版查询缓存容量常量，保留以兼容调用方；实际容量由适配器缓存控制。</summary>
	public const int MaxCacheSize = 250;

	/// <summary>
	/// 解析 Embedding 接入配置 (显式传入优先, 否则读配置; 缺省回退 llm 配置)
	/// </summary>
	public (string BaseUrl, string ApiKey, string Model, int? Dimensions) ResolveConfig()
	{
		string baseUrl = config.GetStringOr("embedding_api_base", "").Trim();
		if (baseUrl.Length == 0) baseUrl = config.GetStringOr("llm_api_base", "https://api.openai.com/v1").Trim();
		if (baseUrl.Length == 0) baseUrl = "https://api.openai.com/v1";

		string apiKey = config.GetStringOr("embedding_api_key", "");
		if (apiKey.Length == 0) apiKey = config.GetStringOr("llm_api_key", "");

		string model = config.GetStringOr("embedding_model", "BAAI/bge-m3");

		int? dimensions = null;
		string raw = config.GetStringOr("embedding_dimensions", "").Trim();
		if (int.TryParse(raw, out int parsed) && parsed > 0) dimensions = parsed;

		return (baseUrl, apiKey, model, dimensions);
	}

	/// <summary>获取文本的向量嵌入; 失败返回 null。</summary>
	public async Task<float[]?> EmbedAsync(string text)
	{
		string trimmed = text.Trim();
		if (trimmed.Length == 0) return null;

		try
		{
			(string baseUrl, string apiKey, string model, int? dimensions) = ResolveConfig();
			float[] vector = await embedding.GetEmbeddingAsync(baseUrl, apiKey, model, trimmed, dimensions);
			if (vector.Length == 0) return null;

			return vector;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>清空向量缓存</summary>
	public void ClearCache() => embedding.ClearCache();

	/// <summary>添加一条新记忆 (自动计算向量嵌入)</summary>
	public async Task<MemoryItem> AddAsync(string content, string type = "general", double importance = 0.5, string? tags = null, string source = "chat")
	{
		float[]? vector = await EmbedAsync(content);
		return store.Add(type, content, importance, source, tags, vector is null ? null : JsonSerializer.Serialize(vector));
	}

	/// <summary>更新记忆内容 (文本变化时自动重算向量, 失败时清空旧向量)</summary>
	public async Task<bool> UpdateAsync(long id, string content, double? importance = null, string? tags = null)
	{
		float[]? vector = await EmbedAsync(content);
		return store.Update(id, content, importance, tags, vector is null ? null : JsonSerializer.Serialize(vector));
	}

	/// <summary>混合语义检索 (向量相似度 + 关键词融合), 向量失败回退关键词</summary>
	public async Task<IReadOnlyList<MemoryItem>> SearchHybridAsync(string keyword, int limit = 10)
	{
		float[]? vector = await EmbedAsync(keyword);
		return store.SearchHybrid(keyword, vector, limit);
	}

	/// <summary>
	/// 提取并返回与当前用户输入最相关的记忆片段列表 (用于 Prompt 注入)
	/// </summary>
	public async Task<IReadOnlyList<string>> GetRelevantMemoriesAsync(string prompt, int limit = 5)
	{
		try
		{
			IReadOnlyList<MemoryItem> results = await SearchHybridAsync(prompt, limit);
			return results.Select(item => item.Content).ToList();
		}
		catch
		{
			return [];
		}
	}

	/// <summary>
	/// 重新为所有未嵌入向量的记忆生成 Embedding, 返回成功条数
	/// </summary>
	public async Task<int> ReembedAllAsync(CancellationToken cancellationToken = default)
	{
		long afterId = 0;
		int count = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<MemoryItem> page = store.GetUnembedded(100, afterId);
			if (page.Count == 0) break;

			foreach (MemoryItem item in page)
			{
				afterId = item.Id;
				float[]? vector = await EmbedAsync(item.Content);
				if (vector is not null)
				{
					store.UpdateEmbedding(item.Id, JsonSerializer.Serialize(vector));
					count++;
				}
			}
		}
		return count;
	}
}
