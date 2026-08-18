//! 系统级查询命令 (全局鼠标位置等)

/// 获取全局鼠标位置 (物理像素, 相对屏幕左上角)
///
/// 桌宠窗口外的光标移动也需要让模型头部跟踪, 浏览器无法获取
/// 窗口外光标, 这里通过系统 API 查询
#[tauri::command]
pub fn get_cursor_pos() -> Result<(f64, f64), String> {
    #[cfg(target_os = "windows")]
    {
        use windows_sys::Win32::Foundation::POINT;
        use windows_sys::Win32::UI::WindowsAndMessaging::GetCursorPos;
        unsafe {
            let mut point = std::mem::zeroed::<POINT>();
            if GetCursorPos(&mut point) == 0 {
                return Err("GetCursorPos failed".to_string());
            }
            Ok((point.x as f64, point.y as f64))
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = app;
        Err("get_cursor_pos is only supported on Windows".to_string())
    }
}