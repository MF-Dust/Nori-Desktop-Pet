//! 应用命令模块
//!
//! 前端通过 `invoke("<command>")` 调用这里的 #[tauri::command] 函数。
//! 命令清单与 lib.rs 中 `invoke_handler` 的注册保持同步。

use crate::db::Db;
use crate::log;
use crate::resource::{DownloadProgress, ResourceType};

use tauri::{Emitter, Manager};

/// 资源下载进度事件
/// 前端: listen("resource-download", ...)
#[derive(Debug, Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ResourceDownloadEvent {
    /// 资源类型
    pub resource_type: String,
    /// 当前阶段:
    /// - installed
    /// - downloading
    /// - download-done
    /// - extracting
    /// - done
    /// - error
    pub step: String,
    /// 下载百分比
    pub progress: Option<f32>,
    /// 已下载字节数
    pub downloaded: Option<u64>,
    /// 文件总大小
    pub total: Option<u64>,
    /// 错误信息
    pub message: Option<String>,
}

/// 发送资源下载进度事件
fn emit_resource_event(
    app: &tauri::AppHandle,
    resource_type: &str,
    step: &str,
    progress: Option<f32>,
    downloaded: Option<u64>,
    total: Option<u64>,
    message: Option<String>,
) {
    let event = ResourceDownloadEvent {
        resource_type: resource_type.to_string(),
        step: step.to_string(),
        progress,
        downloaded,
        total,
        message,
    };
    if let Err(error) = app.emit("resource-download", event) {
        let _ = log::write(
            app,
            &log::LogSource::Backend,
            "warn",
            &format!("发送资源事件失败: {error}"),
        );
    }
}

/// 退出应用
#[tauri::command]
pub fn exit_app() {
    std::process::exit(0);
}

/// 首次启动完成
/// 只有 first-run 窗口允许调用
#[tauri::command]
pub fn complete_first_run(
    app: tauri::AppHandle,
    webview: tauri::Webview,
    state: tauri::State<'_, Db>,
) -> Result<(), String> {
    // 校验调用来源
    if webview.label() != "first-run" {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "warn",
            &format!(
                "拒绝 complete_first_run: 来源窗口 label={}",
                webview.label()
            ),
        );
        return Err("只能从首次运行窗口调用 complete_first_run".to_string());
    }
    // 校验 first-run 窗口可见
    if let Some(window) = app.get_webview_window("first-run") {
        let visible = window.is_visible().map_err(|e| e.to_string())?;
        if !visible {
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "warn",
                "拒绝 complete_first_run: 首次运行窗口不可见",
            );
            return Err("首次运行窗口不可见".to_string());
        }
    }
    // 更新数据库
    {
        let conn = state.0.lock().map_err(|e| e.to_string())?;
        crate::config::mark_first_run_completed(&conn).map_err(|e| e.to_string())?;
        crate::config::mark_initialized(&conn).map_err(|e| e.to_string())?;
    }
    let _ = log::write(&app, &log::LogSource::Backend, "info", "首次初始化完成");
    // 切换窗口: 关闭首次运行窗口, 显示 init 中转窗口 (下载模型) 与桌宠窗口
    if let Some(window) = app.get_webview_window("first-run") {
        window.close().map_err(|e| e.to_string())?;
    }
    if let Some(window) = app.get_webview_window("init") {
        window.show().map_err(|e| e.to_string())?;
        window.set_focus().map_err(|e| e.to_string())?;
    }
    // 通知 init 窗口 (首次运行路径下为隐藏启动) 开始初始化流程
    let _ = app.emit("nori:init-start", ());
    Ok(())
}

/// 前端写日志
/// invoke("write_log", {
///   level: "info",
///   message: "xxx"
/// })
#[tauri::command]
pub fn write_log(app: tauri::AppHandle, level: String, message: String) -> Result<(), String> {
    log::write(&app, &log::LogSource::Frontend, &level, &message).map_err(|e| e.to_string())
}

