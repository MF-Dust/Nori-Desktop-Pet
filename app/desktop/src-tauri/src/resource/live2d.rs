use crate::resource::types::{ResourceInfo, ResourceType};
use std::fs;
use std::path::Path;

/// 检查 Live2D 资源是否存在
pub fn exists(data_dir: &Path, name: &str) -> bool {
    let resource_dir = data_dir.join(ResourceType::Live2D.dir_name()).join(name);
    resource_dir.exists() && resource_dir.is_dir()
}

/// 列出所有 Live2D 资源
pub fn list(data_dir: &Path) -> Vec<ResourceInfo> {
    let live2d_dir = data_dir.join(ResourceType::Live2D.dir_name());

    if !live2d_dir.exists() {
        return Vec::new();
    }

    let mut resources = Vec::new();

    if let Ok(entries) = fs::read_dir(&live2d_dir) {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_dir() {
                if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                    let size = calculate_dir_size(&path).unwrap_or(0);
                    resources.push(ResourceInfo {
                        name: name.to_string(),
                        resource_type: ResourceType::Live2D,
                        path: path.clone(),
                        size,
                    });
                }
            }
        }
    }

    resources
}

/// 删除 Live2D 资源
pub fn delete(data_dir: &Path, name: &str) -> Result<(), String> {
    let resource_dir = data_dir.join(ResourceType::Live2D.dir_name()).join(name);
    
    if !resource_dir.exists() {
        return Err(format!("资源不存在: {}", name));
    }

    fs::remove_dir_all(&resource_dir).map_err(|e| format!("删除失败: {}", e))?;
    
    Ok(())
}

/// 计算目录大小
fn calculate_dir_size(path: &Path) -> Result<u64, std::io::Error> {
    let mut size = 0;
    
    if path.is_dir() {
        for entry in fs::read_dir(path)? {
            let entry = entry?;
            let metadata = entry.metadata()?;
            
            if metadata.is_file() {
                size += metadata.len();
            } else if metadata.is_dir() {
                size += calculate_dir_size(&entry.path())?;
            }
        }
    }
    
    Ok(size)
}
