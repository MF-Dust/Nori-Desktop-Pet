//! 聊天模块
//!
//! 聊天历史保存在本机 SQLite (nori.db, 与资源下载同属数据目录)
//! 前端通过 `invoke("get_chat_history")` / `invoke("chat_completion", ...)` 调用.

use crate::db::Db;
use crate::log;

use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use tauri::State;

/// Nori 人格系统提示词 (最新版)
/// 源文件: resources/nori-system-prompt.md (V3.5.2)
const SYSTEM_PROMPT: &str = include_str!("../resources/nori-system-prompt.md");

/// 聊天消息 (输入)
/// 前端: {role: "user" | "assistant", content: "..."}
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChatMessageInput {
    /// 角色: user / assistant
    pub role: String,
    /// 消息内容
    pub content: String,
}

/// 聊天消息 (存储 / 输出)
/// 前端: {id, role, content, createdAt}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ChatMessage {
    /// 自增 id (即时间顺序)
    pub id: i64,
    /// 角色
    pub role: String,
    /// 内容
    pub content: String,
    /// 创建时间 (RFC3339)
    pub created_at: String,
}

/// 保存一条聊天消息
fn save_message(conn: &Connection, role: &str, content: &str) -> Result<(), String> {
    conn.execute(
        "INSERT INTO chat_messages (role, content, created_at) VALUES (?1, ?2, ?3)",
        params![role, content, chrono::Utc::now().to_rfc3339()],
    )
    .map_err(|e| e.to_string())?;
    Ok(())
}

/// 获取完整聊天历史 (按时间正序, 永不清除)
/// invoke("get_chat_history")
#[tauri::command]
pub fn get_chat_history(state: State<'_, Db>) -> Result<Vec<ChatMessage>, String> {
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    let mut stmt = conn
        .prepare(
            "SELECT id, role, content, created_at FROM chat_messages ORDER BY id ASC",
        )
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |row| {
            Ok(ChatMessage {
                id: row.get(0)?,
                role: row.get(1)?,
                content: row.get(2)?,
                created_at: row.get(3)?,
            })
        })
        .map_err(|e| e.to_string())?;
    rows.collect::<Result<Vec<_>, _>>().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn chat_completion(
    app: tauri::AppHandle,
    state: State<'_, Db>,
    base_url: String,
    api_key: String,
    model: String,
    messages: Vec<ChatMessageInput>,
) -> Result<String, String> {
    // 校验
    let base_url = base_url.trim_end_matches('/');
    if base_url.is_empty() {
        return Err("Base URL 不能为空".to_string());
    }
    if api_key.is_empty() {
        return Err("API Key 不能为空".to_string());
    }
    if model.is_empty() {
        return Err("模型不能为空".to_string());
    }
    if messages.is_empty() {
        return Err("消息不能为空".to_string());
    }
    let payload = serde_json::json!({
        "model": model,
        "messages": std::iter::once(serde_json::json!({"role": "system", "content": SYSTEM_PROMPT}))
            .chain(
                messages
                    .iter()
                    .map(|m| serde_json::json!({"role": m.role, "content": m.content})),
            )
            .collect::<Vec<_>>(),
    });
    let url = format!("{base_url}/chat/completions");
    let response = reqwest::blocking::Client::new()
        .post(&url)
        .bearer_auth(&api_key)
        .json(&payload)
        .send()
        .map_err(|error| {
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "error",
                &format!("聊天请求失败: {error}"),
            );
            format!("请求失败: {error}")
        })?;
    let status = response.status();
    if !status.is_success() {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "error",
            &format!("聊天接口错误: HTTP {status}"),
        );
        return Err(format!("接口返回错误: HTTP {status}"));
    }
    let body: serde_json::Value = response.json().map_err(|error| {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "error",
            &format!("解析聊天响应失败: {error}"),
        );
        format!("解析响应失败: {error}")
    })?;
    let content = body["choices"][0]["message"]["content"]
        .as_str()
        .ok_or_else(|| {
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "warn",
                "聊天响应缺少 choices[0].message.content",
            );
            "接口响应格式异常".to_string()
        })?
        .to_string();
    // 写入历史: 仅保存最后一条输入 (前端传来的新消息) 与回复, 避免重复落库
    {
        let conn = state.0.lock().map_err(|e| e.to_string())?;
        if let Some(last) = messages.last() {
            save_message(&conn, &last.role, &last.content)?;
        }
        save_message(&conn, "assistant", &content)?;
    }
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!(
            "聊天完成: model={model} 消息数={} 回复长度={}",
            messages.len(),
            content.len()
        ),
    );
    Ok(content)
}
