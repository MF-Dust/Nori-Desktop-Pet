mod db;

use db::Db;
use tauri::Manager;

/// 首次启动完成后由前端调用: 写入标记 + 切换窗口(first-run → init)
/// 仅允许 first-run 窗口调用
#[tauri::command]
fn complete_first_run(
    app: tauri::AppHandle,
    webview: tauri::Webview,
    state: tauri::State<'_, Db>,
) -> Result<(), String> {
    if webview.label() != "first-run" {
        return Err("只能从首次运行窗口调用complete_first_run".into());
    }
    // 叠加可见性校验: 只有正在显示的 first-run 窗口才能完成初始化
    if let Some(win) = app.get_webview_window("first-run") {
        if !win.is_visible().map_err(|e| e.to_string())? {
            return Err("首次运行窗口不可见".into());
        }
    }
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    db::mark_first_run_completed(&conn).map_err(|e| e.to_string())?;
    db::write_log(&conn, "info", "首次初始化完成").map_err(|e| e.to_string())?;
    drop(conn);
    if let Some(win) = app.get_webview_window("first-run") {
        win.hide().map_err(|e| e.to_string())?;
    }
    if let Some(win) = app.get_webview_window("init") {
        win.show().map_err(|e| e.to_string())?;
        win.set_focus().map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// 写日志: 前端 invoke("write_log", { level, message })
/// 仅允许 init / first-run 窗口调用, 且 level 必须属于白名单
#[tauri::command]
fn write_log(
    webview: tauri::Webview,
    state: tauri::State<'_, Db>,
    level: String,
    message: String,
) -> Result<(), String> {
    if !matches!(webview.label(), "init" | "first-run") {
        return Err("未知窗口不允许写日志".into());
    }
    if !matches!(level.as_str(), "info" | "warn" | "error") {
        return Err(format!("非法的日志级别: {level}"));
    }
    let conn = state.0.lock().map_err(|e| e.to_string())?;
    db::write_log(&conn, &level, &message).map_err(|e| e.to_string())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            // 初始化数据库 (建库建表)
            let db_handle = db::init(app.handle())?;
            let conn = db_handle.0.lock().map_err(|e| e.to_string())?;
            let first_run = db::is_first_run(&conn)?;
            db::write_log(
                &conn,
                "info",
                if first_run { "首次启动应用" } else { "应用启动完成" },
            )?;
            drop(conn);

            // 控制窗口显隐: 首次启动 → first-run, 否则启动完成 → init
            if first_run {
                if let Some(win) = app.get_webview_window("first-run") {
                    win.show()?;
                }
                if let Some(win) = app.get_webview_window("init") {
                    win.hide()?;
                }
            } else {
                if let Some(win) = app.get_webview_window("init") {
                    win.show()?;
                }
                if let Some(win) = app.get_webview_window("first-run") {
                    win.hide()?;
                }
            }

            app.manage(db_handle);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![complete_first_run, write_log])
        .run(tauri::generate_context!())
        .expect("运行应用时出错")
}
