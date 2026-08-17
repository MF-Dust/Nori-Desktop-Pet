use rusqlite::Connection;
use std::error::Error as StdError;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use tauri::AppHandle;

use crate::config::{self, ConfigValue};

/// 统一错误类型: 可向上兼容 tauri setup(Box<dyn Error>)与 command(String)
pub type DbResult<T> = Result<T, Box<dyn StdError>>;

/// 数据库封装: 内部用 Mutex 包 Connection,供 Tauri state 跨命令共享
pub struct Db(pub Mutex<Connection>);

/// 建表
const SCHEMA: &str = "
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

/// 是否首次启动: config 表中不存在 first_run_completed 标记或标记值为 "0"
pub fn is_first_run(conn: &Connection) -> DbResult<bool> {
    match config::get(conn, KEY_FIRST_RUN_COMPLETED)? {
        None => Ok(true),
        Some(ConfigValue::String(s)) => Ok(s == "0"),
        Some(ConfigValue::Boolean(b)) => Ok(!b),
        _ => Ok(false),
    }
}

/// 写入 first_run_completed 标记 (幂等)
pub fn mark_first_run_completed(conn: &Connection) -> DbResult<()> {
    config::set(conn, KEY_FIRST_RUN_COMPLETED, &ConfigValue::Boolean(true))?;
    Ok(())
}
