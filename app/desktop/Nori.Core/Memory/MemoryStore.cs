using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;

namespace Nori.Core.Memory;

/// <summary>
/// 记忆数据模型
/// </summary>
public sealed record MemoryItem
{
	public required long Id { get; init; }
	public required string Type { get; init; }
	public required string Content { get; init; }
	public required double Importance { get; init; }
	public required string Source { get; init; }
	public string? Tags { get; init; }
	public string? Embedding { get; init; }
	public required string CreatedAt { get; init; }
	public required string UpdatedAt { get; init; }

	/// <summary>
	/// 辅助方法: 解析 JSON 向量数组
	/// </summary>
	public float[]? GetVector()
	{
		if (string.IsNullOrWhiteSpace(Embedding)) return null;
		try
		{
			return JsonSerializer.Deserialize<float[]>(Embedding);
		}
		catch
		{
			return null;
		}
	}
}

/// <summary>
/// 语义检索匹配结果
/// </summary>
public sealed record MemorySearchResult
{
	public required MemoryItem Item { get; init; }
	public required double Similarity { get; init; }
	public required double Score { get; init; }
}

/// <summary>
/// SQLite 记忆库存储层 (集成 BGE-M3 语义向量检索与混合搜索)
/// </summary>
public sealed class MemoryStore(NoriDatabase database)
{
	private readonly NoriDatabase _database = database;

	// 向量缓存: 语义检索的热路径上避免每次全量反序列化 JSON 向量。
	// 以 updated_at 做失效比对, 内容变更后自动重解析。
	private readonly Lock _vectorCacheGate = new();
	private readonly Dictionary<long, (string UpdatedAt, float[] Vector)> _vectorCache = [];

	/// <summary>
	/// 取记忆的向量, 命中缓存时不再反序列化 JSON
	/// </summary>
	private float[]? VectorOf(MemoryItem item)
	{
		if (string.IsNullOrWhiteSpace(item.Embedding)) return null;
		lock (_vectorCacheGate)
		{
			if (_vectorCache.TryGetValue(item.Id, out var cached) && cached.UpdatedAt == item.UpdatedAt)
			{
				return cached.Vector;
			}
			float[]? vector = item.GetVector();
			if (vector is not null) _vectorCache[item.Id] = (item.UpdatedAt, vector);
			return vector;
		}
	}

	/// <summary>从向量缓存中逐出一条 (行被删改时调用)</summary>
	private void EvictVector(long id)
	{
		lock (_vectorCacheGate)
		{
			_vectorCache.Remove(id);
		}
	}

