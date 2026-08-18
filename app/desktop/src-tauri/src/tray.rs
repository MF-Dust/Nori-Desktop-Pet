//! 托盘菜单模块

use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Manager,
};

use crate::log;

/// 菜单项 ID: 打开主界面
const MENU_OPEN_MAIN: &str = "tray.open_main";

/// 菜单项 ID: 显示/隐藏桌宠
const MENU_TOGGLE_PET: &str = "tray.toggle_pet";

/// 菜单项 ID: 退出应用
const MENU_QUIT: &str = "tray.quit";

/// 初始化系统托盘
pub fn init(app_handle: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let _ = log::write(
        app_handle,
        &log::LogSource::Backend,
        "info",
        "初始化托盘菜单",
    );
    // 创建菜单项
    let open_main =
        MenuItem::with_id(app_handle, MENU_OPEN_MAIN, "打开主界面", true, None::<&str>)?;
    let toggle_pet = MenuItem::with_id(
        app_handle,
        MENU_TOGGLE_PET,
        "显示/隐藏桌宠",
        true,
        None::<&str>,
    )?;
    let quit = MenuItem::with_id(app_handle, MENU_QUIT, "退出应用", true, None::<&str>)?;
    // 创建菜单
    let menu = Menu::with_items(app_handle, &[&open_main, &toggle_pet, &quit])?;
    // 获取应用图标作为托盘图标
    let icon = app_handle
        .default_window_icon()
        .ok_or("无法获取应用图标")?
        .clone();
    // 构建托盘图标
    let _tray = TrayIconBuilder::new()
        .icon(icon.clone())
        .menu(&menu)
        .show_menu_on_left_click(false) // 左键不弹出菜单，用于自定义行为
        .on_menu_event(move |app, event| {
            let _ = handle_menu_event(&app, &event.id.0);
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                // 左键点击时打开主界面
                let _ = show_main_window(&app);
            }
        })
        .tooltip("Nori Desktop Pet - 点击打开主界面")
        .build(app_handle)?;
    let _ = log::write(
        app_handle,
        &log::LogSource::Backend,
        "info",
        "托盘菜单初始化完成",
    );
    Ok(())
}

/// 处理菜单项点击事件
fn handle_menu_event(app: &AppHandle, menu_id: &str) -> Result<(), Box<dyn std::error::Error>> {
    match menu_id {
        MENU_OPEN_MAIN => {
            let _ = log::write(
                app,
                &log::LogSource::Backend,
                "info",
                "托盘菜单：打开主界面",
            );
            show_main_window(app)?;
        }
        MENU_TOGGLE_PET => {
            let _ = log::write(
                app,
                &log::LogSource::Backend,
                "info",
                "托盘菜单：切换桌宠显示",
            );
            toggle_pet_window(app)?;
        }
        MENU_QUIT => {
            let _ = log::write(app, &log::LogSource::Backend, "info", "托盘菜单：退出应用");
            app.exit(0);
        }
        _ => {
            let _ = log::write(
                app,
                &log::LogSource::Backend,
                "warn",
                &format!("未知的菜单项 ID: {}", menu_id),
            );
        }
    }
    Ok(())
}

/// 显示主窗口
fn show_main_window(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    if let Some(window) = app.get_webview_window("main") {
        window.show()?;
        window.set_focus()?;
        let _ = log::write(
            app,
            &log::LogSource::Backend,
            "info",
            "托盘操作：已显示主窗口",
        );
    } else {
        let _ = log::write(
            app,
            &log::LogSource::Backend,
            "warn",
            "托盘操作：主窗口不存在",
        );
    }
    Ok(())
}

/// 切换桌宠窗口的显示/隐藏状态
fn toggle_pet_window(app: &AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    if let Some(window) = app.get_webview_window("pet") {
        let is_visible = window.is_visible()?;
        if is_visible {
            window.hide()?;
            let _ = log::write(
                app,
                &log::LogSource::Backend,
                "info",
                "托盘操作：已隐藏桌宠窗口",
            );
        } else {
            window.show()?;
            window.set_always_on_top(true)?;
            let _ = log::write(
                app,
                &log::LogSource::Backend,
                "info",
                "托盘操作：已显示桌宠窗口",
            );
        }
    } else {
        let _ = log::write(
            app,
            &log::LogSource::Backend,
            "warn",
            "托盘操作：桌宠窗口不存在",
        );
    }
    Ok(())
}
