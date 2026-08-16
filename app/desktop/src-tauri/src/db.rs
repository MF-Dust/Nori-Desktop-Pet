use rusqlite::{params, Connection};
use std::error::Error as StdError;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use tauri::AppHandle;

/// 统一错误类型: 可向上兼容 tauri setup(Box<dyn Error>)与 command(String)
pub type DbResult<T> = Result<T, Box<dyn StdError>>;

/// 数据库封装: 内部用 Mutex 包 Connection,供 Tauri state 跨命令共享
pub struct Db(pub Mutex<Connection>);

/// 建表
const SCHEMA: &str = "
CREATE TABLE IF NOT EXISTS logs (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT    NOT NULL DEFAULT (datetime('now', 'localtime')),
    level      TEXT    NOT NULL,
    message    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS config (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
";

/// 配置键: 首次初始化是否已完成
const KEY_FIRST_RUN_COMPLETED: &str = "first_run_completed";

/// 打开 (或创建) 数据库并建表
pub fn init(app: &AppHandle) -> DbResult<Db> {
    let dir = data_dir(app)?;
    std::fs::create_dir_all(&dir)?;
    let conn = Connection::open(dir.join("nori.db"))?;
    conn.execute_batch(SCHEMA)?;
    Ok(Db(Mutex::new(conn)))
}

/// 数据目录: 软件安装目录 (可执行文件所在目录) 下的 data 文件夹,
/// dev 时为 src-tauri/target/debug/data, 打包后为安装目录/data
fn data_dir(_app: &AppHandle) -> DbResult<PathBuf> {
    let exe = std::env::current_exe()?;
    Ok(exe.parent().unwrap_or(Path::new(".")).join("data"))
}

/// 是否首次启动:config 表中不存在 first_run_completed 标记
pub fn is_first_run(conn: &Connection) -> DbResult<bool> {
    let count: i64 = conn.query_row(
        "SELECT COUNT(*) FROM config WHERE key = ?1",
        params![KEY_FIRST_RUN_COMPLETED],
        |row| row.get(0),
    )?;
    Ok(count == 0)
}

/// 写入 first_run_completed 标记 (幂等)
pub fn mark_first_run_completed(conn: &Connection) -> DbResult<()> {
    conn.execute(
        "INSERT OR REPLACE INTO config (key, value) VALUES (?1, ?2)",
        params![KEY_FIRST_RUN_COMPLETED, "1"],
    )?;
    Ok(())
}

/// 写一条日志
pub fn write_log(conn: &Connection, level: &str, message: &str) -> DbResult<()> {
    conn.execute(
        "INSERT INTO logs (level, message) VALUES (?1, ?2)",
        params![level, message],
    )?;
    Ok(())
}
