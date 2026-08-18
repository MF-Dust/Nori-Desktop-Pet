//! 数据库模块
//! Windows: %APPDATA%/<应用标识>/data/<DB_FILE_NAME>
//! macOS: ~/Library/Application Support/<应用标识>/data/<DB_FILE_NAME>
//! Linux: ~/.local/share/<应用标识>/data/<DB_FILE_NAME>

use rusqlite::Connection;
use std::error::Error as StdError;
use std::path::PathBuf;
use std::sync::Mutex;
use tauri::{AppHandle, Manager};

use crate::config::{self};
use crate::log::{self, LogSource};

/// 统一错误类型: 兼容 tauri setup(Box<dyn Error>)与 command(String)
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

/// 数据库文件名
const DB_FILE_NAME: &str = "nori.db";

/// 初始化数据库
pub fn init(app: &AppHandle) -> DbResult<Db> {
    let dir = data_dir(app)?;
    // 确保 data 目录存在
    std::fs::create_dir_all(&dir)?;
    let db_path = dir.join(DB_FILE_NAME);
    // 打开或创建 SQLite 数据库
    let conn = Connection::open(&db_path)?;
    // 创建数据库表
    conn.execute_batch(SCHEMA)?;
    // 初始化默认配置
    // 只补充缺失配置, 不覆盖用户已有配置.
    config::init_defaults(&conn)?;
    // 检查配置结构版本.
    // 为未来数据库 / 配置迁移预留.
    config::ensure_schema_version(&conn)?;
    // 记录数据库位置
    let _ = log::write(
        app,
        &LogSource::Backend,
        "info",
        &format!("数据库已打开: {}", db_path.display()),
    );

    Ok(Db(Mutex::new(conn)))
}

/// 获取应用数据目录
pub fn data_dir(app: &AppHandle) -> DbResult<PathBuf> {
    let dir = app
        .path()
        .app_data_dir()?
        .join("data");
    std::fs::create_dir_all(&dir)?;
    Ok(dir)
}

/// 获取 SQLite 数据库文件路径
pub fn database_path(app: &AppHandle) -> DbResult<PathBuf> {
    Ok(data_dir(app)?.join(DB_FILE_NAME))
}
