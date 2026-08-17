use serde::{Deserialize, Serialize};
use std::path::PathBuf;

/// 资源类型
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "lowercase")]
pub enum ResourceType {
    Live2D,
    // 未来扩展: Voice, Image 等
}

impl ResourceType {
    /// 获取资源目录名
    pub fn dir_name(&self) -> &str {
        match self {
            ResourceType::Live2D => "live2D",
        }
    }

    /// 从字符串解析
    pub fn from_str(s: &str) -> Option<Self> {
        match s.to_lowercase().as_str() {
            "live2d" => Some(ResourceType::Live2D),
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
