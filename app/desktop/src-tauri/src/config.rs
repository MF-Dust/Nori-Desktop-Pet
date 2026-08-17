use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::error::Error as StdError;

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
        Err(e) => Err(e.into()),
    }
}

/// 写入配置项 (不存在则插入, 存在则更新)
pub fn set(conn: &Connection, key: &str, value: &ConfigValue) -> ConfigResult<()> {
    conn.execute(
        "INSERT OR REPLACE INTO config (key, value) VALUES (?1, ?2)",
        params![key, value.to_storage()],
    )?;
    Ok(())
}

/// 删除配置项
pub fn delete(conn: &Connection, key: &str) -> ConfigResult<bool> {
    let count = conn.execute("DELETE FROM config WHERE key = ?1", params![key])?;
    Ok(count > 0)
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
