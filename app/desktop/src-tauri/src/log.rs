use chrono::Local;
use std::error::Error as StdError;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::Duration;

/// 统一错误类型, 兼容 tauri setup(Box<dyn Error>)
pub type LogResult<T> = Result<T, Box<dyn StdError>>;

/// 日志保留天数
const LOG_RETENTION_DAYS: u64 = 7;

/// 日志目录: 软件安装目录/data/log
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

/// 日志来源类型
pub enum LogSource {
    /// 前端调用 write_log 写入
    Frontend,
    /// Rust 后端直接调用
    Backend,
}

impl LogSource {
    fn prefix(&self) -> &str {
        match self {
            LogSource::Frontend => "frontend",
            LogSource::Backend => "backend",
        }
    }
}

/// 获取今天的日志文件名: frontend_2026-01-01.log 或 backend_2026-01-01.log
fn today_log_file(dir: &Path, source: &LogSource) -> PathBuf {
    let date = Local::now().format("%Y-%m-%d").to_string();
    dir.join(format!("{}_{}.log", source.prefix(), date))
}

/// 初始化日志: 创建日志目录 + 清理过期日志 (setup 阶段调用)
pub fn init() -> LogResult<()> {
    let dir = log_dir()?;
    fs::create_dir_all(&dir)?;
    cleanup_old_logs(&dir)?;
    Ok(())
}

/// 追加一行日志到 data/log/{source}_YYYY-MM-DD.log
pub fn write(source: &LogSource, level: &str, message: &str) -> LogResult<()> {
    let dir = log_dir()?;
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

        if let Ok(meta) = fs::metadata(&path) {
            if let Ok(modified) = meta.modified() {
                if let Ok(age) = now.duration_since(modified) {
                    if age > cutoff {
                        let _ = fs::remove_file(&path);
                    }
                }
            }
        }
    }
    Ok(())
}
