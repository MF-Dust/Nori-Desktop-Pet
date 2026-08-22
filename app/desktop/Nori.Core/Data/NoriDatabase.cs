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
	public const long DatabaseSchemaVersion = 3;

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

	public void Dispose()
	{
		lock (_gate) _connection.Dispose();
	}
}
