// 资源管理模块: 各类型资源 (live2d 等) 的检查 / 下载 / 管理.
// 对外提供通用 `is_installed` 按资源类型分发; downloader 负责下载与解压;
// 其余 API (list/delete/from_str) 为管理功能预留, 待接入前允许 dead_code 以免干扰编译.
#![allow(dead_code)]
#![allow(unused_imports)]

pub mod downloader;
pub mod live2d;
pub mod types;

pub use types::{ResourceInfo, ResourceType};
use std::path::Path;

/// 判断指定类型的资源是否已安装 (按具体类型分发到各自的检查逻辑)
pub fn is_installed(data_dir: &Path, resource_type: &ResourceType, name: &str) -> bool {
    match resource_type {
        // Live2D 需要目录内含 `.model3.json` 模型清单才算真正安装
        ResourceType::Live2D => live2d::exists(data_dir, name),
        // SDK 目前以目录存在作为"已就位"判据 (无固定清单文件)
        ResourceType::Live2DSdk => data_dir.join("live2d-sdk").join(name).is_dir(),
    }
}
