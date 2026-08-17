use crate::db::Db;
use crate::log;
use tauri::Manager;

/// 退出应用
#[tauri::command]
pub fn exit_app() {
    std::process::exit(0);
}

/// 首次启动完成后由前端调用: 写入标记 + 切换窗口(first-run → init)
/// 仅允许 first-run 窗口调用
#[tauri::command]
pub fn complete_first_run(
    app: tauri::AppHandle,
    webview: tauri::Webview,
    state: tauri::State<'_, Db>,
) -> Result<(), String> {
    if webview.label() != "first-run" {
        let _ = log::write(
            &log::LogSource::Backend,
            "warn",
            &format!("拒绝 complete_first_run: 来源窗口 label={}", webview.label()),
        );
        return Err("只能从首次运行窗口调用complete_first_run".into());
    }
    // 叠加可见性校验: 只有正在显示的 first-run 窗口才能完成初始化
    if let Some(win) = app.get_webview_window("first-run") {
        if !win.is_visible().map_err(|e| e.to_string())? {
            let _ = log::write(&log::LogSource::Backend, "warn", "拒绝 complete_first_run: 首次运行窗口不可见");
            return Err("首次运行窗口不可见".into());
        }
    }
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    crate::db::mark_first_run_completed(&conn).map_err(|e| e.to_string())?;
    // 记录首次初始化完成时间 (幂等)
    crate::config::mark_initialized(&conn).map_err(|e| e.to_string())?;
    drop(conn);
    log::write(&log::LogSource::Backend, "info", "首次初始化完成").map_err(|e| e.to_string())?;
    if let Some(win) = app.get_webview_window("first-run") {
        win.hide().map_err(|e| e.to_string())?;
    }
    if let Some(win) = app.get_webview_window("init") {
        win.show().map_err(|e| e.to_string())?;
        win.set_focus().map_err(|e| e.to_string())?;
    }
    if let Some(pet) = app.get_webview_window("pet") {
        pet.show().map_err(|e| e.to_string())?;
        pet.set_always_on_top(true).map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// 写日志: 前端 invoke("write_log", { level, message })
/// 追加到 data/log/app.log
/// 仅允许 init / first-run 窗口调用, 且 level 必须属于白名单
#[tauri::command]
pub fn write_log(
    webview: tauri::Webview,
    level: String,
    message: String,
) -> Result<(), String> {
    if !matches!(webview.label(), "init" | "first-run") {
        return Err("未知窗口不允许写日志".into());
    }
    if !matches!(level.as_str(), "info" | "warn" | "error") {
        return Err(format!("非法的日志级别: {level}"));
    }
    log::write(&log::LogSource::Frontend, &level, &message).map_err(|e| e.to_string())
}

/// 获取系统语言: 前端 invoke("get_system_language")
/// 返回如 "zh-CN", "en-US" 等
#[tauri::command]
pub fn get_system_language() -> String {
    let lang = sys_locale::get_locale().unwrap_or_else(|| "zh-CN".to_string());
    let _ = log::write(&log::LogSource::Backend, "info", &format!("检测到系统语言: {lang}"));
    lang
}

/// 拉取 OpenAI 协议接口的模型列表: 前端 invoke("fetch_llm_models", { baseUrl, apiKey })
/// 请求 `{baseUrl}/models`, 返回模型 id 列表
#[tauri::command]
pub fn fetch_llm_models(base_url: String, api_key: String) -> Result<Vec<String>, String> {
    let url = format!("{}/models", base_url.trim_end_matches('/'));
    let resp = reqwest::blocking::Client::new()
        .get(&url)
        .bearer_auth(api_key)
        .send()
        .map_err(|e| {
            let _ = log::write(&log::LogSource::Backend, "error", &format!("拉取模型请求失败: {e}"));
            format!("请求失败: {e}")
        })?
        .error_for_status()
        .map_err(|e| {
            let status = e.status().map(|s| s.to_string()).unwrap_or_default();
            let _ = log::write(
                &log::LogSource::Backend,
                "error",
                &format!("拉取模型接口错误 (HTTP {status})"),
            );
            format!("接口返回错误 (HTTP {status}): {e}")
        })?;

    let body: serde_json::Value = match resp.json() {
        Ok(b) => b,
        Err(e) => {
            let _ = log::write(&log::LogSource::Backend, "error", &format!("拉取模型解析响应失败: {e}"));
            return Err(format!("解析响应失败: {e}"));
        }
    };
    let mut ids = Vec::new();
    if let Some(arr) = body.get("data").and_then(|d| d.as_array()) {
        for item in arr {
            match item {
                ::serde_json::Value::String(s) => ids.push(s.clone()),
                obj => {
                    if let Some(id) = obj.get("id").and_then(|id| id.as_str()) {
                        ids.push(id.to_string());
                    }
                }
            }
        }
    }

    if ids.is_empty() {
        let _ = log::write(&log::LogSource::Backend, "warn", "拉取模型: data 为空, 未解析到任何模型");
        return Err("接口返回成功, 但未解析到任何模型数据 (data)".into());
    }
    let _ = log::write(
        &log::LogSource::Backend,
        "info",
        &format!("拉取模型成功, 共 {} 个", ids.len()),
    );
    Ok(ids)
}
