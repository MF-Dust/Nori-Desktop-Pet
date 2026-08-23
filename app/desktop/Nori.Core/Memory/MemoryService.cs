using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Embedding;

namespace Nori.Core.Memory;

/// <summary>
/// 长期记忆 Facade。
/// 写入、检索和生命周期逻辑均通过 MemoryStore 聚合层完成，保留旧版公开 API 兼容性。
/// </summary>
public sealed class MemoryService
{
	public const int MaxCacheSize = 250;
	private readonly MemoryStore _store;
	private readonly IEmbeddingAdapter _embedding;
	private readonly ConfigStore _config;

	public MemoryService(MemoryStore store, IEmbeddingAdapter embedding, ConfigStore config)
	{
		_store = store;
		_embedding = embedding;
		_config = config;
	}

	public MemoryStore Store => _store;

	/// <summary>独立 ARG 知识服务，由宿主装配后回填。</summary>
	public KnowledgeService? Knowledge { get; set; }

	/// <summary>读取记忆运行设置。</summary>
	public MemorySettings Settings => new()
	{
		Enabled = _config.GetBoolOr("memory_enabled", true),
		ReflectionEnabled = _config.GetBoolOr("memory_reflection_enabled", true),
		ReflectionRounds = ReadInt("memory_reflection_rounds", 8, 1, 32),
		ReflectionMinChars = ReadInt("memory_reflection_min_chars", 2500, 100, 20000),
		RecallTopK = ReadInt("memory_recall_top_k", 6, 1, 20),
		KeywordTopK = ReadInt("memory_keyword_top_k", 20, 1, 100),
		VectorTopK = ReadInt("memory_vector_top_k", 20, 1, 100),
		RrfK = ReadInt("memory_rrf_k", 60, 1, 500),
		MinSimilarity = ReadDouble("memory_min_similarity", 0.25, 0, 1),
		DecayEnabled = _config.GetBoolOr("memory_decay_enabled", true),
		ArchiveEnabled = _config.GetBoolOr("memory_archive_enabled", true),
		SourceRetentionThreshold = ReadDouble("memory_source_retention_threshold", 0.75, 0, 1),
		ArchiveThreshold = ReadDouble("memory_archive_threshold", 0.15, 0, 1),
		KnowledgeEnabled = _config.GetBoolOr("memory_knowledge_enabled", true),
		KnowledgeWatch = _config.GetBoolOr("memory_knowledge_watch", true),
		DebugRetrieval = _config.GetBoolOr("memory_debug_retrieval", false),
	};

	/// <summary>解析 Embedding 接入配置。</summary>
	public (string BaseUrl, string ApiKey, string Model, int? Dimensions) ResolveConfig()
	{
		string baseUrl = _config.GetStringOr("embedding_api_base", "").Trim();
		if (baseUrl.Length == 0) baseUrl = _config.GetStringOr("llm_api_base", "https://api.openai.com/v1").Trim();
		if (baseUrl.Length == 0) baseUrl = "https://api.openai.com/v1";
		string apiKey = _config.GetStringOr("embedding_api_key", "");
		if (apiKey.Length == 0) apiKey = _config.GetStringOr("llm_api_key", "");
		string model = _config.GetStringOr("embedding_model", "BAAI/bge-m3");
		int? dimensions = int.TryParse(_config.GetStringOr("embedding_dimensions", ""), out int parsed) && parsed > 0 ? parsed : null;
		return (baseUrl, apiKey, model, dimensions);
	}

	/// <summary>当前 Embedding 配置指纹，不包含 API Key。</summary>
	public bool EmbeddingConfigured
	{
		get
		{
			string explicitBase = _config.GetStringOr("embedding_api_base", "").Trim();
			string key = _config.GetStringOr("embedding_api_key", "");
			if (explicitBase.Length > 0 || key.Length > 0) return true;
			return _config.GetStringOr("llm_api_key", "").Length > 0;
		}
	}

