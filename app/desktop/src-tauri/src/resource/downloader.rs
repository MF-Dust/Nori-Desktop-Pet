use crate::resource::types::{DownloadProgress, ResourceType};
use reqwest::blocking::Client;
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use zip::ZipArchive;

/// 下载器错误类型
#[derive(Debug)]
pub enum DownloadError {
    Network(String),
    Io(String),
    Zip(String),
    Api(String),
}

impl std::fmt::Display for DownloadError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            DownloadError::Network(msg) => write!(f, "网络错误: {}", msg),
            DownloadError::Io(msg) => write!(f, "IO错误: {}", msg),
            DownloadError::Zip(msg) => write!(f, "解压错误: {}", msg),
            DownloadError::Api(msg) => write!(f, "API错误: {}", msg),
        }
    }
}

impl std::error::Error for DownloadError {}

/// 下载资源
///
/// # Arguments
/// * `resource_type` - 资源类型
/// * `name` - 资源名称
/// * `data_dir` - 数据目录
/// * `progress_callback` - 进度回调函数
///
/// # Returns
/// 返回解压后的资源目录路径
pub fn download_resource<F>(
    resource_type: ResourceType,
    name: &str,
    data_dir: &Path,
    progress_callback: F,
) -> Result<PathBuf, DownloadError>
where
    F: Fn(DownloadProgress),
{
    // 1. 调用后端 API 获取签名 URL
    let signed_url = get_signed_url(&resource_type, name)?;

    // 2. 准备临时文件路径
    let temp_dir = data_dir.join("temp");
    fs::create_dir_all(&temp_dir).map_err(|e| DownloadError::Io(e.to_string()))?;
    let temp_file = temp_dir.join(format!("{}.zip", name));

    // 3. 下载文件
    download_file(&signed_url, &temp_file, progress_callback)?;

    // 4. 解压到目标目录
    let target_dir = data_dir.join(resource_type.dir_name()).join(name);
    extract_zip(&temp_file, &target_dir)?;

    // 5. 清理临时文件
    fs::remove_file(&temp_file).map_err(|e| DownloadError::Io(e.to_string()))?;

    Ok(target_dir)
}

/// 从后端 API 获取签名 URL
fn get_signed_url(resource_type: &ResourceType, name: &str) -> Result<String, DownloadError> {
    let client = Client::new();
    let url = format!(
        "http://localhost:8084/api/resource/download-url?type={}&name={}",
        resource_type.dir_name().to_lowercase(),
        name
    );

    let response = client
        .get(&url)
        .send()
        .map_err(|e| DownloadError::Network(e.to_string()))?;

    if !response.status().is_success() {
        return Err(DownloadError::Api(format!(
            "HTTP {}",
            response.status()
        )));
    }

    let json: serde_json::Value = response
        .json()
        .map_err(|e| DownloadError::Api(e.to_string()))?;

    json["body"]["url"]
        .as_str()
        .map(|s| s.to_string())
        .ok_or_else(|| DownloadError::Api("响应中缺少 url 字段".to_string()))
}

/// 下载文件到指定路径，带进度回调
fn download_file<F>(
    url: &str,
    target: &Path,
    progress_callback: F,
) -> Result<(), DownloadError>
where
    F: Fn(DownloadProgress),
{
    let client = Client::new();
    let response = client
        .get(url)
        .send()
        .map_err(|e| DownloadError::Network(e.to_string()))?;

    if !response.status().is_success() {
        return Err(DownloadError::Network(format!(
            "HTTP {}",
            response.status()
        )));
    }

    let total_size = response
        .content_length()
        .ok_or_else(|| DownloadError::Network("无法获取文件大小".to_string()))?;

    let mut file = File::create(target).map_err(|e| DownloadError::Io(e.to_string()))?;
    let mut downloaded: u64 = 0;
    let mut buffer = [0u8; 8192];
    let mut reader = response;

    loop {
        let bytes_read = reader
            .read(&mut buffer)
            .map_err(|e| DownloadError::Io(e.to_string()))?;
        if bytes_read == 0 {
            break;
        }

        file.write_all(&buffer[..bytes_read])
            .map_err(|e| DownloadError::Io(e.to_string()))?;
        downloaded += bytes_read as u64;

        // 发送进度更新
        let percentage = if total_size > 0 {
            (downloaded as f32 / total_size as f32) * 100.0
        } else {
            0.0
        };

        progress_callback(DownloadProgress {
            downloaded,
            total: total_size,
            percentage,
        });
    }

    Ok(())
}

/// 解压 zip 文件到目标目录
fn extract_zip(zip_path: &Path, target_dir: &Path) -> Result<(), DownloadError> {
    let file = File::open(zip_path).map_err(|e| DownloadError::Io(e.to_string()))?;
    let mut archive = ZipArchive::new(file).map_err(|e| DownloadError::Zip(e.to_string()))?;

    // 创建目标目录
    fs::create_dir_all(target_dir).map_err(|e| DownloadError::Io(e.to_string()))?;

    // 解压所有文件
    for i in 0..archive.len() {
        let mut file = archive
            .by_index(i)
            .map_err(|e| DownloadError::Zip(e.to_string()))?;

        let outpath = target_dir.join(file.name());

        if file.name().ends_with('/') {
            fs::create_dir_all(&outpath).map_err(|e| DownloadError::Io(e.to_string()))?;
        } else {
            if let Some(p) = outpath.parent() {
                if !p.exists() {
                    fs::create_dir_all(p).map_err(|e| DownloadError::Io(e.to_string()))?;
                }
            }
            let mut outfile = File::create(&outpath).map_err(|e| DownloadError::Io(e.to_string()))?;
            io::copy(&mut file, &mut outfile).map_err(|e| DownloadError::Io(e.to_string()))?;
        }
    }

    Ok(())
}
