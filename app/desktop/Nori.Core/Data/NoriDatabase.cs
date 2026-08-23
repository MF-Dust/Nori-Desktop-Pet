using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Nori.Core.Data;

/// <summary>
/// 数据库
///
/// 对应 Rust 版的 db.rs: 打开或创建 nori.db, 建表, 补默认配置, 校验结构版本.
/// Rust 用 Mutex&lt;Connection&gt; 跨命令共享单连接, 这里用同一把锁保持等价语义.
/// </summary>
public sealed class NoriDatabase : IDisposable
{
	/// <summary>当前 memories 数据库结构版本</summary>
	public const long DatabaseSchemaVersion = 4;

	/// <summary>建表语句, 与 Rust 版 SCHEMA 完全一致</summary>
	private const string Schema = """
		CREATE TABLE IF NOT EXISTS config (
		    key   TEXT PRIMARY KEY,
		    value TEXT NOT NULL
		);
		CREATE TABLE IF NOT EXISTS chat_messages (
		    id         INTEGER PRIMARY KEY AUTOINCREMENT,
		    role       TEXT NOT NULL,
		    content    TEXT NOT NULL,
		    created_at TEXT NOT NULL
		);
		CREATE TABLE IF NOT EXISTS memories (
		    id          INTEGER PRIMARY KEY AUTOINCREMENT,
		    type        TEXT NOT NULL,
		    content     TEXT NOT NULL,
		    importance  REAL NOT NULL DEFAULT 0.5,
		    source      TEXT NOT NULL DEFAULT 'chat',
		    tags        TEXT,
		    embedding   TEXT,
		    created_at  TEXT NOT NULL,
		    updated_at  TEXT NOT NULL
		);
		CREATE INDEX IF NOT EXISTS idx_memories_importance ON memories(importance DESC, id DESC);
		""";
	private readonly SqliteConnection _connection;
	private readonly Lock _gate = new();

	private NoriDatabase(SqliteConnection connection) => _connection = connection;

