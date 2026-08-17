use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::error::Error as StdError;

use crate::log::{self, LogSource};

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
    /// 转为数据库存储的字符串
    pub fn to_storage(&self) -> String {
        match self {
            ConfigValue::String(s) => s.clone(),
            ConfigValue::Integer(i) => i.to_string(),
            ConfigValue::Boolean(b) => if *b { "1" } else { "0" }.to_string(),
            ConfigValue::Json(v) => v.to_string(),
        }
    }

    /// 从数据库存储的字符串解析
    pub fn from_storage(s: &str) -> ConfigResult<Self> {
        // 尝试解析为 JSON（包括数字、布尔、对象、数组）
        if let Ok(v) = serde_json::from_str::<Value>(s) {
            return Ok(ConfigValue::Json(v));
        }
        // 尝试解析为布尔
        if s == "1" || s.eq_ignore_ascii_case("true") {
            return Ok(ConfigValue::Boolean(true));
        }
        if s == "0" || s.eq_ignore_ascii_case("false") {
            return Ok(ConfigValue::Boolean(false));
        }
        // 尝试解析为整数
        if let Ok(i) = s.parse::<i64>() {
            return Ok(ConfigValue::Integer(i));
        }
        // 默认作为字符串
        Ok(ConfigValue::String(s.to_string()))
    }
}

/// 读取配置项
pub fn get(conn: &Connection, key: &str) -> ConfigResult<Option<ConfigValue>> {
    let result = conn.query_row(
        "SELECT value FROM config WHERE key = ?1",
        params![key],
        |row| row.get::<_, String>(0),
    );

    match result {
        Ok(s) => Ok(Some(ConfigValue::from_storage(&s)?)),
        Err(rusqlite::Error::QueryReturnedNoRows) => Ok(None),
        Err(e) => {
            let _ = log::write(&LogSource::Backend, "error", &format!("读取配置失败 key={key}: {e}"));
            Err(e.into())
        }
    }
}

/// 写入配置项 (不存在则插入, 存在则更新)
/// 日志只记录 key, 不记录 value 明文 (避免 api key 等敏感值泄露)
pub fn set(conn: &Connection, key: &str, value: &ConfigValue) -> ConfigResult<()> {
    conn.execute(
        "INSERT OR REPLACE INTO config (key, value) VALUES (?1, ?2)",
        params![key, value.to_storage()],
    )?;
    let _ = log::write(&LogSource::Backend, "info", &format!("写入配置 key={key}"));
    Ok(())
}

/// 删除配置项
pub fn delete(conn: &Connection, key: &str) -> ConfigResult<bool> {
    let count = conn.execute("DELETE FROM config WHERE key = ?1", params![key])?;
    let removed = count > 0;
    let _ = log::write(
        &LogSource::Backend,
        "info",
        &format!("删除配置 key={key} removed={removed}"),
    );
    Ok(removed)
}

/// 检查配置项是否存在
pub fn exists(conn: &Connection, key: &str) -> ConfigResult<bool> {
    let count: i64 = conn.query_row(
        "SELECT COUNT(*) FROM config WHERE key = ?1",
        params![key],
        |row| row.get(0),
    )?;
    Ok(count > 0)
}

/// 获取所有配置项 (用于调试或导出)
pub fn get_all(conn: &Connection) -> ConfigResult<Vec<(String, ConfigValue)>> {
    let mut stmt = conn.prepare("SELECT key, value FROM config")?;
    let rows = stmt.query_map([], |row| {
        let key = row.get::<_, String>(0)?;
        let value_str = row.get::<_, String>(1)?;
        let value = ConfigValue::from_storage(&value_str).unwrap_or(ConfigValue::String(value_str));
        Ok((key, value))
    })?;

    let mut result = Vec::new();
    for row in rows {
        result.push(row?);
    }
    Ok(result)
}

// ============ Tauri Commands ============

/// 前端调用: 读取配置
/// invoke("get_config", { key: "some_key" })
#[tauri::command]
pub fn get_config(
    state: tauri::State<'_, crate::db::Db>,
    key: String,
) -> Result<Option<ConfigValue>, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    get(&conn, &key).map_err(|e| e.to_string())
}

/// 前端调用: 写入配置
/// invoke("set_config", { key: "some_key", value: "some_value" })
#[tauri::command]
pub fn set_config(
    state: tauri::State<'_, crate::db::Db>,
    key: String,
    value: ConfigValue,
) -> Result<(), String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    set(&conn, &key, &value).map_err(|e| e.to_string())
}

/// 前端调用: 删除配置
/// invoke("delete_config", { key: "some_key" })
#[tauri::command]
pub fn delete_config(
    state: tauri::State<'_, crate::db::Db>,
    key: String,
) -> Result<bool, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    delete(&conn, &key).map_err(|e| e.to_string())
}

