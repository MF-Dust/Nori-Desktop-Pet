//! 资源管理模块: 资源下载器
//! 当前实现使用 reqwest::blocking, 因此不要在 UI / Tauri 主线程中直接执行

use crate::resource::{calculate_dir_size, temp_dir, type_dir, validate_resource_name};

use crate::resource::types::{DownloadProgress, ResourceType};

use reqwest::blocking::Client;
use reqwest::Url;

use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};

use zip::ZipArchive;

/// 资源下载 API。
const DOWNLOAD_API: &str = "https://api.elake.top/nori/resource/download_url";

/// 下载缓冲区大小。
const BUFFER_SIZE: usize = 64 * 1024;

#[derive(Debug)]
pub enum DownloadError {
    /// 网络请求失败
    Network(String),
    /// 本地文件系统错误
    Io(String),
    /// ZIP 文件错误
    Zip(String),
    /// 后端 API 返回错误
    Api(String),
    /// 资源参数非法
    InvalidResource(String),
    /// 资源包内容非法
    InvalidArchive(String),
}

impl std::fmt::Display for DownloadError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Network(message) => {
                write!(f, "网络错误: {message}")
            }

            Self::Io(message) => {
                write!(f, "IO 错误: {message}")
            }

            Self::Zip(message) => {
                write!(f, "ZIP 错误: {message}")
            }

            Self::Api(message) => {
                write!(f, "API 错误: {message}")
            }

            Self::InvalidResource(message) => {
                write!(f, "资源参数错误: {message}")
            }

            Self::InvalidArchive(message) => {
                write!(f, "资源包无效: {message}")
            }
        }
    }
}

impl std::error::Error for DownloadError {}

/// 下载资源
/// 获取下载 URL
///      ↓
/// 下载 ZIP
///      ↓
/// 安全解压
///      ↓
/// 校验资源目录
///      ↓
/// 删除临时 ZIP
///      ↓
/// 返回资源目录
/// progress_callback 会持续收到下载进度
pub fn download_resource<F>(
    resource_type: ResourceType,
    name: &str,
    data_dir: &Path,
    progress_callback: F,
) -> Result<PathBuf, DownloadError>
where
    F: Fn(DownloadProgress),
{
    validate_name(name)?;
    let zip_path = download_to_zip(&resource_type, name, data_dir, &progress_callback)?;
    let target_dir = data_dir
        .join("resources")
        .join(resource_type.dir_name())
        .join(name);
    // 如果之前存在不完整资源, 先删除
    if target_dir.exists() {
        fs::remove_dir_all(&target_dir)
            .map_err(|e| DownloadError::Io(format!("清理旧资源失败: {e}")))?;
    }
    // 创建目标目录
    fs::create_dir_all(&target_dir)
        .map_err(|e| DownloadError::Io(format!("创建资源目录失败: {e}")))?;
    // 解压
    if let Err(error) = extract_zip(&zip_path, &target_dir) {
        // 解压失败时清理半成品
        let _ = fs::remove_dir_all(&target_dir);
        return Err(error);
    }
    // 删除临时 ZIP
    let _ = fs::remove_file(&zip_path);
    Ok(target_dir)
}

/// 下载 ZIP 文件
pub fn download_to_zip<F>(
    resource_type: &ResourceType,
    name: &str,
    data_dir: &Path,
    progress_callback: F,
) -> Result<PathBuf, DownloadError>
where
    F: Fn(DownloadProgress),
{
    validate_name(name)?;
    let signed_url = get_signed_url(resource_type, name)?;
    let temp = data_dir.join("temp");
    fs::create_dir_all(&temp).map_err(|e| DownloadError::Io(format!("创建临时目录失败: {e}")))?;
    let zip_path = temp.join(format!("{name}.zip"));
    let part_path = temp.join(format!("{name}.zip.part"));
    // 清理之前可能残留的下载
    let _ = fs::remove_file(&part_path);
    // 下载到 .part
    if let Err(error) = download_file(&signed_url, &part_path, progress_callback) {
        let _ = fs::remove_file(&part_path);
        return Err(error);
    }
    // 删除旧 ZIP
    let _ = fs::remove_file(&zip_path);
    // 下载完成后再移动
    fs::rename(&part_path, &zip_path).map_err(|e| {
        let _ = fs::remove_file(&part_path);
        DownloadError::Io(format!("保存下载文件失败: {e}"))
    })?;
    Ok(zip_path)
}