	/// <summary>
	/// 打开数据库文件. 传 null 走默认数据目录, 测试可传临时路径.
	/// </summary>
	public static NoriDatabase Open(string? databasePath = null)
	{
		string path = databasePath ?? AppPaths.DatabasePath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		SqliteConnection connection = new(new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = SqliteOpenMode.ReadWriteCreate,
		}.ToString());
		connection.Open();
		NoriDatabase database = new(connection);
		try
		{
			// 外键必须在事务开始前启用, 记忆删除时由 SQLite 清理 Atom/Source/Knowledge 子记录.
			database.Execute("PRAGMA foreign_keys=ON;");
			// WAL: 写不阻塞读, 拖拽落盘这类高频小写不会让界面卡在 fsync 上
			database.Execute("PRAGMA journal_mode=WAL;");
			// 多线程争用单连接时等锁而不是立刻抛 "database is locked"
			database.Execute("PRAGMA busy_timeout=5000;");
			database.Execute(Schema);
			database.MigrateSchema();
			return database;
		}
		catch
		{
			database.Dispose();
			throw;
		}
	}

	/// <summary>
	/// 在锁内执行一段数据库操作
	/// </summary>
	public T Locked<T>(Func<SqliteConnection, T> action)
	{
		lock (_gate) return action(_connection);
	}

	/// <summary>
	/// 在锁内执行一段无返回值的数据库操作
	/// </summary>
	public void Locked(Action<SqliteConnection> action)
	{
		lock (_gate) action(_connection);
	}

	/// <summary>
	/// 执行不返回结果的 SQL (可含多条语句)
	/// </summary>
	private void Execute(string sql) => Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	});

	/// <summary>
	/// 按 user_version 逐级迁移 memories 数据库结构.
	/// 每一级都在同一个事务中提交; 任何磁盘、锁、语法或损坏错误都会向上传播.
	/// </summary>
	private void MigrateSchema() => Locked(connection =>
	{
		long current = ReadUserVersion(connection);
		if (current > DatabaseSchemaVersion)
		{
			throw new InvalidOperationException($"记忆数据库版本 {current} 高于当前应用支持版本 {DatabaseSchemaVersion}, 请升级应用");
		}
		if (current == DatabaseSchemaVersion) return;

		using SqliteTransaction transaction = connection.BeginTransaction();
		try
		{
			long version = current;
			while (version < DatabaseSchemaVersion)
			{
				switch (version)
				{
					case 0:
						EnsureEmbeddingColumn(connection, transaction);
						break;
					case 1:
						ClearLegacyEmbeddings(connection, transaction);
						break;
					case 2:
						CreateRemindersTable(connection, transaction);
						break;
					case 3:
						MigrateMemoryEngineV4(connection, transaction);
						break;
					default:
						throw new InvalidOperationException($"不支持的记忆数据库版本: {version}");
				}

				version++;
				SetUserVersion(connection, transaction, version);
			}
			transaction.Commit();
		}
		catch
		{
			try
			{
				transaction.Rollback();
			}
			catch
			{
				// 保留原始迁移异常, 回滚失败不会掩盖真正原因.
			}
			throw;
		}
	});

	/// <summary>读取 SQLite 的独立结构版本</summary>
	private static long ReadUserVersion(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA user_version;";
		return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	/// <summary>写入 SQLite 的独立结构版本</summary>
	private static void SetUserVersion(SqliteConnection connection, SqliteTransaction transaction, long version)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = $"PRAGMA user_version = {version};";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// 仅在元数据确认缺列时补 embedding.
	/// 不通过捕获 ALTER 异常判断“列已存在”, 避免吞掉损坏/权限/磁盘错误.
	/// </summary>
	private static void EnsureEmbeddingColumn(SqliteConnection connection, SqliteTransaction transaction)
	{
		bool hasEmbedding = false;
		using (SqliteCommand command = connection.CreateCommand())
		{
			command.Transaction = transaction;
			command.CommandText = "PRAGMA table_info(memories);";
			using SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				if (reader.GetString(1).Equals("embedding", StringComparison.OrdinalIgnoreCase))
				{
					hasEmbedding = true;
					break;
				}
			}
		}

		if (hasEmbedding) return;
		using SqliteCommand alter = connection.CreateCommand();
		alter.Transaction = transaction;
		alter.CommandText = "ALTER TABLE memories ADD COLUMN embedding TEXT;";
		alter.ExecuteNonQuery();
	}

	/// <summary>一次性失效历史向量, 迁移本身不访问外部 embedding 服务.</summary>
	private static void ClearLegacyEmbeddings(SqliteConnection connection, SqliteTransaction transaction)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "UPDATE memories SET embedding = NULL WHERE embedding IS NOT NULL;";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// v3: 新增可恢复的定时提醒表.
	/// 幂等: 建表语句带 IF NOT EXISTS, 重复执行安全。
	/// </summary>
	private static void CreateRemindersTable(SqliteConnection connection, SqliteTransaction transaction)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = """
			CREATE TABLE IF NOT EXISTS reminders (
			    id          TEXT PRIMARY KEY,
			    content     TEXT NOT NULL,
			    trigger_at  INTEGER NOT NULL,
			    repeat_daily INTEGER NOT NULL DEFAULT 0,
			    created_at  TEXT NOT NULL
			);
			CREATE INDEX IF NOT EXISTS idx_reminders_trigger ON reminders(trigger_at ASC);
			""";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// v4: 建立 Living Memory 聚合、事实原子、来源和知识索引元数据.
	/// 旧记忆不删除: 摘要、状态、来源和 Atom 都在同一事务中回填.
	/// </summary>
	private static void MigrateMemoryEngineV4(SqliteConnection connection, SqliteTransaction transaction)
	{
		(string Name, string Definition)[] columns =
		[
			("kind", "TEXT NOT NULL DEFAULT 'general'"),
			("canonical_summary", "TEXT"),
			("persona_summary", "TEXT"),
			("confidence", "REAL NOT NULL DEFAULT 0.8"),
			("status", "TEXT NOT NULL DEFAULT 'active'"),
			("access_count", "INTEGER NOT NULL DEFAULT 0"),
			("reinforcement_count", "INTEGER NOT NULL DEFAULT 0"),
			("last_accessed_at", "TEXT"),
			("last_reinforced_at", "TEXT"),
			("ttl_days", "REAL"),
			("expires_at", "TEXT"),
			("superseded_by", "INTEGER"),
			("embedding_fingerprint", "TEXT"),
		];
		foreach ((string name, string definition) in columns)
		{
			if (HasColumn(connection, transaction, "memories", name)) continue;
			using SqliteCommand alter = connection.CreateCommand();
			alter.Transaction = transaction;
			alter.CommandText = $"ALTER TABLE memories ADD COLUMN {name} {definition};";
			alter.ExecuteNonQuery();
		}

		using (SqliteCommand backfill = connection.CreateCommand())
		{
			backfill.Transaction = transaction;
			backfill.CommandText = """
				UPDATE memories
				SET kind = CASE lower(type)
					WHEN 'fact' THEN 'factual'
					WHEN 'preference' THEN 'preference'
					WHEN 'identity' THEN 'identity'
					WHEN 'relational' THEN 'relational'
					WHEN 'episodic' THEN 'episodic'
					WHEN 'planned' THEN 'planned'
					ELSE 'general'
				END,
				canonical_summary = COALESCE(canonical_summary, content),
				persona_summary = COALESCE(persona_summary, content),
				confidence = COALESCE(confidence, 0.8),
				status = COALESCE(status, 'active'),
				embedding_fingerprint = CASE
					WHEN embedding IS NOT NULL AND embedding <> '' AND (embedding_fingerprint IS NULL OR embedding_fingerprint = '')
					THEN 'legacy-unknown'
					ELSE embedding_fingerprint
				END;
				""";
			backfill.ExecuteNonQuery();
		}

		using (SqliteCommand create = connection.CreateCommand())
		{
			create.Transaction = transaction;
			create.CommandText = """
				CREATE TABLE IF NOT EXISTS memory_atoms (
				    id INTEGER PRIMARY KEY AUTOINCREMENT,
				    parent_memory_id INTEGER NOT NULL,
				    atom_type TEXT NOT NULL,
				    content TEXT NOT NULL,
				    importance REAL NOT NULL DEFAULT 0.5,
				    confidence REAL NOT NULL DEFAULT 0.8,
				    status TEXT NOT NULL DEFAULT 'active',
				    created_at TEXT NOT NULL,
				    last_accessed_at TEXT,
				    last_reinforced_at TEXT,
				    ttl_days REAL,
				    expires_at TEXT,
				    reinforcement_count INTEGER NOT NULL DEFAULT 0,
				    decay_type TEXT NOT NULL DEFAULT 'exponential',
				    entities TEXT,
				    superseded_by INTEGER,
				    FOREIGN KEY(parent_memory_id) REFERENCES memories(id) ON DELETE CASCADE
				);
				CREATE TABLE IF NOT EXISTS memory_sources (
				    id INTEGER PRIMARY KEY AUTOINCREMENT,
				    memory_id INTEGER NOT NULL,
				    role TEXT NOT NULL,
				    content TEXT NOT NULL,
				    message_time TEXT,
				    sequence INTEGER NOT NULL,
				    FOREIGN KEY(memory_id) REFERENCES memories(id) ON DELETE CASCADE
				);
				CREATE TABLE IF NOT EXISTS knowledge_documents (
				    id INTEGER PRIMARY KEY AUTOINCREMENT,
				    path TEXT NOT NULL UNIQUE,
				    content_hash TEXT NOT NULL,
				    updated_at TEXT NOT NULL
				);
				CREATE TABLE IF NOT EXISTS knowledge_chunks (
				    id INTEGER PRIMARY KEY AUTOINCREMENT,
				    document_id INTEGER NOT NULL,
				    chunk_key TEXT NOT NULL,
				    sequence INTEGER NOT NULL DEFAULT 0,
				    heading TEXT,
				    subheading TEXT,
				    content TEXT NOT NULL,
				    knowledge_type TEXT,
				    awareness TEXT,
				    content_hash TEXT NOT NULL,
				    embedding TEXT,
				    embedding_fingerprint TEXT,
				    updated_at TEXT NOT NULL,
				    FOREIGN KEY(document_id) REFERENCES knowledge_documents(id) ON DELETE CASCADE,
				    UNIQUE(document_id, chunk_key)
				);
				CREATE TABLE IF NOT EXISTS memory_engine_state (
				    key TEXT PRIMARY KEY,
				    value TEXT NOT NULL
				);
				CREATE INDEX IF NOT EXISTS idx_memories_status_kind ON memories(status, kind, importance DESC);
				CREATE INDEX IF NOT EXISTS idx_memories_accessed ON memories(last_accessed_at);
				CREATE INDEX IF NOT EXISTS idx_memory_atoms_parent_status ON memory_atoms(parent_memory_id, status);
				CREATE INDEX IF NOT EXISTS idx_memory_atoms_type_status ON memory_atoms(atom_type, status, importance DESC);
				CREATE INDEX IF NOT EXISTS idx_memory_sources_memory ON memory_sources(memory_id, sequence);
				CREATE INDEX IF NOT EXISTS idx_knowledge_chunks_document ON knowledge_chunks(document_id, sequence);
				""";
			create.ExecuteNonQuery();
		}

		using (SqliteCommand atoms = connection.CreateCommand())
		{
			atoms.Transaction = transaction;
			atoms.CommandText = """
				INSERT INTO memory_atoms
					(parent_memory_id, atom_type, content, importance, confidence, status, created_at, ttl_days)
				SELECT m.id, m.kind, COALESCE(m.canonical_summary, m.content), m.importance,
				       m.confidence, m.status, m.created_at, m.ttl_days
				FROM memories AS m
				WHERE NOT EXISTS (
					SELECT 1 FROM memory_atoms AS a WHERE a.parent_memory_id = m.id
				);
				""";
			atoms.ExecuteNonQuery();
		}
	}

	private static bool HasColumn(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = $"PRAGMA table_info({table});";
		using SqliteDataReader reader = command.ExecuteReader();
		while (reader.Read())
		{
			if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	public void Dispose()
	{
		lock (_gate) _connection.Dispose();
	}
}
