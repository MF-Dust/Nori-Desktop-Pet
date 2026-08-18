//! 配置模块

use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::error::Error as StdError;
use tauri::Emitter;

pub type ConfigResult<T> = Result<T, Box<dyn StdError>>;

/// 配置值类型: 支持基础类型和 JSON
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(untagged)]
pub enum ConfigValue {
    String(String),
    Integer(i64),
    Boolean(bool),
    Json(Value),
}

impl ConfigValue {
    /// 转换成 SQLite 中保存的字符串
    pub fn to_storage(&self) -> String {
        match self {
            ConfigValue::String(value) => value.clone(),
            ConfigValue::Integer(value) => value.to_string(),
            ConfigValue::Boolean(value) => {
                if *value {
                    "1".to_string()
                } else {
                    "0".to_string()
                }
            }
            ConfigValue::Json(value) => value.to_string(),
        }
    }

    /// 从 SQLite 保存的字符串恢复 ConfigValue
    pub fn from_storage(value: &str) -> ConfigResult<Self> {
        // Boolean
        if value == "1" || value.eq_ignore_ascii_case("true") {
            return Ok(ConfigValue::Boolean(true));
        }
        if value == "0" || value.eq_ignore_ascii_case("false") {
            return Ok(ConfigValue::Boolean(false));
        }
        // Integer
        if let Ok(number) = value.parse::<i64>() {
            return Ok(ConfigValue::Integer(number));
        }
        // JSON
        if let Ok(json) = serde_json::from_str::<Value>(value) {
            match json {
                Value::Object(_) | Value::Array(_) => {
                    return Ok(ConfigValue::Json(json));
                }
                _ => {}
            }
        }
        // 默认字符串
        Ok(ConfigValue::String(value.to_string()))
    }
}

/// 读取配置
pub fn get(conn: &Connection, key: &str) -> ConfigResult<Option<ConfigValue>> {
    let result = conn.query_row(
        "SELECT value FROM config WHERE key = ?1",
        params![key],
        |row| row.get::<_, String>(0),
    );
    match result {
        Ok(value) => Ok(Some(ConfigValue::from_storage(&value)?)),
        Err(rusqlite::Error::QueryReturnedNoRows) => Ok(None),
        Err(error) => Err(error.into()),
    }
}

/// 写入配置
pub fn set(conn: &Connection, key: &str, value: &ConfigValue) -> ConfigResult<()> {
    conn.execute(
        r#"
        INSERT INTO config (key, value)
        VALUES (?1, ?2)
        ON CONFLICT(key)
        DO UPDATE SET value = excluded.value
        "#,
        params![key, value.to_storage()],
    )?;
    Ok(())
}

/// 删除配置, 返回是否真的删除了记录
pub fn delete(conn: &Connection, key: &str) -> ConfigResult<bool> {
    let affected = conn.execute("DELETE FROM config WHERE key = ?1", params![key])?;
    Ok(affected > 0)
}

/// 判断配置是否存在
pub fn exists(conn: &Connection, key: &str) -> ConfigResult<bool> {
    let count: i64 = conn.query_row(
        "SELECT COUNT(*) FROM config WHERE key = ?1",
        params![key],
        |row| row.get(0),
    )?;
    Ok(count > 0)
}

/// 获取所有配置
pub fn get_all(conn: &Connection) -> ConfigResult<Vec<(String, ConfigValue)>> {
    let mut stmt = conn.prepare("SELECT key, value FROM config ORDER BY key")?;
    let rows = stmt.query_map([], |row| {
        let key = row.get::<_, String>(0)?;
        let value = row.get::<_, String>(1)?;
        Ok((key, value))
    })?;
    let mut result = Vec::new();
    for row in rows {
        let (key, value) = row?;
        let value = ConfigValue::from_storage(&value).unwrap_or(ConfigValue::String(value));
        result.push((key, value));
    }
    Ok(result)
}

/// 读取字符串配置, 缺失/类型不符时返回 fallback
pub fn get_str_or(conn: &Connection, key: &str, fallback: &str) -> String {
    match get(conn, key) {
        Ok(Some(ConfigValue::String(value))) if !value.is_empty() => value,
        Ok(Some(ConfigValue::Integer(value))) => value.to_string(),
        Ok(Some(ConfigValue::Boolean(value))) => value.to_string(),
        _ => fallback.to_string(),
    }
}

