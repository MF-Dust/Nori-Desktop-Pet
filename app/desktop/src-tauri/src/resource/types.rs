use serde::{Deserialize, Serialize};
use std::path::PathBuf;

/// 资源类型
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum ResourceType {
    Live2D,
    Live2DSdk,
}

impl ResourceType {
    pub fn dir_name(&self) -> &str {
        match self {
            ResourceType::Live2D => "live2d",
            ResourceType::Live2DSdk => "live2d-sdk",
        }
    }

    /// 从字符串解析
    pub fn from_str(s: &str) -> Option<Self> {
        match s.to_lowercase().as_str() {
            "live2d" => Some(ResourceType::Live2D),
            "live2dsdk" | "live2d-sdk" | "live2d_sdk" => Some(ResourceType::Live2DSdk),
            _ => None,
        }
    }
}

/// 资源信息
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResourceInfo {
    pub name: String,
    pub resource_type: ResourceType,
    pub path: PathBuf,
    pub size: u64,
}

/// 下载进度
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DownloadProgress {
    pub downloaded: u64,
    pub total: u64,
    pub percentage: f32,
}
