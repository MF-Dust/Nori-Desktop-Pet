import {emit} from "@tauri-apps/api/event"
import {getCurrentWebviewWindow, WebviewWindow} from "@tauri-apps/api/webviewWindow"

/**
 * 窗口调度器
 */
export type WindowLabel = "first-run" | "init" | "main" | "pet"

/**
 * 窗口 label → 页面路由
 */
export const NAMESPACE = {
	firstRun: "first-run",
	init: "init",
	main: "main",
	pet: "pet",
} as const satisfies Record<string, WindowLabel>

/**
 * 窗口 label → 页面路由表
 */
export const WINDOW_ROUTES: Record<WindowLabel, string> = {
	[NAMESPACE.firstRun]: "/first-run",
	[NAMESPACE.init]: "/init",
	[NAMESPACE.main]: "/main",
	[NAMESPACE.pet]: "/pet",
}

/**
 * 返回当前窗口 label, 非 Tauri 环境 (纯 vite 调试) 返回 null
 */
export async function getCurrentWindowLabel(): Promise<WindowLabel | null> {
	try {
		const LABEL = getCurrentWebviewWindow().label as WindowLabel
		return LABEL in WINDOW_ROUTES ? LABEL : null
	} catch {
		// 非 Tauri 环境忽略
		return null
	}
}

/**
 * 当前窗口应该跳转到的路由 (按 label), 未知窗口返回 null
 */
export async function getCurrentWindowRoute(): Promise<string | null> {
	const LABEL = await getCurrentWindowLabel()
	return LABEL ? WINDOW_ROUTES[LABEL] : null
}

/**
 * 供 Windows 插件操作指定 label 的窗口对象封装
 */
async function getTarget(label: WindowLabel) {
	const WINDOW = await WebviewWindow.getByLabel(label)
	if (!WINDOW) {
		console.warn(`[window] 未找到窗口: ${label}`)
		return null
	}
	return WINDOW
}

/**
 * 显示并聚焦窗口
 */
export async function showWindow(label: WindowLabel) {
	const WINDOW = await getTarget(label)
	if (WINDOW) {
		await WINDOW.show()
		await WINDOW.setFocus()
		// 桌宠窗口显示后通知其前端加载模型
		if (label === NAMESPACE.pet) {
			await emit("nori:pet-start")
		}
	}
}

/**
 * 隐藏窗口
 */
export async function hideWindow(label: WindowLabel) {
	const WINDOW = await getTarget(label)
	if (WINDOW) await WINDOW.hide()
}

/**
 * 关闭窗口 (label 未知时仅关闭当前窗口)
 */
export async function closeWindow(label?: WindowLabel) {
	if (label) {
		const WINDOW = await getTarget(label)
		if (WINDOW) {
			await WINDOW.close()
			return
		}
	}
	await getCurrentWebviewWindow().close()
}

/**
 * 启动时按当前窗口 label 跳转到对应页面路由
 * 供各窗口挂载后 (如 App.vue onMounted) 调用, 纯浏览器调试时安全跳过
 */
export async function navigateToOwnWindow(router: {
	replace: (path: string) => Promise<unknown>
	currentRoute: { value: { path: string } }
}): Promise<string | null> {
	const TARGET = await getCurrentWindowRoute()
	if (TARGET && router.currentRoute.value.path !== TARGET) {
		await router.replace(TARGET)
	}
	return TARGET
}
