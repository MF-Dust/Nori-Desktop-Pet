//! 资源管理模块: Live2D 模型

use crate::resource::types::{ResourceInfo, ResourceType};
use crate::resource::{calculate_dir_size, validate_resource_name, RESOURCES_DIR};

use std::fs;
use std::path::{Path, PathBuf};

/// Live2D 资源根目录
fn root_dir(data_dir: &Path) -> PathBuf {
    data_dir
        .join(RESOURCES_DIR)
        .join(ResourceType::Live2D.dir_name())
}

/// 获取指定 Live2D 模型目录
fn model_dir(data_dir: &Path, name: &str) -> Result<PathBuf, String> {
    validate_resource_name(name)?;
    Ok(root_dir(data_dir).join(name))
}

/// 检查 Live2D 模型是否已经安装
pub fn exists(data_dir: &Path, name: &str) -> bool {
    let Ok(resource_dir) = model_dir(data_dir, name) else {
        return false;
    };
    if !resource_dir.is_dir() {
        return false;
    }
    has_model3_json(&resource_dir)
}

/// 获取指定 Live2D 模型的信息
pub fn get(data_dir: &Path, name: &str) -> Result<ResourceInfo, String> {
    let resource_dir = model_dir(data_dir, name)?;
    if !resource_dir.is_dir() {
        return Err(format!("Live2D 资源不存在: {name}"));
    }
    if !has_model3_json(&resource_dir) {
        return Err(format!("Live2D 资源无效，缺少 .model3.json: {name}"));
    }
    let size = calculate_dir_size(&resource_dir)
        .map_err(|e| format!("计算 Live2D 资源大小失败: {e}"))?;
    Ok(ResourceInfo {
        name: name.to_string(),
        resource_type: ResourceType::Live2D,
        path: resource_dir,
        size,
    })
}

/// 列出所有已经安装的 Live2D 模型
/// 只有真正包含 `.model3.json` 的目录才会被列出
pub fn list(data_dir: &Path) -> Vec<ResourceInfo> {
    let root = root_dir(data_dir);
    if !root.is_dir() {
        return Vec::new();
    }
    let Ok(entries) = fs::read_dir(&root) else {
        return Vec::new();
    };
    let mut resources = Vec::new();
    for entry in entries.flatten() {
        let path = entry.path();
        if !path.is_dir() {
            continue;
        }
        let Some(name) = path.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        // 空目录 / 下载失败目录不算已安装
        if !has_model3_json(&path) {
            continue;
        }
        let size = calculate_dir_size(&path).unwrap_or(0);
        resources.push(ResourceInfo {
            name: name.to_string(),
            resource_type: ResourceType::Live2D,
            path,
            size,
        });
    }
    resources.sort_by(|a, b| a.name.cmp(&b.name));
    resources
}

/// 删除指定 Live2D 模型
/// 1. 验证模型名称
/// 2. 确保目标路径位于 Live2D 根目录
/// 3. 确认资源存在
pub fn delete(data_dir: &Path, name: &str) -> Result<(), String> {
    let resource_dir = model_dir(data_dir, name)?;
    let root = root_dir(data_dir);
    ensure_inside(&root, &resource_dir)?;
    if !resource_dir.exists() {
        return Err(format!("Live2D 资源不存在: {name}"));
    }
    if !resource_dir.is_dir() {
        return Err(format!("Live2D 资源路径不是目录: {name}"));
    }
    fs::remove_dir_all(&resource_dir).map_err(|e| format!("删除 Live2D 资源失败: {e}"))?;
    Ok(())
}

/// 检查目录下是否存在 `.model3.json`
fn has_model3_json(dir: &Path) -> bool {
    let Ok(entries) = fs::read_dir(dir) else {
        return false;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let Ok(metadata) = entry.metadata() else {
            continue;
        };
        if metadata.is_dir() {
            if has_model3_json(&path) {
                return true;
            }
            continue;
        }
        if !metadata.is_file() {
            continue;
        }
        let Some(file_name) = path.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        if file_name.to_ascii_lowercase().ends_with(".model3.json") {
            return true;
        }
    }
    false
}

/// 确保目标路径位于 root 目录里面
fn ensure_inside(root: &Path, target: &Path) -> Result<(), String> {
    let root = fs::canonicalize(root).map_err(|e| format!("解析资源根目录失败: {e}"))?;
    // target 可能不存在, 因此不能直接 canonicalize target
    let parent = target
        .parent()
        .ok_or_else(|| "资源路径没有父目录".to_string())?;
    let parent = fs::canonicalize(parent).map_err(|e| format!("解析资源父目录失败: {e}"))?;
    if !parent.starts_with(&root) {
        return Err("资源路径超出资源目录范围".to_string());
    }
    Ok(())
}
