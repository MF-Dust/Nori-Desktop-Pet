/**
 * 系统能力
 *
 * 取代原来的 @tauri-apps/plugin-opener 与 @tauri-apps/plugin-clipboard-manager 两个插件
 */
import {invoke} from "./invoke"

/**
 * 用系统默认浏览器打开链接
 */
export const openUrl = async (url: string): Promise<void> => {
	await invoke("open_url", {url})
}

/**
 * 写入系统剪贴板
 */
export const writeText = async (text: string): Promise<void> => {
	await invoke("clipboard_write_text", {text})
}