/// 从后端 API 获取签名下载 URL
fn get_signed_url(resource_type: &ResourceType, name: &str) -> Result<String, DownloadError> {
    let client = Client::builder()
        .build()
        .map_err(|e| DownloadError::Network(e.to_string()))?;
    let mut url = Url::parse(DOWNLOAD_API)
        .map_err(|e| DownloadError::Api(format!("下载 API 地址无效: {e}")))?;
    url.query_pairs_mut()
        .append_pair("type", resource_type.as_str())
        .append_pair("name", name);
    let response = client
        .get(url)
        .send()
        .map_err(|e| DownloadError::Network(e.to_string()))?;
    let status = response.status();
    if !status.is_success() {
        return Err(DownloadError::Api(format!("下载 API HTTP {}", status)));
    }
    let json: serde_json::Value = response
        .json()
        .map_err(|e| DownloadError::Api(format!("解析 API 响应失败: {e}")))?;
    // API 业务错误
    if json.get("error").and_then(|v| v.as_bool()).unwrap_or(false) {
        let message = json
            .get("message")
            .and_then(|v| v.as_str())
            .filter(|v| !v.is_empty())
            .unwrap_or("接口返回错误");
        return Err(DownloadError::Api(message.to_string()));
    }
    let url = json
        .get("body")
        .and_then(|v| v.get("url"))
        .and_then(|v| v.as_str())
        .filter(|v| !v.is_empty())
        .ok_or_else(|| DownloadError::Api("API 响应中缺少 body.url".to_string()))?;
    Ok(url.to_string())
}

/// 下载文件
fn download_file<F>(url: &str, target: &Path, progress_callback: F) -> Result<(), DownloadError>
where
    F: Fn(DownloadProgress),
{
    let client = Client::builder()
        .build()
        .map_err(|e| DownloadError::Network(e.to_string()))?;
    let response = client
        .get(url)
        .send()
        .map_err(|e| DownloadError::Network(e.to_string()))?;
    let status = response.status();
    if !status.is_success() {
        return Err(DownloadError::Network(format!("下载文件 HTTP {}", status)));
    }
    let total = response.content_length();
    let parent = target
        .parent()
        .ok_or_else(|| DownloadError::Io("下载目标没有父目录".to_string()))?;
    fs::create_dir_all(parent).map_err(|e| DownloadError::Io(e.to_string()))?;
    let mut file =
        File::create(target).map_err(|e| DownloadError::Io(format!("创建下载文件失败: {e}")))?;
    let mut reader = response;
    let mut buffer = vec![0u8; BUFFER_SIZE];
    let mut downloaded = 0u64;
    loop {
        let bytes_read = reader
            .read(&mut buffer)
            .map_err(|e| DownloadError::Network(format!("读取网络数据失败: {e}")))?;
        if bytes_read == 0 {
            break;
        }
        file.write_all(&buffer[..bytes_read])
            .map_err(|e| DownloadError::Io(format!("写入下载文件失败: {e}")))?;
        downloaded = downloaded.saturating_add(bytes_read as u64);
        progress_callback(DownloadProgress::new(downloaded, total));
    }
    file.flush()
        .map_err(|e| DownloadError::Io(format!("刷新下载文件失败: {e}")))?;
    // 有 Content-Length 时进行最终校验
    if let Some(total) = total {
        if downloaded != total {
            return Err(DownloadError::Network(format!(
                "下载文件大小不完整: {downloaded}/{total}"
            )));
        }
    }
    // 明确发送 100%
    progress_callback(DownloadProgress::completed(downloaded));
    Ok(())
}

