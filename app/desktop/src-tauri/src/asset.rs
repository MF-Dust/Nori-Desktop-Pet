//! 资源文件通道: 自定义 `asset` URI scheme, 把运行期 `data` 目录 serve 给前端.
//!
//! `live2d-easy-control` 等只认 HTTP/fetch 的加载方式需要一条能访问本地模型 /
//! SDK 文件的通道. 这里注册一个 Tauri 自定义协议, 暴露整个 `data` 目录:
//!
//! - Windows/Android:  `http://asset.localhost/<data 内相对路径>`
//! - macOS / iOS / Linux: `asset://localhost/<data 内相对路径>`
//!
//! 协议只 serve `data_dir` 内的相对路径, 并对解析后的真实路径做越界校验,
//! 杜绝 `../` 之类的路径穿越, 不暴露 data 目录以外的任何文件.

use std::borrow::Cow;
use tauri::UriSchemeContext;
use tauri::Wry;
use tauri::http::{Response, StatusCode};

/// 资源协议名. 保持两端 (Rust 注册 / 前端构造 URL) 一致.
pub const SCHEME: &str = "asset";

/// 处理 `asset` scheme 请求: 解析相对路径 → 映射到 data 目录 → 校验 → 返回文件.
///
/// 桌面端 Tauri 运行时为 Wry, 因此这里固定绑定到 `Wry`.
pub fn handle(
    ctx: &UriSchemeContext<'_, Wry>,
    request: tauri::http::Request<Vec<u8>>,
) -> Response<Cow<'static, [u8]>> {
    // 取 URL 的 path 部分 (如 `/live2d/arg-nori/ARGNori.model3.json`), 去掉前导 `/`.
    let raw_path = request.uri().path();
    let relative = raw_path.trim_start_matches('/');
    if relative.is_empty() {
        return not_found("空路径");
    }

    // 解 URL 编码 (%20 等), 拒绝绝对路径形态.
    let decoded = decode_url(relative);
    if decoded.starts_with('/') || decoded.contains(":\\") || decoded.split(['/', '\\']).any(|seg| seg == "..") {
        return not_found("非法路径");
    }
    if decoded.is_empty() {
        return not_found("空路径");
    }

    // live2d-easy-control 约定模型存储为 `<模型名>/<模型名>.model3.json` 的嵌套结构,
    // 而我们运行时 `data/live2d/<name>/` 是扁平存放的 (模型清单与 moc3/纹理同级).
    // 故对 URL 做一次"首层模型名目录剥离": `live2d/<name>/<modelDir>/<rest>` → `live2d/<name>/<rest>`.
    let data_root = match crate::db::data_dir(ctx.app_handle()) {
        Ok(dir) => dir,
        Err(_) => return not_found("data 目录不可用"),
    };
    let data_root = match data_root.canonicalize() {
        Ok(dir) => dir,
        Err(_) => return not_found("data 目录不可用"),
    };

    // 依次尝试候选路径(原始 + 各"剥离冗余目录层"变体), 取第一个能命中真实文件的.
    for candidate in path_candidates(&decoded) {
        let path_buf = std::path::PathBuf::from(&candidate);
        let Ok(file_path) = data_root.join(&path_buf).canonicalize() else {
            continue;
        };
        if !file_path.starts_with(&data_root) || !file_path.is_file() {
            continue;
        }
        return serve_file(&file_path, &candidate);
    }

    not_found(&format!("文件不存在: {relative}"))
}

/// 生成可能的文件路径候选: 先试原始路径, 再尝试去掉其中每一层"可能是 modelDir 冗余目录"的段.
///
/// live2d-easy-control 固定以 `resourcesPath(modelDir 外前缀) + modelDir + "/"` 拼出模型目录,
/// 即 `live2d/<name>/<modelDir>/<真实相对路径>`. 我们的运行时目录是扁平的 (`live2d/<name>/<真实路径>`),
/// 因此 `modelDir` 这一层即唯一冗余. 此处对每个段尝试删除其一, 构造多种候选交由调用方探测.
fn path_candidates(path: &str) -> Vec<String> {
    let segs: Vec<&str> = path.split('/').filter(|s| !s.is_empty()).collect();
    if segs.len() < 3 {
        return vec![path.to_string()];
    }
    let mut out = Vec::with_capacity(segs.len());
    out.push(path.to_string());
    // 从第 2 个段起(第 0 段是 live2d), 逐个移除一段生成候选.
    for i in 1..segs.len() {
        let mut clone = segs.clone();
        clone.remove(i);
        out.push(clone.join("/"));
    }
    out
}

/// 读取文件并以正确的 MIME 返回.
fn serve_file(file_path: &std::path::Path, logical: &str) -> Response<Cow<'static, [u8]>> {
    match std::fs::read(file_path) {
        Ok(bytes) => Response::builder()
            .status(StatusCode::OK)
            .header(tauri::http::header::CONTENT_TYPE, mime_for(logical))
            .header("Access-Control-Allow-Origin", "*")
            .header("Cache-Control", "no-store")
            .body(Cow::Owned(bytes))
            .unwrap_or_else(|_| not_found("构造响应失败")),
        Err(e) => {
            let _ = crate::log::write(
                &crate::log::LogSource::Backend,
                "warn",
                &format!("asset 协议读取失败: {logical} ({e})"),
            );
            not_found(&format!("读取失败: {logical}"))
        }
    }
}

/// 简易 URL 解码: 仅处理最常见的 `%XX` 字节序列, 未识别字符原样保留.
fn decode_url(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let (Some(hi), Some(lo)) = (hex_val(bytes[i + 1]), hex_val(bytes[i + 2])) {
                out.push((hi << 4) | lo);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn hex_val(b: u8) -> Option<u8> {
    match b {
        b'0'..=b'9' => Some(b - b'0'),
        b'a'..=b'f' => Some(b - b'a' + 10),
        b'A'..=b'F' => Some(b - b'A' + 10),
        _ => None,
    }
}

/// 根据扩展名返回 MIME 类型.
fn mime_for(path: &str) -> &'static str {
    let lower = path.to_lowercase();
    if lower.ends_with(".png") {
        "image/png"
    } else if lower.ends_with(".jpg") || lower.ends_with(".jpeg") {
        "image/jpeg"
    } else if lower.ends_with(".webp") {
        "image/webp"
    } else if lower.ends_with(".moc3") {
        "application/octet-stream"
    } else if lower.ends_with(".json") {
        "application/json"
    } else if lower.ends_with(".zip") {
        "application/zip"
    } else {
        "application/octet-stream"
    }
}

fn not_found(message: &str) -> Response<Cow<'static, [u8]>> {
    Response::builder()
        .status(StatusCode::NOT_FOUND)
        .header(tauri::http::header::CONTENT_TYPE, "text/plain; charset=utf-8")
        .header("Access-Control-Allow-Origin", "*")
        .body(Cow::Owned(message.as_bytes().to_vec()))
        .unwrap_or_else(|_| Response::new(Cow::Borrowed(&b""[..])))
}