/// 配置键: 配置结构版本 (不兼容变更时 +1, 用于迁移)
pub const KEY_CONFIG_SCHEMA_VERSION: &str = "config_schema_version";
/// 配置键: 首次启动 (数据库创建) 时间
pub const KEY_INSTALLED_AT: &str = "installed_at";
/// 配置键: 首次初始化完成时间
pub const KEY_INITIALIZED_AT: &str = "initialized_at";
/// 配置键: 应用版本 (首次安装时的版本)
pub const KEY_APP_VERSION: &str = "app_version";
/// 配置键: 界面语言
pub const KEY_LANGUAGE: &str = "language";
/// 配置键: 桌宠模型
pub const KEY_SELECTED_MODEL: &str = "selected_model";
/// 配置键: 首次初始化是否已完成
pub const KEY_FIRST_RUN_COMPLETED: &str = "first_run_completed";

/// 当前配置结构版本
const CONFIG_SCHEMA_VERSION: i64 = 1;
/// 默认桌宠模型
const DEFAULT_MODEL: &str = "arg-nori";

/// 当前本地时间, 形如 2026-01-01 12:00:00
fn now() -> String {
    chrono::Local::now().format("%Y-%m-%d %H:%M:%S").to_string()
}

/// 系统语言, 获取失败时回退 zh-CN
fn system_language() -> String {
    sys_locale::get_locale().unwrap_or_else(|| "zh-CN".to_string())
}

/// 初始化默认配置
pub fn init_defaults(conn: &Connection) -> ConfigResult<()> {
    let defaults = [
        (
            KEY_CONFIG_SCHEMA_VERSION,
            ConfigValue::Integer(CONFIG_SCHEMA_VERSION),
        ),
        (
            KEY_APP_VERSION,
            ConfigValue::String(env!("CARGO_PKG_VERSION").to_string()),
        ),
        (KEY_INSTALLED_AT, ConfigValue::String(now())),
        (KEY_LANGUAGE, ConfigValue::String(system_language())),
        (
            KEY_SELECTED_MODEL,
            ConfigValue::String(DEFAULT_MODEL.to_string()),
        ),
        (KEY_FIRST_RUN_COMPLETED, ConfigValue::Boolean(false)),
    ];
    for (key, value) in defaults {
        conn.execute(
            r#"
            INSERT OR IGNORE INTO config (key, value)
            VALUES (?1, ?2)
            "#,
            params![key, value.to_storage()],
        )?;
    }
    Ok(())
}

/// 检查配置结构版本
///
/// current < app version  → 预留未来数据库迁移
/// current == app version  → 正常
/// current > app version → 返回错误, 防止旧程序错误修改新数据库
pub fn ensure_schema_version(conn: &Connection) -> ConfigResult<()> {
    let current = match get(conn, KEY_CONFIG_SCHEMA_VERSION)? {
        Some(ConfigValue::Integer(value)) => value,
        Some(ConfigValue::String(value)) => value.parse::<i64>().unwrap_or(CONFIG_SCHEMA_VERSION),
        _ => CONFIG_SCHEMA_VERSION,
    };
    if current > CONFIG_SCHEMA_VERSION {
        return Err(format!(
            "配置数据库版本 {} 高于当前应用支持版本 {}, 请升级应用",
            current, CONFIG_SCHEMA_VERSION
        )
        .into());
    }
    if current < CONFIG_SCHEMA_VERSION {
        migrate_schema(conn, current, CONFIG_SCHEMA_VERSION)?;
    }
    Ok(())
}

/// 数据库结构迁移
///
/// 未来版本例如:
/// v1 → v2
/// v2 → v3
/// 在这里逐级迁移
fn migrate_schema(conn: &Connection, from: i64, to: i64) -> ConfigResult<()> {
    let mut version = from;
    while version < to {
        match version {
            // v1 → v2
            //
            // 示例：
            // migrate_v1_to_v2(conn)?;
            1 => {
                // 当前没有 v2, 暂时不执行实际迁移
            }
            _ => {
                return Err(format!("不支持的配置数据库版本: {}", version).into());
            }
        }
        version += 1;
    }
    conn.execute(
        r#"
        INSERT INTO config (key, value)
        VALUES (?1, ?2)
        ON CONFLICT(key)
        DO UPDATE SET value = excluded.value
        "#,
        params![KEY_CONFIG_SCHEMA_VERSION, to.to_string()],
    )?;
    Ok(())
}

