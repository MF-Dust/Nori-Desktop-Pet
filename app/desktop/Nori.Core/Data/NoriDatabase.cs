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
		// WAL: 写不阻塞读, 拖拽落盘这类高频小写不会让界面卡在 fsync 上
		database.Execute("PRAGMA journal_mode=WAL;");
		// 多线程争用单连接时等锁而不是立刻抛 "database is locked"
		database.Execute("PRAGMA busy_timeout=5000;");
		database.Execute(Schema);

		// 检查并自动升级旧版数据库缺失的 embedding 列
		try
		{
			database.Execute("ALTER TABLE memories ADD COLUMN embedding TEXT;");
		}
		catch
		{
			/* 若已存在该列则忽略异常 */
		}

		return database;
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

	public void Dispose()
	{
		lock (_gate) _connection.Dispose();
	}
}