/// 安全解压 ZIP
pub fn extract_zip(zip_path: &Path, target_dir: &Path) -> Result<(), DownloadError> {
    let file =
        File::open(zip_path).map_err(|e| DownloadError::Io(format!("打开 ZIP 失败: {e}")))?;
    let mut archive = ZipArchive::new(file).map_err(|e| DownloadError::Zip(e.to_string()))?;
    fs::create_dir_all(target_dir)
        .map_err(|e| DownloadError::Io(format!("创建解压目录失败: {e}")))?;
    let canonical_target = fs::canonicalize(target_dir)
        .map_err(|e| DownloadError::Io(format!("解析解压目录失败: {e}")))?;
    for index in 0..archive.len() {
        let mut entry = archive
            .by_index(index)
            .map_err(|e| DownloadError::Zip(e.to_string()))?;
        let raw_name = entry.name();
        let entry_name = sanitize_zip_path(raw_name)?;
        if entry_name.is_empty() {
            continue;
        }
        let outpath = target_dir.join(&entry_name);
        // 目录
        if entry.is_dir() {
            fs::create_dir_all(&outpath).map_err(|e| {
                DownloadError::Io(format!("创建目录失败 {}: {e}", outpath.display()))
            })?;
            continue;
        }
        // ZIP 中如果包含符号链接, 不允许创建
        if is_zip_symlink(&entry) {
            return Err(DownloadError::InvalidArchive(format!(
                "ZIP 包包含不允许的符号链接: {raw_name}"
            )));
        }
        let parent = outpath.parent().ok_or_else(|| {
            DownloadError::InvalidArchive(format!("ZIP 条目没有父目录: {raw_name}"))
        })?;
        fs::create_dir_all(parent).map_err(|e| DownloadError::Io(format!("创建目录失败: {e}")))?;
        // 对 parent 再做一次 canonical 检查
        // 主要防止已有软链接导致路径跳出 target_dir
        let canonical_parent = fs::canonicalize(parent)
            .map_err(|e| DownloadError::Io(format!("解析 ZIP 父目录失败: {e}")))?;
        if !canonical_parent.starts_with(&canonical_target) {
            return Err(DownloadError::InvalidArchive(format!(
                "ZIP 条目超出目标目录: {raw_name}"
            )));
        }
        let mut outfile = File::create(&outpath)
            .map_err(|e| DownloadError::Io(format!("创建文件失败 {}: {e}", outpath.display())))?;
        io::copy(&mut entry, &mut outfile)
            .map_err(|e| DownloadError::Io(format!("解压文件失败 {}: {e}", outpath.display())))?;
        outfile
            .flush()
            .map_err(|e| DownloadError::Io(format!("刷新解压文件失败: {e}")))?;
    }
    Ok(())
}

/// 清理并验证 ZIP 内部路径
/// ZIP 标准通常使用 `/`, 但 Windows ZIP 也可能出现 `\`
fn sanitize_zip_path(raw: &str) -> Result<String, DownloadError> {
    if raw.is_empty() {
        return Ok(String::new());
    }
    // 统一 Windows 分隔符
    let normalized = raw.replace('\\', "/");
    // Unix 绝对路径
    if normalized.starts_with('/') {
        return Err(DownloadError::InvalidArchive(format!(
            "ZIP 包包含绝对路径: {raw}"
        )));
    }
    // Windows UNC 路径
    if normalized.starts_with("//") {
        return Err(DownloadError::InvalidArchive(format!(
            "ZIP 包包含 UNC 路径: {raw}"
        )));
    }
    // Windows 盘符
    if normalized.len() >= 2 {
        let bytes = normalized.as_bytes();
        if bytes[0].is_ascii_alphabetic() && bytes[1] == b':' {
            return Err(DownloadError::InvalidArchive(format!(
                "ZIP 包包含 Windows 绝对路径: {raw}"
            )));
        }
    }
    let mut parts = Vec::new();
    for part in normalized.split('/') {
        if part.is_empty() || part == "." {
            continue;
        }
        if part == ".." {
            return Err(DownloadError::InvalidArchive(format!(
                "ZIP 条目包含路径穿越: {raw}"
            )));
        }
        if part.chars().any(char::is_control) {
            return Err(DownloadError::InvalidArchive(format!(
                "ZIP 条目包含非法字符: {raw}"
            )));
        }
        parts.push(part);
    }
    Ok(parts.join("/"))
}

/// 判断 ZIP entry 是否是符号链接
fn is_zip_symlink(entry: &zip::read::ZipFile<'_>) -> bool {
    #[cfg(unix)]
    {
        if let Some(mode) = entry.unix_mode() {
            return (mode & 0o170000) == 0o120000;
        }
    }
    false
}

/// 验证资源名称是否符合要求
fn validate_name(name: &str) -> Result<(), DownloadError> {
    validate_resource_name(name).map_err(DownloadError::InvalidResource)
}