/// 获取系统语言
#[tauri::command]
pub fn get_system_language(app: tauri::AppHandle) -> String {
    let language = sys_locale::get_locale().unwrap_or_else(|| "zh-CN".to_string());
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!("检测到系统语言: {language}"),
    );
    language
}

/// 拉取 OpenAI-compatible /models
/// 内部使用 reqwest::blocking, Tauri 命令在独立线程执行, 不会阻塞 UI.
/// invoke("fetch_llm_models", {
///   baseUrl: "https://api.openai.com/v1",
///   apiKey: "sk-..."
/// })
#[tauri::command]
pub fn fetch_llm_models(
    app: tauri::AppHandle,
    base_url: String,
    api_key: String,
) -> Result<Vec<String>, String> {
    let base_url = base_url.trim_end_matches('/');
    if base_url.is_empty() {
        return Err("Base URL 不能为空".to_string());
    }
    let url = format!("{base_url}/models");
    let response = reqwest::blocking::Client::new()
        .get(&url)
        .bearer_auth(api_key)
        .send()
        .map_err(|error| {
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "error",
                &format!("拉取模型请求失败: {error}"),
            );
            format!("请求失败: {error}")
        })?;
    let status = response.status();
    if !status.is_success() {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "error",
            &format!("拉取模型接口错误: HTTP {status}"),
        );
        return Err(format!("接口返回错误: HTTP {status}"));
    }
    let body: serde_json::Value = response.json().map_err(|error| {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "error",
            &format!("拉取模型解析响应失败: {error}"),
        );
        format!("解析响应失败: {error}")
    })?;
    let data = body
        .get("data")
        .and_then(|value| value.as_array())
        .ok_or_else(|| "接口返回成功，但缺少 data 字段".to_string())?;
    let mut models = Vec::with_capacity(data.len());
    for item in data {
        if let Some(id) = item.as_str() {
            models.push(id.to_string());
            continue;
        }
        if let Some(id) = item.get("id").and_then(|value| value.as_str()) {
            models.push(id.to_string());
        }
    }
    models.sort();
    models.dedup();
    if models.is_empty() {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "warn",
            "拉取模型成功，但 data 中没有有效模型",
        );
        return Err("接口返回成功，但没有解析到任何模型".to_string());
    }
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!("拉取模型成功，共 {} 个", models.len()),
    );
    Ok(models)
}

/// 解析资源类型
fn parse_resource_type(value: &str) -> Result<ResourceType, String> {
    ResourceType::from_str(value).ok_or_else(|| format!("未知的资源类型: {value}"))
}

/// 检查资源名称
fn validate_resource_name(name: &str) -> Result<&str, String> {
    let name = name.trim();
    if name.is_empty() {
        return Err("资源名称不能为空".to_string());
    }
    crate::resource::validate_resource_name(name)?;
    Ok(name)
}

/// 检查资源是否已安装
/// invoke("check_resource", {
///   resourceType: "live2d",
///   name: "arg-nori"
/// })
#[tauri::command]
pub fn check_resource(
    app: tauri::AppHandle,
    resource_type: String,
    name: String,
) -> Result<bool, String> {
    let resource_type = parse_resource_type(&resource_type)?;
    let name = validate_resource_name(&name)?;
    let installed = crate::resource::is_installed(&app, resource_type, name)?;
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!(
            "检查资源: type={} name={} installed={installed}",
            resource_type.as_str(),
            name
        ),
    );
    Ok(installed)
}

