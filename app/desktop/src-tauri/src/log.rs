use chrono::Local;
use std::error::Error as StdError;
use std::fs::OpenOptions;
use std::io::Write;
use std::path::{Path, PathBuf};

/// 统一错误类型, 兼容 tauri setup(Box<dyn Error>)
pub type LogResult<T> = Result<T, Box<dyn StdError>>;

/// 日志目录: 软件安装目录/data/log (dev 时为 target/debug/data/log)
/// 与 db::data_dir 同级下的 log 子目录
fn log_dir() -> LogResult<PathBuf> {
    let exe = std::env::current_exe()?;
    Ok(exe
        .parent()
        .unwrap_or(Path::new("."))
        .join("data")
        .join("log"))
}

/// 当前本地时间, 形如 2026-01-01 12:00:00
fn now() -> String {
    Local::now().format("%Y-%m-%d %H:%M:%S").to_string()
}

/// 初始化日志: 创建日志目录 (setup 阶段调用)
pub fn init() -> LogResult<()> {
    let dir = log_dir()?;
    std::fs::create_dir_all(&dir)?;
    Ok(())
}

/// 追加一行日志到 data/log/debug.log
pub fn write(level: &str, message: &str) -> LogResult<()> {
    let dir = log_dir()?;
    std::fs::create_dir_all(&dir)?;
    let file = dir.join("debug.log");
    let mut f = OpenOptions::new().create(true).append(true).open(&file)?;
    writeln!(f, "[{}] [{}] {}", now(), level, message)?;
    Ok(())
}
