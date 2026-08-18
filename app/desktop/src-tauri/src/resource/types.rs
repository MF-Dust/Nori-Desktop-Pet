//! 资源管理模块: 资源类型

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

/// 资源类型
/// 所有应用资源都通过 ResourceType 管理
#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "camelCase")]
pub enum ResourceType {
    /// Live2D 模型
    Live2D,
}

impl ResourceType {
    /// 获取资源在 data/resources 下的目录名称
    pub const fn dir_name(self) -> &'static str {
        match self {
            Self::Live2D => "live2d",
        }
    }

    /// 获取资源类型对应的字符串
    /// 这个值主要用于:
    /// - API 请求
    /// - 前端传参
    /// - 日志
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Live2D => "live2d",
        }
    }

    /// 从字符串解析资源类型
    pub fn from_str(value: &str) -> Option<Self> {
        match value.trim().to_ascii_lowercase().as_str() {
            "live2d" => Some(Self::Live2D),
            _ => None,
        }
    }

    /// 判断当前资源类型是否为 Live2D 模型
    pub const fn is_live2d(self) -> bool {
        matches!(self, Self::Live2D)
    }
}

/// 资源信息
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ResourceInfo {
    /// 资源名称
    pub name: String,
    /// 资源类型
    pub resource_type: ResourceType,
    /// 资源实际路径
    /// 注意:
    /// 这个字段属于 Rust 内部文件系统信息
    /// 前端如果只是显示资源, 不应该依赖这个路径进行文件访问
    pub path: PathBuf,
    /// 资源总大小, 单位：bytes
    pub size: u64,
}

/// 下载状态
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadProgress {
    /// 已下载字节数
    pub downloaded: u64,
    /// 文件总大小
    /// 如果服务器没有提供 Content-Length, 则为 None
    pub total: Option<u64>,
    /// 下载百分比
    /// 无法计算总大小时为 None
    pub percentage: Option<f32>,
}

impl DownloadProgress {
    /// 创建一个新的下载进度
    pub fn new(downloaded: u64, total: Option<u64>) -> Self {
        let percentage = total
            .filter(|total| *total > 0)
            .map(|total| (downloaded as f64 / total as f64 * 100.0).min(100.0) as f32);
        Self {
            downloaded,
            total,
            percentage,
        }
    }

    /// 创建下载完成状态
    pub fn completed(total: u64) -> Self {
        Self {
            downloaded: total,
            total: Some(total),
            percentage: Some(100.0),
        }
    }
}
