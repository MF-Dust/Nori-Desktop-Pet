/**
 * 宿主桥接层
 *
 * 取代原来的 @tauri-apps/api: 上层代码只认这里导出的接口, 不关心底层是 Tauri 还是 Avalonia.
 * 底层实现在 index.html 的引导脚本里 (window.__nori), 必须在应用代码之前同步就位.
 */

/**
 * 引导脚本注入的宿主对象
 */
interface NoriHost {
	assetBase: string
	label: string | null
	invoke: (cmd: string, args?: Record<string, unknown>) => Promise<unknown>
	emit: (event: string, payload?: unknown) => void
	listen: (event: string, handler: (message: {payload: unknown}) => void) => () => void
	dispatch: (raw: string) => void
}

declare global {
	interface Window {
		__nori?: NoriHost
	}
}

/**
 * 取宿主对象, 纯浏览器调试 (未经宿主打开) 时返回 null
 */
export const host = (): NoriHost | null => window.__nori ?? null

/**
 * 是否运行在宿主中
 */
export const inHost = (): boolean => host() !== null

export {}
