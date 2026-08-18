//! 聊天模块
//!
//! 聊天历史保存在本机 SQLite (nori.db, 与资源下载同属数据目录)
//! 前端通过 `invoke("get_chat_history")` / `invoke("chat_completion", ...)` 调用.

use crate::config::{get_str_or, ConfigValue};
use crate::db::Db;
use crate::log;

use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use tauri::{Emitter, Manager, State};

/// Nori 人格系统提示词 (最新版)
/// 源文件: resources/nori-system-prompt.md (V3.5.2)
const SYSTEM_PROMPT: &str = include_str!("../resources/nori-system-prompt.md");

/// 聊天请求超时 (秒): 防止接口挂起导致后台线程永久阻塞
const CHAT_TIMEOUT_SECS: u64 = 120;

/// 动作标记: [nori_motion:动作名]
const MOTION_MARKER_START: &str = "[nori_motion:";

/// 从回复中提取动作标记, 返回 (剥离标记后的文本, 动作名列表)
fn extract_motion_markers(content: &str) -> (String, Vec<String>) {
    let mut clean = String::new();
    let mut motions = Vec::new();
    let mut rest = content;
    while let Some(start) = rest.find(MOTION_MARKER_START) {
        clean.push_str(&rest[..start]);
        let after = &rest[start + MOTION_MARKER_START.len()..];
        match after.find(']') {
            Some(end) => {
                let name = after[..end].trim();
                if !name.is_empty() {
                    motions.push(name.to_string());
                }
                rest = &after[end + 1..];
            }
            None => {
                clean.push_str(MOTION_MARKER_START);
                rest = after;
            }
        }
    }
    clean.push_str(rest);
    (clean, motions)
}

/// 从配置读取当前模型动作列表, 组装成提示词附录 (无动作时返回空串)
/// 优先读 l2d_motions_<模型id>, 回退全局 l2d_motions
fn motion_hint(conn: &Connection, model_id: &str) -> String {
    let mut groups: Vec<serde_json::Value> = Vec::new();
    let keys: Vec<String> = if model_id.is_empty() {
        vec!["l2d_motions".to_string()]
    } else {
        vec![
            format!("l2d_motions_{model_id}"),
            "l2d_motions".to_string(),
        ]
    };
    for key in keys {
        match crate::config::get(conn, &key) {
            Ok(Some(ConfigValue::Json(serde_json::Value::Array(items)))) => {
                groups = items;
                break;
            }
            _ => {}
        }
    }
    if groups.is_empty() {
        return String::new();
    }
    let mut lines = Vec::new();
    for group in groups {
        let name = group
            .get("group")
            .and_then(|v| v.as_str())
            .unwrap_or_default();
        let names = group
            .get("names")
            .and_then(|v| v.as_array())
            .map(|arr| {
                arr.iter()
                    .filter_map(|n| n.as_str())
                    .collect::<Vec<_>>()
                    .join(", ")
            })
            .unwrap_or_default();
        if !name.is_empty() && !names.is_empty() {
            lines.push(format!("{name}: {names}"));
        }
    }
    if lines.is_empty() {
        return String::new();
    }
    format!(
        "\n\n## 当前可用动作\n需要表达动作时, 在回复末尾另起一行附加标记 [nori_motion:动作名], 每行一个, 最多一个, 动作名从下面选择, 没有合适的就不加:\n{}",
        lines.join("\n")
    )
}

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
pub async fn chat_completion(
    app: tauri::AppHandle,
    base_url: String,
    api_key: String,
    model: String,
    messages: Vec<ChatMessageInput>,
) -> Result<String, String> {
    // 校验
    let base_url = base_url.trim_end_matches('/').to_string();
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
    // 阻塞的 HTTP 请求 + DB 操作放到后台线程执行,
    // 否则会卡死主线程导致所有窗口未响应
    tauri::async_runtime::spawn_blocking(move || {
        let db = app.state::<Db>();
        // 系统提示词 = 人格 + 当前模型动作列表附录
        let system_content = {
            let conn = db.0.lock().map_err(|e| e.to_string())?;
            let model_id = get_str_or(&conn, "selected_model", "");
            format!("{}{}", SYSTEM_PROMPT, motion_hint(&conn, &model_id))
        };
        let payload = serde_json::json!({
            "model": model,
            "messages": std::iter::once(serde_json::json!({"role": "system", "content": system_content}))
                .chain(
                    messages
                        .iter()
                        .map(|m| serde_json::json!({"role": m.role, "content": m.content})),
                )
                .collect::<Vec<_>>(),
        });
        let url = format!("{base_url}/chat/completions");
        let response = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(CHAT_TIMEOUT_SECS))
            .build()
            .map_err(|error| {
                let _ = log::write(
                    &app,
                    &log::LogSource::Backend,
                    "error",
                    &format!("创建聊天客户端失败: {error}"),
                );
                format!("创建客户端失败: {error}")
            })?
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
        // 解析动作标记: 剥离标记并广播给桌宠窗口播放
        let (clean_content, motions) = extract_motion_markers(&content);
        for name in &motions {
            let _ = app.emit("nori:play-motion", serde_json::json!({"name": name}));
        }
        if !motions.is_empty() {
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "info",
                &format!("AI 触发动作: {}", motions.join(", ")),
            );
        }
        let content = clean_content;
        // 写入历史: 仅保存最后一条输入 (前端传来的新消息) 与回复, 避免重复落库
        {
            let conn = db.0.lock().map_err(|e| e.to_string())?;
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
    })
    .await
    .map_err(|error| format!("后台任务失败: {error}"))?
}
