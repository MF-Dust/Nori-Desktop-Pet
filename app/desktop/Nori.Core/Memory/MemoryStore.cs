using System.Buffers;
using System.Globalization;
using System.Numerics.Tensors;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;
using Nori.Core.Embedding;

namespace Nori.Core.Memory;

/// <summary>记忆数据模型。</summary>
public sealed record MemoryItem
{
	public required long Id { get; init; }
	public required string Type { get; init; }
	public required string Content { get; init; }
	public required double Importance { get; init; }
	public required string Source { get; init; }
	public string? Tags { get; init; }
	/// <summary>旧版 JSON 向量兼容字段，新写入优先使用 EmbeddingBlob。</summary>
	public string? Embedding { get; init; }
	/// <summary>Float32 little-endian 向量存储。</summary>
	public byte[]? EmbeddingBlob { get; init; }
	public required string CreatedAt { get; init; }
	public required string UpdatedAt { get; init; }
	public string Kind { get; init; } = "general";
	public string? CanonicalSummary { get; init; }
	public string? PersonaSummary { get; init; }
	public double Confidence { get; init; } = 0.8;
	public string Status { get; init; } = "active";
	public int AccessCount { get; init; }
	public int ReinforcementCount { get; init; }
	public string? LastAccessedAt { get; init; }
	public string? LastReinforcedAt { get; init; }
	public double? TtlDays { get; init; }
	public string? ExpiresAt { get; init; }
	public long? SupersededBy { get; init; }
	public string? EmbeddingFingerprint { get; init; }

	/// <summary>解析 BLOB 或旧 JSON 向量数组。</summary>
	public float[]? GetVector()
	{
		if (!string.IsNullOrWhiteSpace(Embedding) && EmbeddingVectorCodec.TryDecodeJson(Embedding, out float[] legacy)) return legacy;
		return EmbeddingBlob is {Length: > 0} && EmbeddingVectorCodec.TryDecode(EmbeddingBlob, out float[] vector) ? vector : null;
	}
}

/// <summary>语义检索匹配结果。</summary>
public sealed record MemorySearchResult
{
	public required MemoryItem Item { get; init; }
	public required double Similarity { get; init; }
	public required double Score { get; init; }
}

/// <summary>
/// SQLite 记忆存储兼容层。
/// 旧的 MemoryStore API 保留给桥接和插件；所有新增聚合写入在这里统一维护 Atom、Source 与 FTS。
/// </summary>
public sealed class MemoryStore
{
	public const int DefaultSemanticCandidateLimit = 100000;
	public const int DefaultVectorCacheCapacity = 512;

	private readonly NoriDatabase _database;
	private readonly int _semanticCandidateLimit;
	private readonly int _vectorCacheCapacity;
	private readonly Lock _vectorCacheGate = new();
	private readonly Dictionary<long, (string UpdatedAt, float[] Vector)> _vectorCache = [];
	private bool _ftsAvailable;

	public MemoryStore(
		NoriDatabase database,
		int semanticCandidateLimit = DefaultSemanticCandidateLimit,
		int vectorCacheCapacity = DefaultVectorCacheCapacity)
	{
		if (semanticCandidateLimit <= 0) throw new ArgumentOutOfRangeException(nameof(semanticCandidateLimit));
		if (vectorCacheCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(vectorCacheCapacity));
		_database = database;
		_semanticCandidateLimit = semanticCandidateLimit;
		_vectorCacheCapacity = vectorCacheCapacity;
		InitializeFts();
	}

	/// <summary>当前 SQLite 是否提供可用的 FTS5 索引。</summary>
	public bool IsFtsAvailable => _ftsAvailable;

	private float[]? VectorOf(MemoryItem item)
	{
		if (string.IsNullOrWhiteSpace(item.Embedding) && item.EmbeddingBlob is not {Length: > 0}) return null;
		lock (_vectorCacheGate)
		{
			if (_vectorCache.Remove(item.Id, out (string UpdatedAt, float[] Vector) cached))
			{
				if (cached.UpdatedAt == item.UpdatedAt)
				{
					_vectorCache[item.Id] = cached;
					return cached.Vector;
				}
			}

			float[]? vector = item.GetVector();
			if (vector is null) return null;
			if (_vectorCache.Count >= _vectorCacheCapacity)
			{
				long oldest = _vectorCache.Keys.First();
				_vectorCache.Remove(oldest);
			}
			_vectorCache[item.Id] = (item.UpdatedAt, vector);
			return vector;
		}
	}

	private void EvictVector(long id)
	{
		lock (_vectorCacheGate) _vectorCache.Remove(id);
	}