/// 前端调用: 检查配置是否存在
/// invoke("has_config", { key: "some_key" })
#[tauri::command]
pub fn has_config(
    state: tauri::State<'_, crate::db::Db>,
    key: String,
) -> Result<bool, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    exists(&conn, &key).map_err(|e| e.to_string())
}

/// 前端调用: 获取所有配置 (调试用)
/// invoke("get_all_configs")
#[tauri::command]
pub fn get_all_configs(
    state: tauri::State<'_, crate::db::Db>,
) -> Result<Vec<(String, ConfigValue)>, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    get_all(&conn).map_err(|e| e.to_string())
}

// ============ 默认配置 / 首次初始化 ============

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

/// 读取字符串配置, 缺失/类型不符时返回 fallback
fn get_str_or(conn: &Connection, key: &str, fallback: &str) -> String {
    match get(conn, key) {
        Ok(Some(ConfigValue::String(s))) if !s.is_empty() => s,
        Ok(Some(ConfigValue::Integer(i))) => i.to_string(),
        Ok(Some(ConfigValue::Boolean(b))) => b.to_string(),
        _ => fallback.to_string(),
    }
}

/// 首次加载时初始化默认配置 (幂等): 只补写缺失的键, 绝不覆盖用户已有配置.
/// 数据库建表后调用一次即可.
pub fn init_defaults(conn: &Connection) -> ConfigResult<()> {
    let defaults: &[(&str, ConfigValue)] = &[
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
    ];
    let mut inserted = 0;
    for (key, value) in defaults {
        inserted += conn.execute(
            "INSERT OR IGNORE INTO config (key, value) VALUES (?1, ?2)",
            params![key, value.to_storage()],
        )?;
    }
    let _ = log::write(
        &LogSource::Backend,
        "info",
        &format!("初始化默认配置: 补写 {inserted} 个缺失键"),
    );
    Ok(())
}

/// 校验配置结构版本, 为未来迁移预留:
/// - 低于当前版本: 未来在此按版本号逐级迁移 (当前 v1 无历史版本)
/// - 高于当前版本: 数据库来自更高版本应用, 记录警告并提示升级
pub fn ensure_schema_version(conn: &Connection) -> ConfigResult<()> {
    let current = match get(conn, KEY_CONFIG_SCHEMA_VERSION)? {
        Some(ConfigValue::Integer(v)) => v,
        Some(ConfigValue::String(s)) => s.parse::<i64>().unwrap_or(CONFIG_SCHEMA_VERSION),
        _ => CONFIG_SCHEMA_VERSION,
    };
    if current > CONFIG_SCHEMA_VERSION {
        let _ = log::write(
            &LogSource::Backend,
            "warn",
            &format!(
                "配置结构版本 {current} 高于当前应用支持的 {CONFIG_SCHEMA_VERSION}, 请升级应用"
            ),
        );
    } else if current < CONFIG_SCHEMA_VERSION {
        // 未来迁移入口: 例如 v1 → v2 在此逐级执行
        let _ = log::write(
            &LogSource::Backend,
            "info",
            &format!(
                "配置结构版本 {current} → {CONFIG_SCHEMA_VERSION}, 当前版本无历史迁移"
            ),
        );
    }
    Ok(())
}

/// 记录首次初始化完成时间 (幂等, 由 complete_first_run 调用)
pub fn mark_initialized(conn: &Connection) -> ConfigResult<()> {
    if !exists(conn, KEY_INITIALIZED_AT)? {
        set(conn, KEY_INITIALIZED_AT, &ConfigValue::String(now()))?;
    }
    Ok(())
}

/// 首次初始化配置快照, 供前端首次加载时一次性读取
/// 字段序列化为 camelCase (configSchemaVersion / appVersion 等), 与前端 TS 接口对齐
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

/// 前端调用: 获取首次初始化配置快照 (语言/模型/版本/时间)
/// invoke("get_init_config")
#[tauri::command]
pub fn get_init_config(state: tauri::State<'_, crate::db::Db>) -> Result<InitConfig, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;

    let schema = match get(&conn, KEY_CONFIG_SCHEMA_VERSION).map_err(|e| e.to_string())? {
        Some(ConfigValue::Integer(v)) => v,
        Some(ConfigValue::String(s)) => s.parse::<i64>().unwrap_or(CONFIG_SCHEMA_VERSION),
        _ => CONFIG_SCHEMA_VERSION,
    };
    let initialized_at = match get(&conn, KEY_INITIALIZED_AT).map_err(|e| e.to_string())? {
        Some(ConfigValue::String(s)) if !s.is_empty() => Some(s),
        _ => None,
    };

    Ok(InitConfig {
        config_schema_version: schema,
        app_version: get_str_or(&conn, KEY_APP_VERSION, "unknown"),
        installed_at: get_str_or(&conn, KEY_INSTALLED_AT, ""),
        initialized_at,
        language: get_str_or(&conn, KEY_LANGUAGE, &system_language()),
        selected_model: get_str_or(&conn, KEY_SELECTED_MODEL, DEFAULT_MODEL),
    })
}
