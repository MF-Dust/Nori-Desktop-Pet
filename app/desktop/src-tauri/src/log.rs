//! 日志模块
//! Windows: %APPDATA%/<应用标识>/data/log/
//! macOS: ~/Library/Application Support/<应用标识>/data/log/
//! Linux: ~/.local/share/<应用标识>/data/log/

use chrono::Local;
use std::error::Error as StdError;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::Duration;
use tauri::{AppHandle, Manager};

/// 统一错误类型, 兼容 Tauri setup(Box<dyn Error>)
pub type LogResult<T> = Result<T, Box<dyn StdError>>;

/// 日志保留天数
const LOG_RETENTION_DAYS: u64 = 7;

/// 日志来源类型
pub enum LogSource {
    /// 前端调用 write_log 写入
    Frontend,

    /// Rust 后端直接调用
    Backend,
}

/// 日志来源类型实现
impl LogSource {
    fn prefix(&self) -> &'static str {
        match self {
            LogSource::Frontend => "frontend",
            LogSource::Backend => "backend",
        }
    }
}

/// 获取日志目录: 软件安装目录/data/log
fn log_dir(app: &AppHandle) -> LogResult<PathBuf> {
    let app_data_dir = app.path().app_data_dir()?;
    let dir = app_data_dir.join("data").join("log");
    Ok(dir)
}

/// 当前本地时间, 形如 2026-01-01 12:00:00
fn now() -> String {
    Local::now().format("%Y-%m-%d %H:%M:%S").to_string()
}

/// 获取今天的日志文件名
fn today_log_file(dir: &Path, source: &LogSource) -> PathBuf {
    let date = Local::now().format("%Y-%m-%d").to_string();
    dir.join(format!("{}_{}.log", source.prefix(), date))
}

/// 初始化日志
/// Tauri setup 阶段调用, 创建日志目录, 清理超过 LOG_RETENTION_DAYS 天的日志
pub fn init(app: &AppHandle) -> LogResult<()> {
    let dir = log_dir(app)?;
    // 创建日志目录
    fs::create_dir_all(&dir)?;
    // 清理旧日志
    cleanup_old_logs(&dir)?;
    Ok(())
}

/// 写入日志
pub fn write(app: &AppHandle, source: &LogSource, level: &str, message: &str) -> LogResult<()> {
    let dir = log_dir(app)?;
    // 确保目录存在
    fs::create_dir_all(&dir)?;
    let file = today_log_file(&dir, source);
    let mut f = OpenOptions::new().create(true).append(true).open(&file)?;
    writeln!(f, "[{}] [{}] {}", now(), level, message)?;
    Ok(())
}

/// 清理超过 LOG_RETENTION_DAYS 天的日志文件
fn cleanup_old_logs(dir: &Path) -> LogResult<()> {
    let cutoff = Duration::from_secs(LOG_RETENTION_DAYS * 24 * 60 * 60);
    let now = std::time::SystemTime::now();
    for entry in fs::read_dir(dir)? {
        let entry = entry?;
        let path = entry.path();
        // 只处理 .log 文件
        if path.extension().and_then(|e| e.to_str()) != Some("log") {
            continue;
        }
        let metadata = match entry.metadata() {
            Ok(metadata) => metadata,
            Err(_) => continue,
        };
        let modified = match metadata.modified() {
            Ok(modified) => modified,
            Err(_) => continue,
        };
        let age = match now.duration_since(modified) {
            Ok(age) => age,
            Err(_) => {
                // 文件修改时间在未来, 不删除
                continue;
            }
        };
        if age > cutoff {
            // 单个文件删除失败, 不影响应用启动
            let _ = fs::remove_file(&path);
        }
    }
    Ok(())
}
