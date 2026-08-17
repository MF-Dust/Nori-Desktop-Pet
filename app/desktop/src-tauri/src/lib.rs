mod commands;
mod config;
mod db;
mod live2d;
mod log;

use tauri::Manager;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .setup(|app| {
            // 初始化日志 (创建 data/log 目录) 与数据库 (建库建表)
            log::init()?;
            let db_handle = db::init(app.handle())?;
            let conn = db_handle.0.lock().map_err(|e| e.to_string())?;
            let first_run = db::is_first_run(&conn)?;
            drop(conn);
            log::write(
                &log::LogSource::Backend,
                "info",
                if first_run { "首次启动应用" } else { "应用启动完成" },
            )?;

            // 控制窗口显隐: 首次启动 → first-run, 否则启动完成 → init
            if first_run {
                if let Some(win) = app.get_webview_window("first-run") {
                    win.show()?;
                }
                if let Some(win) = app.get_webview_window("init") {
                    win.hide()?;
                }
                log::write(&log::LogSource::Backend, "info", "窗口调度: 显示 first-run, 隐藏 init")?;
            } else {
                if let Some(win) = app.get_webview_window("init") {
                    win.show()?;
                }
                if let Some(win) = app.get_webview_window("first-run") {
                    win.hide()?;
                }
                log::write(&log::LogSource::Backend, "info", "窗口调度: 显示 init, 隐藏 first-run")?;
            }

            app.manage(db_handle);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::complete_first_run,
            commands::write_log,
            commands::get_system_language,
            commands::fetch_llm_models,
            config::get_config,
            config::set_config,
            config::delete_config,
            config::has_config,
            config::get_all_configs
        ])
        .run(tauri::generate_context!())
        .expect("运行应用时出错")
}