	/// <summary>
	/// 添加一条新记忆
	/// </summary>
	public MemoryItem Add(
		string type,
		string content,
		double importance = 0.5,
		string source = "chat",
		string? tags = null,
		string? embedding = null)
	{
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		long id = _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO memories (type, content, importance, source, tags, embedding, created_at, updated_at)
				VALUES ($type, $content, $importance, $source, $tags, $embedding, $created_at, $updated_at);
				SELECT last_insert_rowid();
				""";
			command.Parameters.AddWithValue("$type", type);
			command.Parameters.AddWithValue("$content", content);
			command.Parameters.AddWithValue("$importance", importance);
			command.Parameters.AddWithValue("$source", source);
			command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
			command.Parameters.AddWithValue("$embedding", (object?)embedding ?? DBNull.Value);
			command.Parameters.AddWithValue("$created_at", now);
			command.Parameters.AddWithValue("$updated_at", now);

			return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
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
			CreatedAt = now,
			UpdatedAt = now,
		};
	}

	/// <summary>
	/// 更新记忆的向量嵌入 (用于后台批量补全或重新生成 Embedding)
	/// </summary>
	public bool UpdateEmbedding(long id, string embedding)
	{
		bool updated = _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "UPDATE memories SET embedding = $embedding WHERE id = $id";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$embedding", embedding);
			return command.ExecuteNonQuery() > 0;
		});
		if (updated) EvictVector(id);
		return updated;
	}

	/// <summary>
	/// 获取所有记忆 (按重要度与创建时间降序)
	/// </summary>
	public IReadOnlyList<MemoryItem> GetAll(int limit = 100) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, type, content, importance, source, tags, embedding, created_at, updated_at FROM memories ORDER BY importance DESC, id DESC LIMIT $limit";
		command.Parameters.AddWithValue("$limit", limit);
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryItem> list = [];
		while (reader.Read())
		{
			list.Add(ReadRow(reader));
		}
		return list;
	});

	/// <summary>
	/// 按关键词搜索记忆
	/// </summary>
	public IReadOnlyList<MemoryItem> Search(string keyword, int limit = 20) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			SELECT id, type, content, importance, source, tags, embedding, created_at, updated_at
			FROM memories
			WHERE content LIKE $pattern OR tags LIKE $pattern
			ORDER BY importance DESC, id DESC
			LIMIT $limit
			""";
		command.Parameters.AddWithValue("$pattern", $"%{keyword}%");
		command.Parameters.AddWithValue("$limit", limit);
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryItem> list = [];
		while (reader.Read())
		{
			list.Add(ReadRow(reader));
		}
		return list;
	});

	/// <summary>
	/// 全表读取记忆 (语义检索用, 不截断: 截断会静默丢掉更早的记忆)
	/// </summary>
	private IReadOnlyList<MemoryItem> GetAllUnbounded() => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, type, content, importance, source, tags, embedding, created_at, updated_at FROM memories";
		using SqliteDataReader reader = command.ExecuteReader();
		List<MemoryItem> list = [];
		while (reader.Read())
		{
			list.Add(ReadRow(reader));
		}
		return (IReadOnlyList<MemoryItem>)list;
	});

	/// <summary>
	/// 基于 BGE-M3 / OpenAI 向量的语义检索 (余弦相似度)
	/// </summary>
	public IReadOnlyList<MemorySearchResult> SearchSemantic(
		float[] queryVector,
		int limit = 10,
		double minSimilarity = 0.25)
	{
		IReadOnlyList<MemoryItem> all = GetAllUnbounded();
		List<MemorySearchResult> results = [];

		foreach (MemoryItem item in all)
		{
			float[]? vec = VectorOf(item);
			if (vec == null || vec.Length != queryVector.Length) continue;

			double sim = CosineSimilarity(queryVector, vec);
			if (sim >= minSimilarity)
			{
				// 综合评分 = 向量相似度 * 0.75 + 记忆重要度 * 0.25
				double score = (sim * 0.75) + (item.Importance * 0.25);
				results.Add(new MemorySearchResult
				{
					Item = item,
					Similarity = sim,
					Score = score,
				});
			}
		}

		results.Sort((a, b) => b.Score.CompareTo(a.Score));
		limit = Math.Max(0, limit);
		if (results.Count > limit)
		{
			results.RemoveRange(limit, results.Count - limit);
		}
		return results;
	}

	/// <summary>
	/// 混合检索 (关键词匹配 + 向量语义检索加权融合)
	/// </summary>
	public IReadOnlyList<MemoryItem> SearchHybrid(
		string keyword,
		float[]? queryVector = null,
		int limit = 10)
	{
		if (queryVector != null && queryVector.Length > 0)
		{
			IReadOnlyList<MemorySearchResult> semanticResults = SearchSemantic(queryVector, limit);
			if (semanticResults.Count > 0)
			{
				List<MemoryItem> items = [];
				foreach (MemorySearchResult res in semanticResults)
				{
					items.Add(res.Item);
				}
				return items;
			}
		}

		// 降级回退至关键词文本搜索
		return Search(keyword, limit);
	}

	/// <summary>
	/// 更新记忆内容与重要性
	/// </summary>
	public bool Update(long id, string content, double? importance = null, string? tags = null) => _database.Locked(connection =>
	{
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			UPDATE memories
			SET content = $content,
			    importance = COALESCE($importance, importance),
			    tags = COALESCE($tags, tags),
			    updated_at = $updated_at
			WHERE id = $id
			""";
		command.Parameters.AddWithValue("$id", id);
		command.Parameters.AddWithValue("$content", content);
		command.Parameters.AddWithValue("$importance", (object?)importance ?? DBNull.Value);
		command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
		command.Parameters.AddWithValue("$updated_at", now);

		return command.ExecuteNonQuery() > 0;
	});

	/// <summary>
	/// 删除单条记忆
	/// </summary>
	public bool Delete(long id)
	{
		bool deleted = _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "DELETE FROM memories WHERE id = $id";
			command.Parameters.AddWithValue("$id", id);
			return command.ExecuteNonQuery() > 0;
		});
		if (deleted) EvictVector(id);
		return deleted;
	}

	/// <summary>
	/// 清空所有记忆
	/// </summary>
	public void Clear()
	{
		_database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "DELETE FROM memories";
			command.ExecuteNonQuery();
		});
		lock (_vectorCacheGate)
		{
			_vectorCache.Clear();
		}
	}

	/// <summary>
	/// 计算两个向量的余弦相似度
	/// </summary>
	public static double CosineSimilarity(float[] a, float[] b)
	{
		if (a.Length != b.Length || a.Length == 0) return 0;
		double dot = 0;
		double normA = 0;
		double normB = 0;
		for (int i = 0; i < a.Length; i++)
		{
			dot += a[i] * b[i];
			normA += a[i] * a[i];
			normB += b[i] * b[i];
		}
		if (normA <= 0 || normB <= 0) return 0;
		return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
	}

	private static MemoryItem ReadRow(SqliteDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Type = reader.GetString(1),
		Content = reader.GetString(2),
		Importance = reader.GetDouble(3),
		Source = reader.GetString(4),
		Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
		Embedding = reader.IsDBNull(6) ? null : reader.GetString(6),
		CreatedAt = reader.GetString(7),
		UpdatedAt = reader.GetString(8),
	};
}