/// 判断是否首次启动
pub fn is_first_run(conn: &Connection) -> ConfigResult<bool> {
    match get(conn, KEY_FIRST_RUN_COMPLETED)? {
        None => Ok(true),
        Some(ConfigValue::Boolean(value)) => Ok(!value),
        Some(ConfigValue::Integer(value)) => Ok(value == 0),
        Some(ConfigValue::String(value)) => {
            Ok(value == "0" || value.eq_ignore_ascii_case("false"))
        }
        Some(ConfigValue::Json(_)) => Ok(false),
    }
}

/// 标记首次启动完成
pub fn mark_first_run_completed(conn: &Connection) -> ConfigResult<()> {
    set(conn, KEY_FIRST_RUN_COMPLETED, &ConfigValue::Boolean(true))?;
    Ok(())
}

/// 记录首次初始化完成时间
pub fn mark_initialized(conn: &Connection) -> ConfigResult<()> {
    if !exists(conn, KEY_INITIALIZED_AT)? {
        set(conn, KEY_INITIALIZED_AT, &ConfigValue::String(now()))?;
    }
    Ok(())
}

/// 首次初始化配置快照
/// 使用 camelCase 与前端 TypeScript 接口保持一致
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InitConfig {
    pub config_schema_version: i64,
    pub app_version: String,
    pub installed_at: String,
    pub initialized_at: Option<String>,
    pub language: String,
    pub selected_model: String,
}

/// 获取首次初始化配置
#[tauri::command]
pub fn get_init_config(state: tauri::State<'_, crate::db::Db>) -> Result<InitConfig, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    let schema = match get(&conn, KEY_CONFIG_SCHEMA_VERSION).map_err(|e| e.to_string())? {
        Some(ConfigValue::Integer(value)) => value,
        Some(ConfigValue::String(value)) => value.parse::<i64>().unwrap_or(CONFIG_SCHEMA_VERSION),
        _ => CONFIG_SCHEMA_VERSION,
    };
    let initialized_at = match get(&conn, KEY_INITIALIZED_AT).map_err(|e| e.to_string())? {
        Some(ConfigValue::String(value)) if !value.is_empty() => Some(value),
        _ => None,
    };
    let language_fallback = system_language();
    Ok(InitConfig {
        config_schema_version: schema,
        app_version: get_str_or(&conn, KEY_APP_VERSION, "unknown"),
        installed_at: get_str_or(&conn, KEY_INSTALLED_AT, ""),
        initialized_at,
        language: get_str_or(&conn, KEY_LANGUAGE, &language_fallback),
        selected_model: get_str_or(&conn, KEY_SELECTED_MODEL, DEFAULT_MODEL),
    })
}

/// 前端读取配置
#[tauri::command]
pub fn get_config(
    state: tauri::State<'_, crate::db::Db>,
    key: String,
) -> Result<Option<ConfigValue>, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    get(&conn, &key).map_err(|e| e.to_string())
}

/// 前端写入配置
#[tauri::command]
pub fn set_config(
    app: tauri::AppHandle,
    state: tauri::State<'_, crate::db::Db>,
    key: String,
    value: ConfigValue,
) -> Result<(), String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    set(&conn, &key, &value).map_err(|e| e.to_string())?;
    // 全局广播配置变更, 桌宠窗口据此热更新模型/显示参数
    let _ = app.emit(
        "nori:config-changed",
        serde_json::json!({"key": key, "value": value.to_storage()}),
    );
    Ok(())
}

/// 前端删除配置
#[tauri::command]
pub fn delete_config(state: tauri::State<'_, crate::db::Db>, key: String) -> Result<bool, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    delete(&conn, &key).map_err(|e| e.to_string())
}

/// 前端检查配置是否存在
#[tauri::command]
pub fn has_config(state: tauri::State<'_, crate::db::Db>, key: String) -> Result<bool, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    exists(&conn, &key).map_err(|e| e.to_string())
}

/// 前端获取全部配置
#[tauri::command]
pub fn get_all_configs(
    state: tauri::State<'_, crate::db::Db>,
) -> Result<Vec<(String, ConfigValue)>, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    get_all(&conn).map_err(|e| e.to_string())
}
