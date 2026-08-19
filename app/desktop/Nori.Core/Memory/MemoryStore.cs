using System.Globalization;
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
	public required string CreatedAt { get; init; }
	public required string UpdatedAt { get; init; }
}

/// <summary>
/// SQLite 记忆库存储层
/// </summary>
public sealed class MemoryStore(NoriDatabase database)
{
	private readonly NoriDatabase _database = database;

	/// <summary>
	/// 添加一条新记忆
	/// </summary>
	public MemoryItem Add(string type, string content, double importance = 0.5, string source = "chat", string? tags = null)
	{
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		long id = _database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO memories (type, content, importance, source, tags, created_at, updated_at)
				VALUES ($type, $content, $importance, $source, $tags, $created_at, $updated_at);
				SELECT last_insert_rowid();
				""";
			command.Parameters.AddWithValue("$type", type);
			command.Parameters.AddWithValue("$content", content);
			command.Parameters.AddWithValue("$importance", importance);
			command.Parameters.AddWithValue("$source", source);
			command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
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
			CreatedAt = now,
			UpdatedAt = now,
		};
	}

	/// <summary>
	/// 获取所有记忆 (按重要度与创建时间降序)
	/// </summary>
	public IReadOnlyList<MemoryItem> GetAll(int limit = 100) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, type, content, importance, source, tags, created_at, updated_at FROM memories ORDER BY importance DESC, id DESC LIMIT $limit";
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
			SELECT id, type, content, importance, source, tags, created_at, updated_at
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
	public bool Delete(long id) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM memories WHERE id = $id";
		command.Parameters.AddWithValue("$id", id);
		return command.ExecuteNonQuery() > 0;
	});

	/// <summary>
	/// 清空所有记忆
	/// </summary>
	public void Clear() => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM memories";
		command.ExecuteNonQuery();
	});

	private static MemoryItem ReadRow(SqliteDataReader reader) => new()
	{
		Id = reader.GetInt64(0),
		Type = reader.GetString(1),
		Content = reader.GetString(2),
		Importance = reader.GetDouble(3),
		Source = reader.GetString(4),
		Tags = reader.IsDBNull(5) ? null : reader.GetString(5),
		CreatedAt = reader.GetString(6),
		UpdatedAt = reader.GetString(7),
	};
}
