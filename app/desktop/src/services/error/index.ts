/**
 * 全局错误捕获
 *
 * 参考 ClassIsland 的全局兜底思路: Vue errorHandler + window error/unhandledrejection,
 * 统一转发到宿主 write_log 落盘, 让前端错误在 DevTools 之外也可追溯。
 * 复用现有 write_log 桥接命令, 不改桥接协议。
 */
import type {App} from "vue"
import {invoke} from "../host/invoke"

/** 同一首行消息最多转发的次数, 防止渲染循环类错误刷爆日志文件 */
const MAX_REPEAT = 5

const REPEAT_COUNTS = new Map<string, number>()

/**
 * 格式化任意抛出值为可读文本 (Error 带堆栈, 其余 String 化)
 */
const formatError = (error: unknown): string => {
	if (error instanceof Error) {
		return [`${error.name}: ${error.message}`, error.stack ?? ""].filter(Boolean).join("\n")
	}
	return String(error)
}

/**
 * 转发到宿主日志; 同一首行消息限流, 超出后静默丢弃
 */
const forward = async (message: string): Promise<void> => {
	const KEY = message.split("\n")[0] ?? message
	const COUNT = (REPEAT_COUNTS.get(KEY) ?? 0) + 1
	REPEAT_COUNTS.set(KEY, COUNT)
	if (COUNT > MAX_REPEAT) return
	if (COUNT === MAX_REPEAT + 1) console.warn(`[error] 相同错误已达上限, 后续不再转发: ${KEY}`)
	try {
		await invoke("write_log", {level: "error", message})
	} catch (failure) {
		// 上报自身失败只走控制台, 绝不递归触发捕获
		console.error("[error] 错误上报失败:", failure)
		console.error(message)
	}
}

/**
 * 安装全局错误捕获, 在 APP.mount 之前调用
 */
export const installErrorHandlers = (app: App): void => {
	// Vue 组件内的渲染/生命周期/事件处理器异常
	app.config.errorHandler = (error, _instance, info) => {
		void forward(`[vue:${info}] ${formatError(error)}`)
	}
	// errorHandler 覆盖不到的同步脚本错误与资源加载失败
	window.addEventListener("error", (event) => {
		const DETAIL = event.error instanceof Error ? `\n${formatError(event.error)}` : ""
		void forward(`[window.onerror] ${event.message} @ ${event.filename}:${event.lineno}:${event.colno}${DETAIL}`)
	})
	// 未处理的 Promise 拒绝
	window.addEventListener("unhandledrejection", (event) => {
		void forward(`[unhandledrejection] ${formatError(event.reason)}`)
	})
}
