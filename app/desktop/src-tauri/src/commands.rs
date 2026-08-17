use crate::db::Db;
use crate::log;
use tauri::Manager;
use tauri::Emitter;

/// 资源下载进度事件载荷: 通过全局事件 `resource-download` 推送给前端.
/// 不绑定具体资源类型, 通用地表达检查/下载/解压流程.
#[derive(serde::Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct ResourceDownloadEvent {
    /// 资源类型 (如 "live2d", 与 config / 目录 / 未来扩展对应)
    resource_type: String,
    /// 阶段: installed | downloading | download-done | extracting | done | error
    step: String,
    /// 下载进度百分比 (0-100), downloading 阶段有值
    progress: Option<f32>,
    /// 已下载字节数, downloading 阶段有值 (供前端按大小计算真实进度)
    downloaded: Option<u64>,
    /// 总字节数, downloading 阶段有值 (Content-Length 缺失时为 None)
    total: Option<u64>,
    /// 错误信息, error 阶段有值
    message: Option<String>,
}

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

/// 解析资源类型字符串, 未知类型返回 None
fn parse_resource_type(s: &str) -> Option<crate::resource::types::ResourceType> {
    crate::resource::types::ResourceType::from_str(s)
}

/// 消毒资源名称: 仅保留文件名部分, 防止 `../` 或其他路径片段带进资源目录
fn sanitize_name(raw: &str) -> Result<String, String> {
    let name = std::path::Path::new(raw)
        .file_name()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    if name.is_empty() {
        Err("非法的资源名".into())
    } else {
        Ok(name)
    }
}

/// 检查指定类型的资源是否已安装
/// 前端 invoke("check_resource", { resourceType, name })
#[tauri::command]
pub fn check_resource(
    app: tauri::AppHandle,
    resource_type: String,
    name: String,
) -> Result<bool, String> {
    let rt = parse_resource_type(&resource_type).ok_or_else(|| format!("未知的资源类型: {resource_type}"))?;
    let name = sanitize_name(&name)?;
    let data_dir = crate::db::data_dir(&app).map_err(|e| e.to_string())?;

    let installed = crate::resource::is_installed(&data_dir, &rt, &name);
    let _ = log::write(
        &log::LogSource::Backend,
        "info",
        &format!("检查资源: type={resource_type} name={name} installed={installed}"),
    );
    Ok(installed)
}

/// 确保指定类型的资源就位: 已安装则直接返回, 否则下载并解压.
/// 整个过程通过全局事件 `resource-download` 推送给前端:
///   downloading{progress} → download-done → extracting → done | installed | error
/// 前端 invoke("ensure_resource", { resourceType, name }) 后订阅该事件即可拿到实时进度.
#[tauri::command]
pub fn ensure_resource(
    app: tauri::AppHandle,
    resource_type: String,
    name: String,
) -> Result<(), String> {
    let rt = parse_resource_type(&resource_type).ok_or_else(|| format!("未知的资源类型: {resource_type}"))?;
    let name = sanitize_name(&name)?;
    let data_dir = crate::db::data_dir(&app).map_err(|e| e.to_string())?;

    // 发送指定阶段事件 (不阻塞). 下载阶段携带真实字节进度供前端计算进度条.
    let emit = |step: &str,
                progress: Option<f32>,
                downloaded: Option<u64>,
                total: Option<u64>,
                message: Option<&str>| {
        let _ = app.emit(
            "resource-download",
            ResourceDownloadEvent {
                resource_type: resource_type.clone(),
                step: step.to_string(),
                progress,
                downloaded,
                total,
                message: message.map(|s| s.to_string()),
            },
        );
    };

    // 已安装: 直接通知并返回
    if crate::resource::is_installed(&data_dir, &rt, &name) {
        let _ = log::write(
            &log::LogSource::Backend,
            "info",
            &format!("资源已安装, 无需下载: type={resource_type} name={name}"),
        );
        emit("installed", Some(100.0), None, None, None);
        return Ok(());
    }

    let _ = log::write(
        &log::LogSource::Backend,
        "info",
        &format!("开始下载资源: type={resource_type} name={name}"),
    );
    emit("downloading", Some(0.0), Some(0), None, None);

    // 下载 zip (进度回调 → downloading 事件, 携带真实字节)
    let progress_cb = |p: crate::resource::types::DownloadProgress| {
        emit(
            "downloading",
            Some(p.percentage),
            Some(p.downloaded),
            (p.total > 0).then_some(p.total),
            None,
        );
    };
    let zip_path = crate::resource::downloader::download_to_zip(&rt, &name, &data_dir, progress_cb)
        .map_err(|e| {
            emit("error", None, None, None, Some(&e.to_string()));
            format!("下载资源失败: {e}")
        })?;

    // 下载完成 → 解压 (前端负责让文案停留)
    emit("download-done", Some(100.0), None, None, None);
    emit("extracting", None, None, None, None);

    let target_dir = data_dir.join(rt.dir_name()).join(&name);
    crate::resource::downloader::extract_zip(&zip_path, &target_dir).map_err(|e| {
        emit("error", None, None, None, Some(&e.to_string()));
        let _ = log::write(
            &log::LogSource::Backend,
            "error",
            &format!("解压资源失败: type={resource_type} name={name} err={e}"),
        );
        format!("解压资源失败: {e}")
    })?;

    // 清理临时文件
    let _ = std::fs::remove_file(&zip_path);

    emit("done", Some(100.0), None, None, None);
    let _ = log::write(
        &log::LogSource::Backend,
        "info",
        &format!("资源就位: type={resource_type} name={name}"),
    );
    Ok(())
}
