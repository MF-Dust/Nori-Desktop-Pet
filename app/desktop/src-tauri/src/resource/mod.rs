//! 资源管理模块

pub mod downloader;
pub mod live2d;
pub mod types;

pub use types::{DownloadProgress, ResourceInfo, ResourceType};

use std::path::{Path, PathBuf};
use tauri::AppHandle;

use crate::db;

/// 所有资源的根目录
pub const RESOURCES_DIR: &str = "resources";

/// 下载临时文件目录
pub const TEMP_DIR: &str = "temp";

/// 获取资源根目录
pub fn resources_dir(app: &AppHandle) -> Result<PathBuf, String> {
    Ok(db::data_dir(app)
        .map_err(|e| e.to_string())?
        .join(RESOURCES_DIR))
}

/// 获取指定类型资源的目录
pub fn type_dir(app: &AppHandle, resource_type: ResourceType) -> Result<PathBuf, String> {
    Ok(resources_dir(app)?.join(resource_type.dir_name()))
}

/// 获取指定资源目录
pub fn resource_dir(
    app: &AppHandle,
    resource_type: ResourceType,
    name: &str,
) -> Result<PathBuf, String> {
    validate_resource_name(name)?;
    Ok(type_dir(app, resource_type)?.join(name))
}

/// 获取资源下载临时目录
pub fn temp_dir(app: &AppHandle) -> Result<PathBuf, String> {
    let data_dir = db::data_dir(app).map_err(|e| e.to_string())?;
    Ok(data_dir.join(TEMP_DIR))
}

/// 初始化资源目录
/// setup 阶段调用
pub fn init(app: &AppHandle) -> Result<(), String> {
    let resources = resources_dir(app)?;
    std::fs::create_dir_all(&resources)
        .map_err(|e| format!("创建资源目录失败: {}: {}", resources.display(), e))?;
    let temp = temp_dir(app)?;
    std::fs::create_dir_all(&temp)
        .map_err(|e| format!("创建资源临时目录失败: {}: {}", temp.display(), e))?;
    Ok(())
}

/// 判断资源是否已经安装
pub fn is_installed(
    app: &AppHandle,
    resource_type: ResourceType,
    name: &str,
) -> Result<bool, String> {
    validate_resource_name(name)?;
    let data_dir = db::data_dir(app).map_err(|e| e.to_string())?;
    match resource_type {
        ResourceType::Live2D => Ok(live2d::exists(&data_dir, name))
    }
}

/// 获取指定类型的所有资源
pub fn list(app: &AppHandle, resource_type: ResourceType) -> Result<Vec<ResourceInfo>, String> {
    let data_dir = db::data_dir(app).map_err(|e| e.to_string())?;
    match resource_type {
        ResourceType::Live2D => Ok(live2d::list(&data_dir)),
    }
}

/// 获取单个资源信息
pub fn get(
    app: &AppHandle,
    resource_type: ResourceType,
    name: &str,
) -> Result<ResourceInfo, String> {
    validate_resource_name(name)?;
    let data_dir = db::data_dir(app).map_err(|e| e.to_string())?;
    match resource_type {
        ResourceType::Live2D => live2d::get(&data_dir, name),
    }
}

/// 删除资源
pub fn delete(app: &AppHandle, resource_type: ResourceType, name: &str) -> Result<(), String> {
    validate_resource_name(name)?;
    let data_dir = db::data_dir(app).map_err(|e| e.to_string())?;
    match resource_type {
        ResourceType::Live2D => live2d::delete(&data_dir, name),
    }
}

/// 验证资源名称
/// 资源名称只能表示一个目录名, 不能是路径
pub fn validate_resource_name(name: &str) -> Result<(), String> {
    let name = name.trim();
    if name.is_empty() {
        return Err("资源名称不能为空".to_string());
    }
    if name == "." || name == ".." {
        return Err(format!("非法资源名称: {name}"));
    }
    if name.contains('/') || name.contains('\\') {
        return Err(format!("资源名称不能包含路径分隔符: {name}"));
    }
    if name.chars().any(char::is_control) {
        return Err("资源名称不能包含控制字符".to_string());
    }
    // Windows 盘符, 例如 C:
    if name.len() >= 2 {
        let bytes = name.as_bytes();
        if bytes[0].is_ascii_alphabetic() && bytes[1] == b':' {
            return Err(format!("非法资源名称: {name}"));
        }
    }
    Ok(())
}

/// 列出普通目录型资源
fn list_directory_resources(
    data_dir: &Path,
    resource_type: ResourceType,
) -> Result<Vec<ResourceInfo>, String> {
    let root = data_dir.join(RESOURCES_DIR).join(resource_type.dir_name());
    if !root.is_dir() {
        return Ok(Vec::new());
    }
    let entries = std::fs::read_dir(&root).map_err(|e| format!("读取资源目录失败: {e}"))?;
    let mut resources = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }
        let Some(name) = path.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        let size = calculate_dir_size(&path).unwrap_or(0);
        resources.push(ResourceInfo {
            name: name.to_string(),
            resource_type,
            path,
            size,
        });
    }
    resources.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(resources)
}

/// 计算目录大小
fn calculate_dir_size(path: &Path) -> std::io::Result<u64> {
    let mut total = 0u64;
    for entry in std::fs::read_dir(path)? {
        let entry = entry?;
        let metadata = entry.metadata()?;
        if metadata.is_file() {
            total = total.saturating_add(metadata.len());
        } else if metadata.is_dir() {
            total = total.saturating_add(calculate_dir_size(&entry.path())?);
        }
    }
    Ok(total)
}
