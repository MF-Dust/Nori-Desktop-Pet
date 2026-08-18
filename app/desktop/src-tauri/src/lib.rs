mod asset;
mod commands;
mod config;
mod db;
mod log;
mod resource;
mod tray;

use tauri::Manager;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        // 资源文件通道: 通过 `asset://` / `http://asset.localhost` 把 `data` 目录
        .register_uri_scheme_protocol(asset::SCHEME, |ctx, request| asset::handle(&ctx, request))
        // 插件: 打开文件
        .plugin(tauri_plugin_opener::init())
        // 插件: 粘贴板管理
        .plugin(tauri_plugin_clipboard_manager::init())
        .setup(|app| {
            let app_handle = app.handle();
            // 初始化托盘
            tray::init(app_handle).map_err(|e| -> Box<dyn std::error::Error> { e.into() })?;
            // 初始化日志
            log::init(app_handle)?;
            log::write(
                app_handle,
                &log::LogSource::Backend,
                "info",
                "日志系统初始化完成",
            )?;
            // 初始化数据库
            let db_handle = db::init(app_handle)?;
            // 初始化资源目录 (资源和下载临时目录)
            resource::init(app_handle).map_err(|e| -> Box<dyn std::error::Error> { e.into() })?;
            let _ = log::write(
                app_handle,
                &log::LogSource::Backend,
                "info",
                "资源目录初始化完成",
            )?;
            // 判断是否第一次运行
            let first_run = {
                let conn = db_handle.0.lock().map_err(|e| e.to_string())?;
                config::is_first_run(&conn)?
            };
            log::write(
                app_handle,
                &log::LogSource::Backend,
                "info",
                if first_run {
                    "首次启动应用"
                } else {
                    "应用启动完成"
                },
            )?;
            // 窗口调度
            if first_run {
                // 首次启动
                if let Some(win) = app.get_webview_window("first-run") {
                    win.show()?;
                    log::write(
                        app_handle,
                        &log::LogSource::Backend,
                        "info",
                        "窗口调度: 显示 first-run",
                    )?;
                }
                if let Some(win) = app.get_webview_window("init") {
                    win.hide()?;
                    log::write(
                        app_handle,
                        &log::LogSource::Backend,
                        "info",
                        "窗口调度: 隐藏 init",
                    )?;
                }
            } else {
                // 正常启动
                if let Some(win) = app.get_webview_window("init") {
                    win.show()?;
                    log::write(
                        app_handle,
                        &log::LogSource::Backend,
                        "info",
                        "窗口调度: 显示 init",
                    )?;
                }
                if let Some(win) = app.get_webview_window("first-run") {
                    win.hide()?;
                    log::write(
                        app_handle,
                        &log::LogSource::Backend,
                        "info",
                        "窗口调度: 隐藏 first-run",
                    )?;
                }
            }
            app.manage(db_handle);
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::exit_app,
            commands::complete_first_run,
            commands::write_log,
            commands::get_system_language,
            commands::fetch_llm_models,
            config::get_config,
            config::set_config,
            config::delete_config,
            config::has_config,
            config::get_all_configs,
            config::get_init_config,
            commands::check_resource,
            commands::ensure_resource
        ])
        .run(tauri::generate_context!())
        .expect("运行应用时出错")
}
