using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Embedding;

namespace Nori.Core.Memory;

/// <summary>
/// 长期记忆 Facade。
/// 写入、检索和生命周期逻辑均通过 MemoryStore 聚合层完成，保留旧版公开 API 兼容性。
/// </summary>
public sealed class MemoryService : IAsyncDisposable
{
	public const int MaxCacheSize = 250;
	private const int EmbeddingBatchSize = 32;
	private const string ReembedFingerprintState = "embedding_reembed_fingerprint";
	private const string ReembedCursorState = "embedding_reembed_cursor";
	private readonly MemoryStore _store;
	private readonly IEmbeddingAdapter _embedding;
	private readonly ConfigStore _config;
	private readonly AiSettingsStore _aiSettings;
	private readonly SemaphoreSlim _reembedGate = new(1, 1);
	private readonly Channel<EmbeddingJob> _embeddingQueue = Channel.CreateBounded<EmbeddingJob>(new BoundedChannelOptions(128)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true,
		SingleWriter = false,
	});
	private readonly CancellationTokenSource _embeddingCts = new();
	private readonly Task _embeddingWorker;
	private int _disposed;

	public MemoryService(
		MemoryStore store,
		IEmbeddingAdapter embedding,
		ConfigStore config,
		bool startBackgroundWorker = true)
	{
		_store = store;
		_embedding = embedding;
		_config = config;
		_aiSettings = new AiSettingsStore(config);
		_embeddingWorker = startBackgroundWorker
			? Task.Run(ProcessEmbeddingQueueAsync)
			: Task.CompletedTask;
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

	/// <summary>解析独立 Embedding 接入配置, 不从聊天配置回退。</summary>
	public (string BaseUrl, string ApiKey, string Model, int? Dimensions) ResolveConfig()
	{
		AiEmbeddingSettings embedding = _aiSettings.Read().Embedding;
		return (embedding.BaseUrl, embedding.ApiKey, embedding.Model, embedding.Dimensions);
	}

	/// <summary>当前 Embedding 配置指纹，不包含 API Key。</summary>
	public bool EmbeddingConfigured => _aiSettings.Read().Embedding.IsConfigured;

	public string ResolveEmbeddingFingerprint()
	{
		(string baseUrl, _, string model, int? dimensions) = ResolveConfig();
		string authority = baseUrl.TrimEnd('/');
		if (Uri.TryCreate(authority, UriKind.Absolute, out Uri? uri))
		{
			authority = $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority.ToLowerInvariant()}{uri.AbsolutePath.TrimEnd('/')}";
		}
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

	/// <summary>添加长期记忆，先保证文本与 Atom 落库，再排队生成向量。</summary>
	public Task<MemoryItem> AddAsync(
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
		if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("记忆内容不能为空", nameof(content));
		MemoryKind resolvedKind = kind ?? MemoryKindExtensions.Parse(type);
		double? ttl = DefaultTtl(resolvedKind);
		MemoryItem item = _store.AddAggregate(type, content, importance, source, tags, null, resolvedKind,
			canonicalSummary, personaSummary, confidence, ttl, null, null, sources);
		QueueEmbedding(item.Id, embeddingText ?? canonicalSummary ?? content);
		return Task.FromResult(item);
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

	/// <summary>更新文本时先清除旧向量，再把新向量放入后台队列。</summary>
	public Task<bool> UpdateAsync(long id, string content, double? importance = null, string? tags = null,
		MemoryKind? kind = null, string? canonicalSummary = null, string? personaSummary = null, double? confidence = null)
	{
		if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("记忆内容不能为空", nameof(content));
		MemoryItem? before = _store.Get(id);
		if (before is null) return Task.FromResult(false);
		MemoryKind resolvedKind = kind ?? MemoryKindExtensions.Parse(before.Kind);
		bool updated = _store.Update(id, content, importance, tags, "", kind,
			canonicalSummary, personaSummary, confidence, embeddingFingerprint: null);
		if (updated)
		{
			string oldSummary = before.CanonicalSummary ?? before.Content;
			MemoryAtom? defaultAtom = _store.GetAtoms(id, null, 100, 0).FirstOrDefault(atom => atom.Content == oldSummary);
			if (defaultAtom is not null)
			{
				_store.UpdateAtom(defaultAtom.Id, canonicalSummary ?? content, resolvedKind, importance, confidence);
			}
			QueueEmbedding(id, canonicalSummary ?? content);
		}
		return Task.FromResult(updated);
	}

	/// <summary>真正的关键词 + 向量混合检索。</summary>
	public async Task<IReadOnlyList<MemoryItem>> SearchHybridAsync(string keyword, int limit = 10, CancellationToken cancellationToken = default)
	{
		if (!Settings.Enabled) return [];
		MemoryContext context = await BuildContextAsync(keyword, [], cancellationToken, false, false, Math.Clamp(limit, 1, 100)).ConfigureAwait(false);
		return context.Personal;
	}

	/// <summary>构建分层 MemoryContext，并只强化最终实际注入的记忆。</summary>
	public async Task<MemoryContext> BuildContextAsync(
		string userText,
		IReadOnlyList<(string Role, string Content)> recentMessages,
		CancellationToken cancellationToken = default,
		bool includeDebug = false,
		bool markAccess = true,
		int? personalLimitOverride = null)
	{
		MemorySettings settings = Settings;
		if (!settings.Enabled) return new MemoryContext();
		string expanded = MemoryQueryBuilder.Build(userText, recentMessages);
		float[]? vector = await EmbedForRecallAsync(expanded, cancellationToken).ConfigureAwait(false);
		IReadOnlyList<RetrievalHit> keywordHits = _store.SearchKeyword(expanded, settings.KeywordTopK);
		IReadOnlyList<RetrievalHit> vectorHits = vector is null
			? []
			: _store.SearchSemantic(vector, settings.VectorTopK, settings.MinSimilarity)
			.Select((hit, index) => new RetrievalHit(hit.Item.Id, hit.Similarity, index + 1)).ToList();
		IReadOnlyList<RetrievalHit> atomHits = _store.SearchAtomKeyword(expanded, 10);
		IReadOnlyList<RetrievalHit> atomParentHits = atomHits
			.Select((hit, index) => (Atom: _store.GetAtom(hit.MemoryId), Rank: index + 1))
			.Where(pair => pair.Atom is not null)
			.Select(pair => new RetrievalHit(pair.Atom!.ParentMemoryId, 1.0 / pair.Rank, pair.Rank))
			.ToList();
		IReadOnlyList<RetrievalHit> fused = RrfFusion.Fuse([keywordHits, vectorHits, atomParentHits], settings.RrfK);
		Dictionary<long, double> scores = fused.ToDictionary(hit => hit.MemoryId, hit => hit.Score);

		DateTimeOffset now = DateTimeOffset.UtcNow;
		List<(MemoryItem Item, double Score)> ranked = scores
			.Select(pair => (Item: _store.Get(pair.Key), Rrf: pair.Value))
			.Where(pair => pair.Item is not null)
			.Select(pair => (Item: pair.Item!, Score: DecayCalculator.FinalScore(pair.Rrf, pair.Item!, now, settings.DecayEnabled)))
			.Where(pair => (pair.Item.Status is "active" or "dormant") && pair.Score > 0)
			.OrderByDescending(pair => pair.Score)
			.ToList();

		List<MemoryItem> personal = TakeWithinBudget(ranked, personalLimitOverride ?? settings.RecallTopK, 900);
		long[] injectedIds = personal.Select(item => item.Id).ToArray();
		if (markAccess) _store.MarkAccessed(injectedIds);
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

	/// <summary>按 fingerprint 批量重建向量，进度持久化后可取消并从游标恢复。</summary>
	public async Task<int> ReembedAllAsync(CancellationToken cancellationToken = default, bool force = true)
	{
		if (!EmbeddingConfigured) return 0;
		await _reembedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			string fingerprint = ResolveEmbeddingFingerprint();
			string? savedFingerprint = _store.GetEngineState(ReembedFingerprintState);
			long afterId = savedFingerprint == fingerprint && long.TryParse(_store.GetEngineState(ReembedCursorState), out long savedCursor)
				? Math.Max(0, savedCursor)
				: 0;
			_store.SetEngineState(ReembedFingerprintState, fingerprint);
			int count = 0;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				IReadOnlyList<MemoryEmbeddingWorkItem> page = _store.GetReembedWork(fingerprint, EmbeddingBatchSize, afterId, force);
				if (page.Count == 0) break;
				IReadOnlyList<float[]> vectors;
				try
				{
					(string baseUrl, string apiKey, string model, int? dimensions) = ResolveConfig();
					vectors = await _embedding.GetEmbeddingsAsync(baseUrl, apiKey, model,
						page.Select(item => item.Text).ToArray(), dimensions, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch
				{
					// 单批失败不阻塞文本；推进游标并在下一次运行从头重试失败项。
					SaveReembedCursor(page[^1].Id);
					afterId = page[^1].Id;
					continue;
				}

				if (vectors.Count == page.Count)
			{
					List<MemoryEmbeddingUpdate> updates = [];
					for (int index = 0; index < page.Count; index++)
					{
						float[] vector = vectors[index];
						if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value))) continue;
						updates.Add(new MemoryEmbeddingUpdate {Id = page[index].Id, UpdatedAt = page[index].UpdatedAt, Vector = vector});
					}
					count += _store.UpdateEmbeddings(updates, fingerprint);
				}
				afterId = page[^1].Id;
				SaveReembedCursor(afterId);
			}
			SaveReembedCursor(0);
			return count;
		}
		finally
		{
			_reembedGate.Release();
		}
	}

	public bool Archive(long id) => _store.Archive(id);
	public bool Restore(long id) => _store.Restore(id);
	public bool Delete(long id) => _store.Delete(id);
	public void Clear() => _store.Clear();
	public MemoryItem? Get(long id) => _store.Get(id);
	public IReadOnlyList<MemoryAtom> GetAtoms(long? parentId = null, MemoryStatus? status = null, int limit = 100, int offset = 0) => _store.GetAtoms(parentId, status, limit, offset);
	public IReadOnlyList<MemorySource> GetSources(long memoryId) => _store.GetSources(memoryId);
	public (int Active, int Atoms, int Archived, int Total) GetOverview() => _store.GetOverview();

	private void QueueEmbedding(long id, string text)
	{
		if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(text)) return;
		_embeddingQueue.Writer.TryWrite(new EmbeddingJob(id, text));
	}

	private async Task ProcessEmbeddingQueueAsync()
	{
		try
		{
			await foreach (EmbeddingJob first in _embeddingQueue.Reader.ReadAllAsync(_embeddingCts.Token).ConfigureAwait(false))
			{
				List<EmbeddingJob> batch = [first];
				while (batch.Count < EmbeddingBatchSize && _embeddingQueue.Reader.TryRead(out EmbeddingJob? next) && next is not null) batch.Add(next);
				try { await EmbedAndStoreBatchAsync(batch, _embeddingCts.Token).ConfigureAwait(false); }
				catch (OperationCanceledException) when (_embeddingCts.IsCancellationRequested) { return; }
				catch { }
			}
		}
		catch (OperationCanceledException) when (_embeddingCts.IsCancellationRequested) { }
	}

	private async Task EmbedAndStoreBatchAsync(IReadOnlyList<EmbeddingJob> batch, CancellationToken cancellationToken)
	{
		if (!EmbeddingConfigured || batch.Count == 0) return;
		(string baseUrl, string apiKey, string model, int? dimensions) = ResolveConfig();
		IReadOnlyList<float[]> vectors = await _embedding.GetEmbeddingsAsync(baseUrl, apiKey, model,
			batch.Select(job => job.Text).ToArray(), dimensions, cancellationToken).ConfigureAwait(false);
		if (vectors.Count != batch.Count) return;
		string fingerprint = ResolveEmbeddingFingerprint();
		List<MemoryEmbeddingUpdate> updates = [];
		for (int index = 0; index < batch.Count; index++)
		{
			float[] vector = vectors[index];
			if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value))) continue;
			MemoryItem? item = _store.Get(batch[index].Id);
			if (item is null) continue;
			updates.Add(new MemoryEmbeddingUpdate {Id = item.Id, UpdatedAt = item.UpdatedAt, Vector = vector});
		}
		_store.UpdateEmbeddings(updates, fingerprint);
	}

	private void SaveReembedCursor(long cursor) => _store.SetEngineState(ReembedCursorState, cursor.ToString(System.Globalization.CultureInfo.InvariantCulture));

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_embeddingQueue.Writer.TryComplete();
		_embeddingCts.Cancel();
		try { await _embeddingWorker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
		catch (OperationCanceledException) { }
		catch (TimeoutException) { }
		_embeddingCts.Dispose();
		_reembedGate.Dispose();
	}

	private sealed record EmbeddingJob(long Id, string Text);

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