	public string ResolveEmbeddingFingerprint()
	{
		(string baseUrl, _, string model, int? dimensions) = ResolveConfig();
		string authority = baseUrl.TrimEnd('/');
		if (Uri.TryCreate(authority, UriKind.Absolute, out Uri? uri)) authority = uri.Authority.ToLowerInvariant();
		string input = $"openai-compatible\n{authority}\n{model.Trim()}\n{dimensions?.ToString() ?? ""}";
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
	}

	/// <summary>获取文本向量；任何远程失败均返回 null。</summary>
	public Task<float[]?> EmbedAsync(string text) => EmbedAsync(text, CancellationToken.None);

	public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
	{
		string trimmed = text.Trim();
		if (trimmed.Length == 0 || !EmbeddingConfigured) return null;
		try
		{
			(string baseUrl, string apiKey, string model, int? dimensions) = ResolveConfig();
			float[] vector = await _embedding.GetEmbeddingAsync(baseUrl, apiKey, model, trimmed, dimensions, cancellationToken).ConfigureAwait(false);
			return vector.Length == 0 ? null : vector;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>3 秒查询向量超时，确保聊天不会被 Embedding 服务拖住。</summary>
	private async Task<float[]?> EmbedForRecallAsync(string text, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(3));
		try { return await EmbedAsync(text, timeout.Token).ConfigureAwait(false); }
		catch (OperationCanceledException) { return null; }
	}

	public void ClearCache() => _embedding.ClearCache();

	/// <summary>添加长期记忆，同时创建一个可独立召回的事实原子。</summary>
	public async Task<MemoryItem> AddAsync(
		string content,
		string type = "general",
		double importance = 0.5,
		string? tags = null,
		string source = "chat",
		MemoryKind? kind = null,
		string? canonicalSummary = null,
		string? personaSummary = null,
		double confidence = 0.8,
		IReadOnlyList<MemorySource>? sources = null,
		string? embeddingText = null)
	{
		MemoryKind resolvedKind = kind ?? MemoryKindExtensions.Parse(type);
		double? ttl = DefaultTtl(resolvedKind);
		float[]? vector = await EmbedAsync(embeddingText ?? canonicalSummary ?? content).ConfigureAwait(false);
		string? embedding = vector is null ? null : JsonSerializer.Serialize(vector);
		MemoryItem item = _store.Add(type, content, importance, source, tags, embedding, resolvedKind,
			canonicalSummary, personaSummary, confidence, ttl, null, vector is null ? null : ResolveEmbeddingFingerprint());
		_store.AddAtom(item.Id, resolvedKind, canonicalSummary ?? content, importance, confidence, ttl);
		if (sources is not null) AddSources(item.Id, sources);
		return item;
	}

	/// <summary>用户或 Agent 明确要求记住时使用的入口，先做同类型精确去重和强化。</summary>
	public async Task<MemoryItem> RememberAsync(string content, MemoryKind kind = MemoryKind.Factual, double importance = 0.8, string? tags = null, string source = "agent")
	{
		string normalized = Normalize(content);
		foreach (RetrievalHit hit in _store.SearchKeyword(content, 5))
		{
			MemoryItem? existing = _store.Get(hit.MemoryId);
			if (existing is null || MemoryKindExtensions.Parse(existing.Kind) != kind) continue;
			if (Normalize(existing.CanonicalSummary ?? existing.Content) == normalized)
			{
				_store.Reinforce(existing.Id);
				return existing;
			}
		}
		return await AddAsync(content, kind.ToStorage(), importance, tags, source, kind).ConfigureAwait(false);
	}

	/// <summary>更新记忆内容，文本变化而向量失败时会使旧向量失效。</summary>
	public async Task<bool> UpdateAsync(long id, string content, double? importance = null, string? tags = null,
		MemoryKind? kind = null, string? canonicalSummary = null, string? personaSummary = null, double? confidence = null)
	{
		float[]? vector = await EmbedAsync(canonicalSummary ?? content).ConfigureAwait(false);
		return _store.Update(id, content, importance, tags, vector is null ? null : JsonSerializer.Serialize(vector), kind,
			canonicalSummary, personaSummary, confidence, embeddingFingerprint: vector is null ? null : ResolveEmbeddingFingerprint());
	}

	/// <summary>真正的关键词 + 向量混合检索。</summary>
	public async Task<IReadOnlyList<MemoryItem>> SearchHybridAsync(string keyword, int limit = 10, CancellationToken cancellationToken = default)
	{
		if (!Settings.Enabled) return [];
		float[]? vector = await EmbedForRecallAsync(keyword, cancellationToken).ConfigureAwait(false);
		return _store.SearchHybrid(keyword, vector, limit);
	}

	/// <summary>构建分层 MemoryContext，并只强化最终实际注入的记忆。</summary>
	public async Task<MemoryContext> BuildContextAsync(
		string userText,
		IReadOnlyList<(string Role, string Content)> recentMessages,
		CancellationToken cancellationToken = default,
		bool includeDebug = false)
	{
		MemorySettings settings = Settings;
		if (!settings.Enabled) return new MemoryContext();
		string expanded = MemoryQueryBuilder.Build(userText, recentMessages);
		float[]? vector = await EmbedForRecallAsync(expanded, cancellationToken).ConfigureAwait(false);
		IReadOnlyList<RetrievalHit> keywordHits = _store.SearchKeyword(userText, settings.KeywordTopK);
		IReadOnlyList<RetrievalHit> vectorHits = vector is null
			? []
			: _store.SearchSemantic(vector, settings.VectorTopK, settings.MinSimilarity)
			.Select((hit, index) => new RetrievalHit(hit.Item.Id, hit.Similarity, index + 1)).ToList();
		IReadOnlyList<RetrievalHit> atomHits = _store.SearchAtomKeyword(userText, 10);
		IReadOnlyList<RetrievalHit> fused = RrfFusion.Fuse([keywordHits, vectorHits], settings.RrfK);

		Dictionary<long, double> scores = fused.ToDictionary(hit => hit.MemoryId, hit => hit.Score);
		foreach (RetrievalHit atomHit in atomHits)
		{
			MemoryAtom? atom = _store.GetAtom(atomHit.MemoryId);
			if (atom is null) continue;
			scores[atom.ParentMemoryId] = scores.GetValueOrDefault(atom.ParentMemoryId) + atomHit.Score;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		List<(MemoryItem Item, double Score)> ranked = scores
			.Select(pair => (Item: _store.Get(pair.Key), Rrf: pair.Value))
			.Where(pair => pair.Item is not null)
			.Select(pair => (Item: pair.Item!, Score: DecayCalculator.FinalScore(pair.Rrf, pair.Item!, now)))
			.Where(pair => (pair.Item.Status is "active" or "dormant") && pair.Score > 0)
			.OrderByDescending(pair => pair.Score)
			.ToList();

		List<MemoryItem> personal = TakeWithinBudget(ranked, settings.RecallTopK, 900);
		long[] injectedIds = personal.Select(item => item.Id).ToArray();
		_store.MarkAccessed(injectedIds);
		List<MemoryAtom> atoms = personal.SelectMany(item => _store.GetAtoms(item.Id, MemoryStatus.Active, 3)).ToList();
		IReadOnlyList<RetrievedKnowledge> knowledge = TakeKnowledgeBudget(Knowledge?.Search(userText, 4) ?? [], 2200);
		IReadOnlyList<MemoryEcho> echoes = (Knowledge?.SearchEchoes(userText, 2) ?? [])
			.Select(echo => echo with {Content = echo.Content.Length > 320 ? echo.Content[..320] : echo.Content})
			.Take(2)
			.ToList();

		RecallDebugTrace? debug = settings.DebugRetrieval || includeDebug
			? new RecallDebugTrace
			{
				Query = userText,
				ExpandedQuery = expanded,
				KeywordHits = keywordHits,
				VectorHits = vectorHits,
				AtomHits = atomHits,
				RrfHits = fused,
				FilteredIds = ranked.Select(pair => pair.Item.Id).Except(injectedIds).ToArray(),
				InjectedIds = injectedIds,
			}
			: null;
		return new MemoryContext {Personal = personal, Atoms = atoms, Knowledge = knowledge, Echoes = echoes, Debug = debug};
	}

	/// <summary>旧接口只返回可注入的个人记忆文本。</summary>
	public async Task<IReadOnlyList<string>> GetRelevantMemoriesAsync(string prompt, int limit = 5)
	{
		try
		{
			MemoryContext context = await BuildContextAsync(prompt, []).ConfigureAwait(false);
			return context.Personal.Take(Math.Max(0, limit)).Select(item => item.PersonaSummary ?? item.Content).ToList();
		}
		catch
		{
			return [];
		}
	}

	/// <summary>批量为未嵌入记忆生成向量。</summary>
	public async Task<int> ReembedAllAsync(CancellationToken cancellationToken = default, bool force = true)
	{
		if (!EmbeddingConfigured) return 0;
		long afterId = 0;
		int count = 0;
		string fingerprint = ResolveEmbeddingFingerprint();
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<MemoryItem> page = _store.GetReembedCandidates(fingerprint, 100, afterId, force);
			if (page.Count == 0) break;
			foreach (MemoryItem item in page)
			{
				afterId = item.Id;
				float[]? vector = await EmbedAsync(item.CanonicalSummary ?? item.Content, cancellationToken).ConfigureAwait(false);
				if (vector is null) continue;
				if (_store.UpdateEmbedding(item.Id, JsonSerializer.Serialize(vector), fingerprint)) count++;
			}
		}
		return count;
	}

	public bool Archive(long id) => _store.Archive(id);
	public bool Restore(long id) => _store.Restore(id);
	public bool Delete(long id) => _store.Delete(id);
	public void Clear() => _store.Clear();
	public MemoryItem? Get(long id) => _store.Get(id);
	public IReadOnlyList<MemoryAtom> GetAtoms(long? parentId = null, MemoryStatus? status = null, int limit = 100, int offset = 0) => _store.GetAtoms(parentId, status, limit, offset);
	public IReadOnlyList<MemorySource> GetSources(long memoryId) => _store.GetSources(memoryId);
	public (int Active, int Atoms, int Archived, int Total) GetOverview() => _store.GetOverview();

	private void AddSources(long memoryId, IReadOnlyList<MemorySource> sources)
	{
		// Source 表的聚合写入由同一数据库连接锁保护；reflection 窗口通常只有几条消息。
		_store.AddSources(memoryId, sources);
	}

	private static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	private static double? DefaultTtl(MemoryKind kind)
	{
		double value = DecayCalculator.HalfLifeDays(kind);
		return double.IsPositiveInfinity(value) ? null : value;
	}

	private static IReadOnlyList<RetrievedKnowledge> TakeKnowledgeBudget(IReadOnlyList<RetrievedKnowledge> source, int budget)
	{
		List<RetrievedKnowledge> result = [];
		int used = 0;
		foreach (RetrievedKnowledge item in source)
		{
			int size = Math.Max(1, item.Content.Length / 2);
			if (result.Count > 0 && used + size > budget) continue;
			result.Add(item);
			used += size;
		}
		return result;
	}

	private static List<MemoryItem> TakeWithinBudget(IReadOnlyList<(MemoryItem Item, double Score)> ranked, int limit, int budget)
	{
		List<MemoryItem> result = [];
		int used = 0;
		foreach ((MemoryItem item, _) in ranked)
		{
			if (result.Count >= Math.Max(0, limit)) break;
			string text = item.PersonaSummary ?? item.CanonicalSummary ?? item.Content;
			int size = Math.Max(1, text.Length / 2);
			if (result.Count > 0 && used + size > budget) continue;
			result.Add(item);
			used += size;
		}
		return result;
	}

	private int ReadInt(string key, int fallback, int min, int max)
	{
		return int.TryParse(_config.GetStringOr(key, ""), out int value) ? Math.Clamp(value, min, max) : fallback;
	}

	private double ReadDouble(string key, double fallback, double min, double max)
	{
		return double.TryParse(_config.GetStringOr(key, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
			? Math.Clamp(value, min, max) : fallback;
	}
}