/// 确保资源已经安装
/// 未安装时执行: 获取下载地址 → 下载 ZIP → 安全解压 → 校验
/// 每个阶段通过 resource-download 事件实时推送给前端.
/// invoke("ensure_resource", {
///   resourceType: "live2d",
///   name: "arg-nori"
/// })
#[tauri::command]
pub fn ensure_resource(
    app: tauri::AppHandle,
    resource_type: String,
    name: String,
) -> Result<(), String> {
    let resource_type = parse_resource_type(&resource_type)?;
    let name = validate_resource_name(&name)?;
    let type_name = resource_type.as_str().to_string();

    // 已安装
    if crate::resource::is_installed(&app, resource_type, name)? {
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "info",
            &format!("资源已安装: type={type_name} name={name}"),
        );
        emit_resource_event(&app, &type_name, "installed", Some(100.0), None, None, None);
        return Ok(());
    }

    let data_dir = crate::db::data_dir(&app).map_err(|e| e.to_string())?;

    // 开始下载
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!("开始下载资源: type={type_name} name={name}"),
    );
    emit_resource_event(
        &app,
        &type_name,
        "downloading",
        Some(0.0),
        Some(0),
        None,
        None,
    );

    // 下载 ZIP
    let progress_app = app.clone();
    let progress_type = type_name.clone();
    let progress_callback = move |progress: DownloadProgress| {
        emit_resource_event(
            &progress_app,
            &progress_type,
            "downloading",
            progress.percentage,
            Some(progress.downloaded),
            progress.total,
            None,
        );
    };
    let zip_path = match crate::resource::downloader::download_to_zip(
        &resource_type,
        name,
        &data_dir,
        progress_callback,
    ) {
        Ok(path) => path,
        Err(error) => {
            let message = error.to_string();
            let _ = log::write(
                &app,
                &log::LogSource::Backend,
                "error",
                &format!("下载资源失败: type={type_name} name={name} error={message}"),
            );
            emit_resource_event(
                &app,
                &type_name,
                "error",
                None,
                None,
                None,
                Some(message.clone()),
            );
            return Err(format!("下载资源失败: {message}"));
        }
    };

    // 下载完成
    emit_resource_event(
        &app,
        &type_name,
        "download-done",
        Some(100.0),
        None,
        None,
        None,
    );

    // 解压
    emit_resource_event(&app, &type_name, "extracting", None, None, None, None);
    let target_dir = data_dir
        .join(crate::resource::RESOURCES_DIR)
        .join(resource_type.dir_name())
        .join(name);
    // 清理可能残留的旧资源
    if target_dir.exists() {
        std::fs::remove_dir_all(&target_dir).map_err(|e| format!("清理旧资源失败: {e}"))?;
    }
    std::fs::create_dir_all(&target_dir).map_err(|e| format!("创建资源目录失败: {e}"))?;
    if let Err(error) = crate::resource::downloader::extract_zip(&zip_path, &target_dir) {
        let _ = std::fs::remove_dir_all(&target_dir);
        let message = error.to_string();
        let _ = log::write(
            &app,
            &log::LogSource::Backend,
            "error",
            &format!("解压资源失败: type={type_name} name={name} error={message}"),
        );
        emit_resource_event(
            &app,
            &type_name,
            "error",
            None,
            None,
            None,
            Some(message.clone()),
        );
        return Err(format!("解压资源失败: {message}"));
    }
    // 删除临时 ZIP
    let _ = std::fs::remove_file(&zip_path);

    // 校验安装结果 (例如 Live2D 模型必须包含 .model3.json)
    if !crate::resource::is_installed(&app, resource_type, name)? {
        let message = format!("资源解压后校验失败: type={type_name} name={name}");
        let _ = log::write(&app, &log::LogSource::Backend, "error", &message);
        emit_resource_event(
            &app,
            &type_name,
            "error",
            None,
            None,
            None,
            Some(message.clone()),
        );
        return Err(message);
    }

    // 完成
    emit_resource_event(&app, &type_name, "done", Some(100.0), None, None, None);
    let _ = log::write(
        &app,
        &log::LogSource::Backend,
        "info",
        &format!(
            "资源就位: type={type_name} name={name} path={}",
            target_dir.display()
        ),
    );
    Ok(())
}