	/// <summary>添加一条记忆，并初始化 v4 聚合字段。</summary>
	public MemoryItem Add(
		string type,
		string content,
		double importance = 0.5,
		string source = "chat",
		string? tags = null,
		string? embedding = null,
		MemoryKind kind = MemoryKind.General,
		string? canonicalSummary = null,
		string? personaSummary = null,
		double confidence = 0.8,
		double? ttlDays = null,
		string? expiresAt = null,
		string? embeddingFingerprint = null)
	{
		ValidateScore(importance, nameof(importance));
		ValidateScore(confidence, nameof(confidence));
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		string storageKind = kind == MemoryKind.General ? MemoryKindExtensions.Parse(type).ToStorage() : kind.ToStorage();
		(string? Legacy, byte[]? Blob) embeddingStorage = PrepareEmbedding(embedding);
		long id = _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = """
				INSERT INTO memories
					(type, content, importance, source, tags, embedding, embedding_blob, created_at, updated_at,
					 kind, canonical_summary, persona_summary, confidence, status, ttl_days, expires_at, embedding_fingerprint)
				VALUES ($type, $content, $importance, $source, $tags, $embedding, $embedding_blob, $created_at, $updated_at,
					 $kind, $canonical_summary, $persona_summary, $confidence, 'active', $ttl_days, $expires_at, $embedding_fingerprint);
				SELECT last_insert_rowid();
				""";
			AddParameter(command, "$type", type);
			AddParameter(command, "$content", content);
			AddParameter(command, "$importance", importance);
			AddParameter(command, "$source", source);
			AddParameter(command, "$tags", tags);
			AddParameter(command, "$embedding", embeddingStorage.Legacy);
			AddParameter(command, "$embedding_blob", embeddingStorage.Blob);
			AddParameter(command, "$created_at", now);
			AddParameter(command, "$updated_at", now);
			AddParameter(command, "$kind", storageKind);
			AddParameter(command, "$canonical_summary", canonicalSummary ?? content);
			AddParameter(command, "$persona_summary", personaSummary ?? content);
			AddParameter(command, "$confidence", confidence);
			AddParameter(command, "$ttl_days", ttlDays);
			AddParameter(command, "$expires_at", expiresAt);
			AddParameter(command, "$embedding_fingerprint", embeddingFingerprint);
			long result = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
			RefreshMemoryIndex(connection, transaction, result);
			transaction.Commit();
			return result;
		});

		return new MemoryItem
		{
			Id = id,
			Type = type,
			Content = content,
			Importance = importance,
			Source = source,
			Tags = tags,
			Embedding = embedding,
			EmbeddingBlob = embeddingStorage.Blob,
			CreatedAt = now,
			UpdatedAt = now,
			Kind = storageKind,
			CanonicalSummary = canonicalSummary ?? content,
			PersonaSummary = personaSummary ?? content,
			Confidence = confidence,
			Status = "active",
			TtlDays = ttlDays,
			ExpiresAt = expiresAt,
			EmbeddingFingerprint = embeddingFingerprint,
		};
	}

	/// <summary>以一个事务写入 Memory、默认 Atom 和 Source 聚合。</summary>
	public MemoryItem AddAggregate(
		string type,
		string content,
		double importance = 0.5,
		string source = "chat",
		string? tags = null,
		string? embedding = null,
		MemoryKind kind = MemoryKind.General,
		string? canonicalSummary = null,
		string? personaSummary = null,
		double confidence = 0.8,
		double? ttlDays = null,
		string? expiresAt = null,
		string? embeddingFingerprint = null,
		IReadOnlyList<MemorySource>? sources = null)
	{
		ValidateScore(importance, nameof(importance));
		ValidateScore(confidence, nameof(confidence));
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		string storageKind = kind == MemoryKind.General ? MemoryKindExtensions.Parse(type).ToStorage() : kind.ToStorage();
		(string? Legacy, byte[]? Blob) embeddingStorage = PrepareEmbedding(embedding);
		long id = _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand memory = connection.CreateCommand();
			memory.Transaction = transaction;
			memory.CommandText = """
				INSERT INTO memories
					(type, content, importance, source, tags, embedding, embedding_blob, created_at, updated_at,
					 kind, canonical_summary, persona_summary, confidence, status, ttl_days, expires_at, embedding_fingerprint)
				VALUES ($type, $content, $importance, $source, $tags, $embedding, $embedding_blob, $created_at, $updated_at,
					 $kind, $canonical_summary, $persona_summary, $confidence, 'active', $ttl_days, $expires_at, $embedding_fingerprint);
				SELECT last_insert_rowid();
				""";
			AddParameter(memory, "$type", type);
			AddParameter(memory, "$content", content);
			AddParameter(memory, "$importance", importance);
			AddParameter(memory, "$source", source);
			AddParameter(memory, "$tags", tags);
			AddParameter(memory, "$embedding", embeddingStorage.Legacy);
			AddParameter(memory, "$embedding_blob", embeddingStorage.Blob);
			AddParameter(memory, "$created_at", now);
			AddParameter(memory, "$updated_at", now);
			AddParameter(memory, "$kind", storageKind);
			AddParameter(memory, "$canonical_summary", canonicalSummary ?? content);
			AddParameter(memory, "$persona_summary", personaSummary ?? content);
			AddParameter(memory, "$confidence", confidence);
			AddParameter(memory, "$ttl_days", ttlDays);
			AddParameter(memory, "$expires_at", expiresAt);
			AddParameter(memory, "$embedding_fingerprint", embeddingFingerprint);
			long memoryId = Convert.ToInt64(memory.ExecuteScalar(), CultureInfo.InvariantCulture);

			using SqliteCommand atom = connection.CreateCommand();
			atom.Transaction = transaction;
			atom.CommandText = """
				INSERT INTO memory_atoms
					(parent_memory_id, atom_type, content, importance, confidence, status, created_at, ttl_days, expires_at)
				VALUES ($parent, $kind, $content, $importance, $confidence, 'active', $created, $ttl, $expires);
				""";
			AddParameter(atom, "$parent", memoryId);
			AddParameter(atom, "$kind", storageKind);
			AddParameter(atom, "$content", canonicalSummary ?? content);
			AddParameter(atom, "$importance", importance);
			AddParameter(atom, "$confidence", confidence);
			AddParameter(atom, "$created", now);
			AddParameter(atom, "$ttl", ttlDays);
			AddParameter(atom, "$expires", expiresAt);
			atom.ExecuteNonQuery();
			using SqliteCommand atomIdCommand = connection.CreateCommand();
			atomIdCommand.Transaction = transaction;
			atomIdCommand.CommandText = "SELECT last_insert_rowid();";
			long atomId = Convert.ToInt64(atomIdCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

			if (sources is not null)
			{
				foreach (MemorySource sourceRow in sources)
				{
					using SqliteCommand sourceCommand = connection.CreateCommand();
					sourceCommand.Transaction = transaction;
					sourceCommand.CommandText = "INSERT INTO memory_sources(memory_id, role, content, message_time, sequence) VALUES ($memory, $role, $content, $time, $sequence)";
					AddParameter(sourceCommand, "$memory", memoryId);
					AddParameter(sourceCommand, "$role", sourceRow.Role);
					AddParameter(sourceCommand, "$content", sourceRow.Content);
					AddParameter(sourceCommand, "$time", sourceRow.MessageTime);
					AddParameter(sourceCommand, "$sequence", sourceRow.Sequence);
					sourceCommand.ExecuteNonQuery();
				}
			}
			RefreshMemoryIndex(connection, transaction, memoryId);
			RefreshAtomIndex(connection, transaction, atomId);
			transaction.Commit();
			return memoryId;
		});

		return new MemoryItem
		{
			Id = id, Type = type, Content = content, Importance = importance, Source = source, Tags = tags,
			Embedding = embedding, EmbeddingBlob = embeddingStorage.Blob, CreatedAt = now, UpdatedAt = now, Kind = storageKind,
			CanonicalSummary = canonicalSummary ?? content, PersonaSummary = personaSummary ?? content,
			Confidence = confidence, Status = "active", TtlDays = ttlDays, ExpiresAt = expiresAt,
			EmbeddingFingerprint = embeddingFingerprint,
		};
	}

	/// <summary>直接创建一个事实原子。</summary>
	public MemoryAtom AddAtom(
		long parentMemoryId,
		MemoryKind kind,
		string content,
		double importance = 0.5,
		double confidence = 0.8,
		double? ttlDays = null,
		string? expiresAt = null,
		string? entities = null)
	{
		ValidateScore(importance, nameof(importance));
		ValidateScore(confidence, nameof(confidence));
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		return _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = """
				INSERT INTO memory_atoms
					(parent_memory_id, atom_type, content, importance, confidence, status, created_at, ttl_days, expires_at, entities)
				VALUES ($parent, $kind, $content, $importance, $confidence, 'active', $created, $ttl, $expires, $entities);
				SELECT last_insert_rowid();
				""";
			AddParameter(command, "$parent", parentMemoryId);
			AddParameter(command, "$kind", kind.ToStorage());
			AddParameter(command, "$content", content);
			AddParameter(command, "$importance", importance);
			AddParameter(command, "$confidence", confidence);
			AddParameter(command, "$created", now);
			AddParameter(command, "$ttl", ttlDays);
			AddParameter(command, "$expires", expiresAt);
			AddParameter(command, "$entities", entities);
			long id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
			RefreshAtomIndex(connection, transaction, id);
			transaction.Commit();
			return new MemoryAtom
			{
				Id = id,
				ParentMemoryId = parentMemoryId,
				AtomType = kind.ToStorage(),
				Content = content,
				Importance = importance,
				Confidence = confidence,
				Status = MemoryStatus.Active,
				CreatedAt = now,
				TtlDays = ttlDays,
				ExpiresAt = expiresAt,
				Entities = entities,
			};
		});
	}

	/// <summary>同步默认 Atom，避免编辑父记忆后旧摘要继续参与召回。</summary>
	public bool UpdateAtom(long atomId, string content, MemoryKind kind, double? importance = null, double? confidence = null)
	{
		return _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE memory_atoms SET content = $content, atom_type = $kind, importance = COALESCE($importance, importance), confidence = COALESCE($confidence, confidence) WHERE id = $id";
			AddParameter(command, "$id", atomId);
			AddParameter(command, "$content", content);
			AddParameter(command, "$kind", kind.ToStorage());
			AddParameter(command, "$importance", importance);
			AddParameter(command, "$confidence", confidence);
			bool updated = command.ExecuteNonQuery() > 0;
			if (updated) RefreshAtomIndex(connection, transaction, atomId);
			transaction.Commit();
			return updated;
		});
	}

	/// <summary>更新记忆的向量嵌入和 fingerprint。</summary>
	public bool UpdateEmbedding(long id, string embedding, string? fingerprint = null)
	{
		(string? Legacy, byte[]? Blob) storage = PrepareEmbedding(embedding);
		return _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "UPDATE memories SET embedding = $embedding, embedding_blob = $embedding_blob, embedding_fingerprint = COALESCE($fingerprint, embedding_fingerprint) WHERE id = $id";
			AddParameter(command, "$id", id);
			AddParameter(command, "$embedding", storage.Legacy);
			AddParameter(command, "$embedding_blob", storage.Blob);
			AddParameter(command, "$fingerprint", fingerprint);
			return command.ExecuteNonQuery() > 0;
		});
	}

	/// <summary>在一个事务中批量写回向量，并用 updated_at 防止覆盖新内容。</summary>
	public int UpdateEmbeddings(IReadOnlyList<MemoryEmbeddingUpdate> updates, string fingerprint)
	{
		if (updates.Count == 0) return 0;
		return _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			int count = 0;
			foreach (MemoryEmbeddingUpdate update in updates)
			{
				if (update.Vector.Length == 0 || update.Vector.Any(value => !float.IsFinite(value))) continue;
				using SqliteCommand command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = "UPDATE memories SET embedding = NULL, embedding_blob = $embedding_blob, embedding_fingerprint = $fingerprint WHERE id = $id AND updated_at = $updated_at AND status IN ('active', 'dormant')";
				AddParameter(command, "$id", update.Id);
				AddParameter(command, "$updated_at", update.UpdatedAt);
				AddParameter(command, "$embedding_blob", EmbeddingVectorCodec.Encode(update.Vector));
				AddParameter(command, "$fingerprint", fingerprint);
				count += command.ExecuteNonQuery();
			}
			transaction.Commit();
			return count;
		});
	}

	/// <summary>按重要度和时间读取记忆；兼容旧设置页的全量接口。</summary>
	public IReadOnlyList<MemoryItem> GetAll(int limit = 100) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = BaseSelect + " ORDER BY importance DESC, id DESC LIMIT $limit";
		AddParameter(command, "$limit", Math.Max(0, limit));
		return ReadItems(command);
	});

	/// <summary>按游标读取待嵌入记忆。</summary>
	public IReadOnlyList<MemoryItem> GetUnembedded(int limit = 100, long afterId = 0) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = BaseSelect + " WHERE id > $afterId AND embedding_blob IS NULL AND (embedding IS NULL OR embedding = '') AND status IN ('active', 'dormant') ORDER BY id ASC LIMIT $limit";
		AddParameter(command, "$afterId", afterId);
		AddParameter(command, "$limit", Math.Max(1, limit));
		return ReadItems(command);
	});

	/// <summary>读取需要向量重建的记忆，支持 fingerprint 变化和强制重建。</summary>
	public IReadOnlyList<MemoryItem> GetReembedCandidates(string fingerprint, int limit = 100, long afterId = 0, bool force = false) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = BaseSelect + " WHERE id > $afterId AND status IN ('active', 'dormant') AND ($force = 1 OR (embedding_blob IS NULL AND (embedding IS NULL OR embedding = '')) OR embedding_fingerprint IS NULL OR embedding_fingerprint <> $fingerprint) ORDER BY id ASC LIMIT $limit";
		AddParameter(command, "$afterId", afterId);
		AddParameter(command, "$force", force ? 1 : 0);
		AddParameter(command, "$fingerprint", fingerprint);
		AddParameter(command, "$limit", Math.Max(1, limit));
		return ReadItems(command);
	});

	/// <summary>读取批量重嵌入所需的最小字段，避免先物化完整 MemoryItem。</summary>
	public IReadOnlyList<MemoryEmbeddingWorkItem> GetReembedWork(string fingerprint, int limit = 32, long afterId = 0, bool force = false) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, updated_at, COALESCE(canonical_summary, content) FROM memories WHERE id > $afterId AND status IN ('active', 'dormant') AND ($force = 1 OR (embedding_blob IS NULL AND (embedding IS NULL OR embedding = '')) OR embedding_fingerprint IS NULL OR embedding_fingerprint <> $fingerprint) ORDER BY id ASC LIMIT $limit";
		AddParameter(command, "$afterId", afterId);
		AddParameter(command, "$force", force ? 1 : 0);
		AddParameter(command, "$fingerprint", fingerprint);
		AddParameter(command, "$limit", Math.Max(1, limit));
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryEmbeddingWorkItem> result = [];
		while (reader.Read())
		{
			result.Add(new MemoryEmbeddingWorkItem
			{
				Id = reader.GetInt64(0),
				UpdatedAt = reader.GetString(1),
				Text = reader.GetString(2),
			});
		}
		return (IReadOnlyList<MemoryEmbeddingWorkItem>)result;
	});

	/// <summary>按 id 获取记忆。</summary>
	public MemoryItem? Get(long id) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = BaseSelect + " WHERE id = $id";
		AddParameter(command, "$id", id);
		using SqliteDataReader reader = command.ExecuteReader();
		return reader.Read() ? ReadRow(reader) : null;
	});

	/// <summary>按关键词搜索；FTS 不可用时降级到 LIKE。</summary>
	public IReadOnlyList<MemoryItem> Search(string keyword, int limit = 20) =>
		SearchKeyword(keyword, limit).Select(hit => Get(hit.MemoryId)).OfType<MemoryItem>().ToList();

	/// <summary>返回关键词检索的排序命中。</summary>
	public IReadOnlyList<RetrievalHit> SearchKeyword(string keyword, int limit = 20)
	{
		if (string.IsNullOrWhiteSpace(keyword)) return [];
		return _database.Locked(connection =>
		{
			if (!_ftsAvailable) return SearchLikeHits(connection, keyword, limit);
			List<RetrievalHit> hits = SearchFts(connection, "memories_fts", keyword, limit);
			return hits.Count > 0 ? hits : SearchLikeHits(connection, keyword, limit);
		});
	}

	/// <summary>返回 Atom 的关键词检索命中。</summary>
	public IReadOnlyList<RetrievalHit> SearchAtomKeyword(string keyword, int limit = 10)
	{
		if (string.IsNullOrWhiteSpace(keyword)) return [];
		return _database.Locked(connection =>
		{
			if (!_ftsAvailable)
			{
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = """
					SELECT id FROM memory_atoms
					WHERE status IN ('active', 'dormant') AND content LIKE $pattern
					ORDER BY importance DESC, id DESC LIMIT $limit
					""";
				AddParameter(command, "$pattern", $"%{keyword}%");
				AddParameter(command, "$limit", Math.Max(0, limit));
				using SqliteDataReader reader = command.ExecuteReader();
				return ReadHits(reader);
			}
			List<RetrievalHit> hits = SearchFts(connection, "memory_atoms_fts", keyword, limit);
			if (hits.Count > 0) return hits;
			using SqliteCommand fallback = connection.CreateCommand();
			fallback.CommandText = "SELECT id FROM memory_atoms WHERE status IN ('active', 'dormant') AND content LIKE $pattern ORDER BY importance DESC, id DESC LIMIT $limit";
			AddParameter(fallback, "$pattern", $"%{keyword}%");
			AddParameter(fallback, "$limit", Math.Max(0, limit));
			using SqliteDataReader fallbackReader = fallback.ExecuteReader();
			return ReadHits(fallbackReader);
		});
	}

	/// <summary>向量语义检索，只扫描轻量向量列，最后才物化 top-K 记忆。</summary>
	public IReadOnlyList<MemorySearchResult> SearchSemantic(
		float[] queryVector,
		int limit = 10,
		double minSimilarity = 0.25)
	{
		int take = Math.Max(0, limit);
		if (take == 0 || queryVector.Length == 0) return [];
		float[] queryBuffer = ArrayPool<float>.Shared.Rent(queryVector.Length);
		float[] vectorBuffer = ArrayPool<float>.Shared.Rent(queryVector.Length);
		queryVector.AsSpan().CopyTo(queryBuffer);
		List<(long Id, byte[] Blob)> legacyMigrations = [];
		PriorityQueue<SemanticScore, (double Score, long Id)> heap = new();
		try
		{
			_database.Locked(connection =>
			{
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, updated_at, embedding_blob, embedding FROM memories WHERE status IN ('active', 'dormant') AND (embedding_blob IS NOT NULL OR (embedding IS NOT NULL AND embedding <> '')) ORDER BY importance DESC, id DESC LIMIT $limit";
				AddParameter(command, "$limit", _semanticCandidateLimit);
				using SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					long id = reader.GetInt64(0);
					string? legacy = reader.IsDBNull(3) ? null : reader.GetString(3);
					int vectorLength = 0;
					bool decoded = !string.IsNullOrWhiteSpace(legacy)
						? EmbeddingVectorCodec.TryDecodeJson(legacy, vectorBuffer, out vectorLength)
						: !reader.IsDBNull(2) && EmbeddingVectorCodec.TryDecode(reader.GetFieldValue<byte[]>(2), vectorBuffer, out vectorLength);
					if (!decoded || vectorLength != queryVector.Length) continue;
					double similarity = CosineSimilarity(queryBuffer.AsSpan(0, queryVector.Length), vectorBuffer.AsSpan(0, vectorLength));
					if (!double.IsFinite(similarity) || similarity < minSimilarity) continue;
					if (legacy is not null) legacyMigrations.Add((id, EmbeddingVectorCodec.Encode(vectorBuffer.AsSpan(0, vectorLength))));
					SemanticScore candidate = new(id, similarity);
					heap.Enqueue(candidate, (similarity, id));
					if (heap.Count > take) heap.Dequeue();
				}
			});
		}
		finally
		{
			ArrayPool<float>.Shared.Return(vectorBuffer);
			ArrayPool<float>.Shared.Return(queryBuffer);
		}

		if (legacyMigrations.Count > 0) MigrateLegacyEmbeddings(legacyMigrations);
		List<SemanticScore> ranked = heap.UnorderedItems.Select(item => item.Element)
			.OrderByDescending(item => item.Score)
			.ThenByDescending(item => item.Id)
			.ToList();
		Dictionary<long, MemoryItem> items = GetMany(ranked.Select(item => item.Id).ToArray());
		return ranked
			.Where(item => items.ContainsKey(item.Id))
			.Select(item => new MemorySearchResult {Item = items[item.Id], Similarity = item.Score, Score = item.Score})
			.ToList();
	}

	/// <summary>真正的关键词 + 向量 RRF 兼容搜索。</summary>
	public IReadOnlyList<MemoryItem> SearchHybrid(string keyword, float[]? queryVector = null, int limit = 10)
	{
		List<RetrievalHit> keywordHits = [.. SearchKeyword(keyword, Math.Max(limit * 2, 20))];
		List<RetrievalHit> vectorHits = queryVector is {Length: > 0}
			? SearchSemantic(queryVector, Math.Max(limit * 2, 20), 0).Select((hit, index) => new RetrievalHit(hit.Item.Id, hit.Similarity, index + 1)).ToList()
			: [];
		Dictionary<long, double> fused = FuseRrf([keywordHits, vectorHits], 60);
		return fused.OrderByDescending(pair => pair.Value).Take(Math.Max(0, limit))
			.Select(pair => Get(pair.Key)).OfType<MemoryItem>().ToList();
	}

	/// <summary>读取单个 Atom。</summary>
	public MemoryAtom? GetAtom(long id) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, parent_memory_id, atom_type, content, importance, confidence, status, created_at, last_accessed_at, last_reinforced_at, ttl_days, expires_at, reinforcement_count, decay_type, entities, superseded_by FROM memory_atoms WHERE id = $id";
		AddParameter(command, "$id", id);
		using SqliteDataReader reader = command.ExecuteReader();
		return reader.Read() ? ReadAtom(reader) : null;
	});

	/// <summary>读取 Atom。</summary>
	public IReadOnlyList<MemoryAtom> GetAtoms(long? parentMemoryId = null, MemoryStatus? status = null, int limit = 100, int offset = 0) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		List<string> where = [];
		if (parentMemoryId is not null) { where.Add("parent_memory_id = $parent"); AddParameter(command, "$parent", parentMemoryId.Value); }
		if (status is not null) { where.Add("status = $status"); AddParameter(command, "$status", status.Value.ToStorage()); }
		command.CommandText = "SELECT id, parent_memory_id, atom_type, content, importance, confidence, status, created_at, last_accessed_at, last_reinforced_at, ttl_days, expires_at, reinforcement_count, decay_type, entities, superseded_by FROM memory_atoms"
			+ (where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where))
			+ " ORDER BY importance DESC, id DESC LIMIT $limit OFFSET $offset";
		AddParameter(command, "$limit", Math.Max(0, limit));
		AddParameter(command, "$offset", Math.Max(0, offset));
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryAtom> result = [];
		while (reader.Read()) result.Add(ReadAtom(reader));
		return (IReadOnlyList<MemoryAtom>)result;
	});

	/// <summary>批量保存来源消息。</summary>
	public void AddSources(long memoryId, IReadOnlyList<MemorySource> sources)
	{
		if (sources.Count == 0) return;
		_database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			foreach (MemorySource source in sources)
			{
				using SqliteCommand command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = "INSERT INTO memory_sources(memory_id, role, content, message_time, sequence) VALUES ($memory, $role, $content, $time, $sequence)";
				AddParameter(command, "$memory", memoryId);
				AddParameter(command, "$role", source.Role);
				AddParameter(command, "$content", source.Content);
				AddParameter(command, "$time", source.MessageTime);
				AddParameter(command, "$sequence", source.Sequence);
				command.ExecuteNonQuery();
			}
			transaction.Commit();
		});
	}

	/// <summary>读取某条记忆的来源。</summary>
	public IReadOnlyList<MemorySource> GetSources(long memoryId) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, memory_id, role, content, message_time, sequence FROM memory_sources WHERE memory_id = $id ORDER BY sequence ASC";
		AddParameter(command, "$id", memoryId);
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemorySource> result = [];
		while (reader.Read())
		{
			result.Add(new MemorySource
			{
				Id = reader.GetInt64(0), MemoryId = reader.GetInt64(1), Role = reader.GetString(2),
				Content = reader.GetString(3), MessageTime = reader.IsDBNull(4) ? null : reader.GetString(4), Sequence = reader.GetInt32(5),
			});
		}
		return (IReadOnlyList<MemorySource>)result;
	});

	/// <summary>更新记忆内容与 v4 元数据。</summary>
	public bool Update(
		long id,
		string content,
		double? importance = null,
		string? tags = null,
		string? embedding = null,
		MemoryKind? kind = null,
		string? canonicalSummary = null,
		string? personaSummary = null,
		double? confidence = null,
		double? ttlDays = null,
		string? expiresAt = null,
		string? embeddingFingerprint = null)
	{
		if (importance is not null) ValidateScore(importance.Value, nameof(importance));
		if (confidence is not null) ValidateScore(confidence.Value, nameof(confidence));
		(string? Legacy, byte[]? Blob) embeddingStorage = embedding is null ? (null, null) : PrepareEmbedding(embedding);
		bool updated = _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = """
				UPDATE memories SET
					content = $content,
					importance = COALESCE($importance, importance),
					tags = COALESCE($tags, tags),
					kind = COALESCE($kind, kind),
					canonical_summary = COALESCE($canonical, canonical_summary, $content),
					persona_summary = COALESCE($persona, persona_summary, $content),
					confidence = COALESCE($confidence, confidence),
					ttl_days = COALESCE($ttl, ttl_days),
					expires_at = COALESCE($expires, expires_at),
					embedding = CASE WHEN $embeddingProvided = 1 THEN $embedding WHEN content <> $content THEN NULL ELSE embedding END,
					embedding_blob = CASE WHEN $embeddingProvided = 1 THEN $embedding_blob WHEN content <> $content THEN NULL ELSE embedding_blob END,
					embedding_fingerprint = CASE WHEN $embeddingProvided = 1 THEN $embeddingFingerprint WHEN content <> $content THEN NULL ELSE embedding_fingerprint END,
					updated_at = $updated
				WHERE id = $id
				""";
			AddParameter(command, "$id", id);
			AddParameter(command, "$content", content);
			AddParameter(command, "$importance", importance);
			AddParameter(command, "$tags", tags);
			AddParameter(command, "$kind", kind?.ToStorage());
			AddParameter(command, "$canonical", canonicalSummary);
			AddParameter(command, "$persona", personaSummary);
			AddParameter(command, "$confidence", confidence);
			AddParameter(command, "$ttl", ttlDays);
			AddParameter(command, "$expires", expiresAt);
			AddParameter(command, "$embedding", embeddingStorage.Legacy);
			AddParameter(command, "$embedding_blob", embeddingStorage.Blob);
			AddParameter(command, "$embeddingProvided", embedding is null ? 0 : 1);
			AddParameter(command, "$embeddingFingerprint", embeddingFingerprint);
			AddParameter(command, "$updated", now);
			int count = command.ExecuteNonQuery();
			if (count > 0) RefreshMemoryIndex(connection, transaction, id);
			transaction.Commit();
			return count > 0;
		});
		if (updated) EvictVector(id);
		return updated;
	}

	/// <summary>更新记忆状态，状态改变同步 FTS。</summary>
	public bool SetStatus(long id, MemoryStatus status, long? supersededBy = null)
	{
		bool updated = _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE memories SET status = $status, superseded_by = $superseded WHERE id = $id";
			AddParameter(command, "$id", id);
			AddParameter(command, "$status", status.ToStorage());
			AddParameter(command, "$superseded", supersededBy);
			int count = command.ExecuteNonQuery();
			if (count > 0)
			{
				using SqliteCommand atoms = connection.CreateCommand();
				atoms.Transaction = transaction;
				atoms.CommandText = "UPDATE memory_atoms SET status = $status WHERE parent_memory_id = $id AND status <> 'superseded'";
				AddParameter(atoms, "$id", id);
				AddParameter(atoms, "$status", status.ToStorage());
				atoms.ExecuteNonQuery();
				RefreshMemoryIndex(connection, transaction, id);
				RebuildAtomIndex(connection, transaction);
			}
			transaction.Commit();
			return count > 0;
		});
		return updated;
	}

	/// <summary>更新单个 Atom 状态并同步 Atom FTS。</summary>
	public bool SetAtomStatus(long atomId, MemoryStatus status, long? supersededBy = null)
	{
		return _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE memory_atoms SET status = $status, superseded_by = $superseded WHERE id = $id";
			AddParameter(command, "$id", atomId);
			AddParameter(command, "$status", status.ToStorage());
			AddParameter(command, "$superseded", supersededBy);
			bool changed = command.ExecuteNonQuery() > 0;
			if (changed) RefreshAtomIndex(connection, transaction, atomId);
			transaction.Commit();
			return changed;
		});
	}

	/// <summary>归档或恢复记忆；Superseded 不允许普通恢复。</summary>
	public bool Archive(long id) => SetStatus(id, MemoryStatus.Archived);

	public bool Restore(long id) => _database.Locked(connection =>
	{
		using SqliteTransaction transaction = connection.BeginTransaction();
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "UPDATE memories SET status = 'active', expires_at = NULL, last_accessed_at = $now WHERE id = $id AND status IN ('archived', 'expired')";
		AddParameter(command, "$id", id);
		AddParameter(command, "$now", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
		int count = command.ExecuteNonQuery();
		if (count > 0)
		{
			using SqliteCommand atoms = connection.CreateCommand();
			atoms.Transaction = transaction;
			atoms.CommandText = "UPDATE memory_atoms SET status = 'active' WHERE parent_memory_id = $id AND status IN ('archived', 'expired')";
			AddParameter(atoms, "$id", id);
			atoms.ExecuteNonQuery();
			RefreshMemoryIndex(connection, transaction, id);
			RebuildAtomIndex(connection, transaction);
		}
		transaction.Commit();
		return count > 0;
	});

	/// <summary>硬删除仅由明确的管理界面调用；外键自动清理 Atom/Source。</summary>
	public bool Delete(long id)
	{
		bool deleted = _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			if (_ftsAvailable)
			{
				using SqliteCommand atomDelete = connection.CreateCommand();
				atomDelete.Transaction = transaction;
				atomDelete.CommandText = "DELETE FROM memory_atoms_fts WHERE memory_id IN (SELECT id FROM memory_atoms WHERE parent_memory_id = $id)";
				AddParameter(atomDelete, "$id", id);
				atomDelete.ExecuteNonQuery();
			}
			command.CommandText = "DELETE FROM memories WHERE id = $id";
			AddParameter(command, "$id", id);
			bool result = command.ExecuteNonQuery() > 0;
			if (_ftsAvailable)
			{
				DeleteFtsRow(connection, transaction, "memories_fts", id);
			}
			transaction.Commit();
			return result;
		});
		if (deleted) EvictVector(id);
		return deleted;
	}

	/// <summary>清空个人记忆，不触碰 Knowledge 表。</summary>
	public void Clear()
	{
		_database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "DELETE FROM memories";
			command.ExecuteNonQuery();
			if (_ftsAvailable) command.CommandText = "DELETE FROM memories_fts; DELETE FROM memory_atoms_fts;";
			if (_ftsAvailable) command.ExecuteNonQuery();
			transaction.Commit();
		});
		lock (_vectorCacheGate) _vectorCache.Clear();
	}

	/// <summary>只强化最终注入 Prompt 的记忆。</summary>
	public void MarkAccessed(IEnumerable<long> ids)
	{
		long[] unique = ids.Distinct().ToArray();
		if (unique.Length == 0) return;
		_database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			foreach (long id in unique)
			{
				using SqliteCommand command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = "UPDATE memories SET access_count = access_count + 1, last_accessed_at = $now, status = CASE WHEN status = 'dormant' THEN 'active' ELSE status END WHERE id = $id";
				AddParameter(command, "$id", id);
				AddParameter(command, "$now", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
				command.ExecuteNonQuery();
				using SqliteCommand atoms = connection.CreateCommand();
				atoms.Transaction = transaction;
				atoms.CommandText = "UPDATE memory_atoms SET status = 'active', last_accessed_at = $now WHERE parent_memory_id = $id AND status = 'dormant'";
				AddParameter(atoms, "$id", id);
				AddParameter(atoms, "$now", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
				atoms.ExecuteNonQuery();
			}
			transaction.Commit();
		});
	}

	/// <summary>强化一条重复记忆。</summary>
	public bool Reinforce(long id, double importanceIncrement = 0.02)
	{
		return _database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
			using SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = "UPDATE memories SET reinforcement_count = reinforcement_count + 1, importance = MIN(1.0, importance + $increment), last_reinforced_at = $now, status = 'active' WHERE id = $id AND status <> 'superseded'";
			AddParameter(command, "$id", id);
			AddParameter(command, "$increment", Math.Max(0, importanceIncrement));
			AddParameter(command, "$now", now);
			bool updated = command.ExecuteNonQuery() > 0;
			if (updated)
			{
				using SqliteCommand atoms = connection.CreateCommand();
				atoms.Transaction = transaction;
				atoms.CommandText = "UPDATE memory_atoms SET status = CASE WHEN status = 'dormant' THEN 'active' ELSE status END, last_reinforced_at = $now WHERE parent_memory_id = $id AND status <> 'superseded'";
				AddParameter(atoms, "$id", id);
				AddParameter(atoms, "$now", now);
				atoms.ExecuteNonQuery();
			}
			transaction.Commit();
			return updated;
		});
	}

	/// <summary>读取后台引擎状态值。</summary>
	public string? GetEngineState(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT value FROM memory_engine_state WHERE key = $key";
		AddParameter(command, "$key", key);
		return command.ExecuteScalar() as string;
	});

	/// <summary>写入后台引擎状态值。</summary>
	public void SetEngineState(string key, string value) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "INSERT INTO memory_engine_state(key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
		AddParameter(command, "$key", key);
		AddParameter(command, "$value", value);
		command.ExecuteNonQuery();
	});

	/// <summary>统计个人记忆。</summary>
	public (int Active, int Atoms, int Archived, int Total) GetOverview()
	{
		return _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT (SELECT COUNT(*) FROM memories WHERE status = 'active'), (SELECT COUNT(*) FROM memory_atoms WHERE status IN ('active', 'dormant')), (SELECT COUNT(*) FROM memories WHERE status = 'archived'), (SELECT COUNT(*) FROM memories)";
			using SqliteDataReader reader = command.ExecuteReader();
			return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)) : (0, 0, 0, 0);
		});
	}

	/// <summary>计算两个向量的余弦相似度。</summary>
	public static double CosineSimilarity(float[] a, float[] b) => CosineSimilarity(a.AsSpan(), b.AsSpan());

	/// <summary>RRF 融合多个独立排名。</summary>
	public static Dictionary<long, double> FuseRrf(IReadOnlyList<IReadOnlyList<RetrievalHit>> rankings, int k = 60)
	{
		Dictionary<long, double> result = [];
		int safeK = Math.Max(1, k);
		foreach (IReadOnlyList<RetrievalHit> ranking in rankings)
		{
			for (int index = 0; index < ranking.Count; index++)
			{
				long id = ranking[index].MemoryId;
				result[id] = result.GetValueOrDefault(id) + (1.0 / (safeK + index + 1));
			}
		}
		return result;
	}

	private const string BaseSelect = "SELECT id, type, content, importance, source, tags, embedding, embedding_blob, created_at, updated_at, kind, canonical_summary, persona_summary, confidence, status, access_count, reinforcement_count, last_accessed_at, last_reinforced_at, ttl_days, expires_at, superseded_by, embedding_fingerprint FROM memories";

	private static List<MemoryItem> ReadItems(SqliteCommand command)
	{
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryItem> result = [];
		while (reader.Read()) result.Add(ReadRow(reader));
		return result;
	}

	private static MemoryItem ReadRow(SqliteDataReader reader)
	{
		string? legacy = reader.IsDBNull(6) ? null : reader.GetString(6);
		byte[]? blob = reader.IsDBNull(7) ? null : reader.GetFieldValue<byte[]>(7);
		string? compatibilityEmbedding = legacy;
		if (compatibilityEmbedding is null && blob is not null && EmbeddingVectorCodec.TryDecode(blob, out float[] vector))
		{
			compatibilityEmbedding = EmbeddingVectorCodec.ToJson(vector);
		}
		return new MemoryItem
		{
			Id = reader.GetInt64(0),
			Type = reader.GetString(1),
			Content = reader.GetString(2),
			Importance = reader.GetDouble(3),
			Source = reader.GetString(4),
			Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
			Embedding = compatibilityEmbedding,
			EmbeddingBlob = blob,
			CreatedAt = reader.GetString(8),
			UpdatedAt = reader.GetString(9),
			Kind = reader.IsDBNull(10) ? "general" : reader.GetString(10),
			CanonicalSummary = reader.IsDBNull(11) ? null : reader.GetString(11),
			PersonaSummary = reader.IsDBNull(12) ? null : reader.GetString(12),
			Confidence = reader.IsDBNull(13) ? 0.8 : reader.GetDouble(13),
			Status = reader.IsDBNull(14) ? "active" : reader.GetString(14),
			AccessCount = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
			ReinforcementCount = reader.IsDBNull(16) ? 0 : reader.GetInt32(16),
			LastAccessedAt = reader.IsDBNull(17) ? null : reader.GetString(17),
			LastReinforcedAt = reader.IsDBNull(18) ? null : reader.GetString(18),
			TtlDays = reader.IsDBNull(19) ? null : reader.GetDouble(19),
			ExpiresAt = reader.IsDBNull(20) ? null : reader.GetString(20),
			SupersededBy = reader.IsDBNull(21) ? null : reader.GetInt64(21),
			EmbeddingFingerprint = reader.IsDBNull(22) ? null : reader.GetString(22),
		};
	}

	private static MemoryAtom ReadAtom(SqliteDataReader reader) => new()
	{
		Id = reader.GetInt64(0), ParentMemoryId = reader.GetInt64(1), AtomType = reader.GetString(2), Content = reader.GetString(3),
		Importance = reader.GetDouble(4), Confidence = reader.GetDouble(5), Status = MemoryStatusExtensions.Parse(reader.GetString(6)), CreatedAt = reader.GetString(7),
		LastAccessedAt = reader.IsDBNull(8) ? null : reader.GetString(8), LastReinforcedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
		TtlDays = reader.IsDBNull(10) ? null : reader.GetDouble(10), ExpiresAt = reader.IsDBNull(11) ? null : reader.GetString(11),
		ReinforcementCount = reader.GetInt32(12), DecayType = reader.GetString(13), Entities = reader.IsDBNull(14) ? null : reader.GetString(14),
		SupersededBy = reader.IsDBNull(15) ? null : reader.GetInt64(15),
	};

	private void MigrateLegacyEmbeddings(IReadOnlyList<(long Id, byte[] Blob)> migrations)
	{
		_database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			foreach ((long id, byte[] blob) in migrations)
			{
				using SqliteCommand command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = "UPDATE memories SET embedding = NULL, embedding_blob = $embedding_blob WHERE id = $id AND embedding IS NOT NULL";
				AddParameter(command, "$id", id);
				AddParameter(command, "$embedding_blob", blob);
				command.ExecuteNonQuery();
			}
			transaction.Commit();
		});
	}

	private Dictionary<long, MemoryItem> GetMany(IReadOnlyList<long> ids)
	{
		if (ids.Count == 0) return [];
		return _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			string[] parameters = new string[ids.Count];
			for (int index = 0; index < ids.Count; index++)
			{
				parameters[index] = $"$id{index}";
				AddParameter(command, parameters[index], ids[index]);
			}
			command.CommandText = $"{BaseSelect} WHERE id IN ({string.Join(", ", parameters)})";
			using SqliteDataReader reader = command.ExecuteReader();
			Dictionary<long, MemoryItem> result = [];
			while (reader.Read())
			{
				MemoryItem item = ReadRow(reader);
				result[item.Id] = item;
			}
			return result;
		});
	}

	private static (string? Legacy, byte[]? Blob) PrepareEmbedding(string? embedding)
	{
		if (string.IsNullOrWhiteSpace(embedding)) return (null, null);
		return EmbeddingVectorCodec.TryDecodeJson(embedding, out float[] vector)
			? (null, EmbeddingVectorCodec.Encode(vector))
			: (embedding, null);
	}

	private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
	{
		if (a.Length != b.Length || a.Length == 0) return 0;
		float similarity = TensorPrimitives.CosineSimilarity(a, b);
		return float.IsFinite(similarity) ? similarity : 0;
	}

	private readonly record struct SemanticScore(long Id, double Score);

	private static void ValidateScore(double value, string name)
	{
		if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(name, "分数必须在 0 到 1 之间");
	}

	private static void AddParameter(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

	private static List<RetrievalHit> ReadHits(SqliteDataReader reader)
	{
		List<RetrievalHit> result = [];
		int rank = 0;
		while (reader.Read()) result.Add(new RetrievalHit(reader.GetInt64(0), 1.0 / (++rank), rank));
		return result;
	}

	private static List<RetrievalHit> SearchFts(SqliteConnection connection, string table, string keyword, int limit)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = $"SELECT CAST(memory_id AS INTEGER) FROM {table} WHERE {table} MATCH $query ORDER BY bm25({table}) LIMIT $limit";
		AddParameter(command, "$query", $"\"{keyword.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
		AddParameter(command, "$limit", Math.Max(0, limit));
		try
		{
			using SqliteDataReader reader = command.ExecuteReader();
			return ReadHits(reader);
		}
		catch (SqliteException)
		{
			return [];
		}
	}

	private static List<RetrievalHit> SearchLikeHits(SqliteConnection connection, string keyword, int limit)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			SELECT id FROM memories
			WHERE status IN ('active', 'dormant') AND (content LIKE $pattern OR canonical_summary LIKE $pattern OR persona_summary LIKE $pattern OR tags LIKE $pattern)
			ORDER BY importance DESC, id DESC LIMIT $limit
			""";
		AddParameter(command, "$pattern", $"%{keyword}%");
		AddParameter(command, "$limit", Math.Max(0, limit));
		using SqliteDataReader reader = command.ExecuteReader();
		return ReadHits(reader);
	}

	private void InitializeFts()
	{
		_database.Locked(connection =>
		{
			try
			{
				CreateFts(connection, "trigram");
			}
			catch (SqliteException)
			{
				try
				{
					CreateFts(connection, "unicode61");
				}
				catch (SqliteException)
				{
					_ftsAvailable = false;
					return;
				}
			}
			_ftsAvailable = true;
			using SqliteCommand rebuild = connection.CreateCommand();
			rebuild.CommandText = """
				DELETE FROM memories_fts;
				INSERT INTO memories_fts(memory_id, content, tags)
				SELECT id, COALESCE(content, '') || ' ' || COALESCE(canonical_summary, '') || ' ' || COALESCE(persona_summary, ''), COALESCE(tags, '')
				FROM memories WHERE status IN ('active', 'dormant');
				DELETE FROM memory_atoms_fts;
				INSERT INTO memory_atoms_fts(memory_id, content)
				SELECT id, content FROM memory_atoms WHERE status IN ('active', 'dormant');
				""";
			try { rebuild.ExecuteNonQuery(); }
			catch (SqliteException) { _ftsAvailable = false; }
		});
	}

	private static void CreateFts(SqliteConnection connection, string tokenizer)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = $"""
			CREATE VIRTUAL TABLE IF NOT EXISTS memories_fts USING fts5(memory_id UNINDEXED, content, tags, tokenize = '{tokenizer}');
			CREATE VIRTUAL TABLE IF NOT EXISTS memory_atoms_fts USING fts5(memory_id UNINDEXED, content, tokenize = '{tokenizer}');
			""";
		command.ExecuteNonQuery();
	}

	private void RefreshMemoryIndex(SqliteConnection connection, SqliteTransaction transaction, long id)
	{
		if (!_ftsAvailable) return;
		DeleteFtsRow(connection, transaction, "memories_fts", id);
		using SqliteCommand insert = connection.CreateCommand();
		insert.Transaction = transaction;
		insert.CommandText = """
			INSERT INTO memories_fts(memory_id, content, tags)
			SELECT id, COALESCE(content, '') || ' ' || COALESCE(canonical_summary, '') || ' ' || COALESCE(persona_summary, ''), COALESCE(tags, '')
			FROM memories WHERE id = $id AND status IN ('active', 'dormant')
			""";
		AddParameter(insert, "$id", id);
		insert.ExecuteNonQuery();
	}

	private void RefreshAtomIndex(SqliteConnection connection, SqliteTransaction transaction, long id)
	{
		if (!_ftsAvailable) return;
		DeleteFtsRow(connection, transaction, "memory_atoms_fts", id);
		using SqliteCommand insert = connection.CreateCommand();
		insert.Transaction = transaction;
		insert.CommandText = "INSERT INTO memory_atoms_fts(memory_id, content) SELECT id, content FROM memory_atoms WHERE id = $id AND status IN ('active', 'dormant')";
		AddParameter(insert, "$id", id);
		insert.ExecuteNonQuery();
	}

	private void RebuildAtomIndex(SqliteConnection connection, SqliteTransaction transaction)
	{
		if (!_ftsAvailable) return;
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "DELETE FROM memory_atoms_fts; INSERT INTO memory_atoms_fts(memory_id, content) SELECT id, content FROM memory_atoms WHERE status IN ('active', 'dormant');";
		command.ExecuteNonQuery();
	}

	private static void DeleteFtsRow(SqliteConnection connection, SqliteTransaction transaction, string table, long id)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = $"DELETE FROM {table} WHERE memory_id = $id";
		AddParameter(command, "$id", id);
		command.ExecuteNonQuery();
	}
}
