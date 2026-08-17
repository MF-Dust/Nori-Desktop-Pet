use crate::log::{self, LogSource};
use serde::Serialize;
use std::path::{Path, PathBuf};
use tauri::AppHandle;

#[derive(Serialize)]
pub struct Live2DModelInfo {
	pub id: String,
	pub installed: bool,
	pub model3: Option<String>,
}

fn base_dir(_app: &AppHandle) -> PathBuf {
	let exe = std::env::current_exe().expect("无法获取可执行文件路径");
	exe.parent()
		.unwrap_or(Path::new("."))
		.join("data")
		.join("live2D")
}

fn find_model3(dir: &Path) -> Option<PathBuf> {
	if let Ok(entries) = std::fs::read_dir(dir) {
		for entry in entries.flatten() {
			let path = entry.path();
			if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
				if name.ends_with(".model3.json") {
					return Some(path);
				}
			}
		}
	}
	None
}

#[tauri::command]
pub fn list_live2d_models(app: AppHandle) -> Result<Vec<Live2DModelInfo>, String> {
	let base = base_dir(&app);
	if !base.exists() {
		let _ = log::write(
			&LogSource::Backend,
			"info",
			&format!("Live2D 目录不存在: {}", base.display()),
		);
		return Ok(Vec::new());
	}
	let entries = std::fs::read_dir(&base).map_err(|e| {
		let _ = log::write(
			&LogSource::Backend,
			"error",
			&format!("读取 Live2D 目录失败: {e}"),
		);
		format!("读取 Live2D 目录失败: {e}")
	})?;
	let mut list = Vec::new();
	for entry in entries {
		let entry = match entry {
			Ok(e) => e,
			Err(_) => continue,
		};
		let path = entry.path();
		if !path.is_dir() {
			continue;
		}
		let id = match path.file_name().and_then(|n| n.to_str()) {
			Some(s) => s.to_string(),
			None => continue,
		};
		let model3 = find_model3(&path);
		let installed = model3.is_some();
		list.push(Live2DModelInfo {
			id,
			installed,
			model3: model3.map(|p| p.to_string_lossy().into_owned()),
		});
	}
	let _ = log::write(
		&LogSource::Backend,
		"info",
		&format!("扫描 Live2D 模型完成, 共 {} 个", list.len()),
	);
	Ok(list)
}

#[tauri::command]
pub fn resolve_live2d_model_path(app: AppHandle, model_id: String) -> Result<Option<String>, String> {
	let base = base_dir(&app);
	let dir = base.join(&model_id);
	if !dir.exists() {
		let _ = log::write(
			&LogSource::Backend,
			"warn",
			&format!("模型目录不存在: {} (model_id={model_id})", dir.display()),
		);
		return Ok(None);
	}
	let model3 = find_model3(&dir).map(|p| p.to_string_lossy().into_owned());
	if model3.is_none() {
		let _ = log::write(
			&LogSource::Backend,
			"warn",
			&format!("模型 {model_id} 未找到 model3.json"),
		);
	}
	Ok(model3)
}
