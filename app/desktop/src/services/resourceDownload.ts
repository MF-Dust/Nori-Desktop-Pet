import {reactive, readonly} from "vue"
import {invoke} from "../services/host/invoke"
import {listen, type UnlistenFn} from "../services/host/event"

/**
 * 资源下载 / 检查的通用 Service.
 *
 * 不绑定具体资源类型与界面文案: 只暴露结构化的舞台状态 (step / percent / 字节进度),
 * 由使用者根据 step 自行映射文案与界面. 这样 live2d / 未来的 voice、image 等资源都能复用.
 */

/**
 * 资源流程阶段
 */
export type ResourceStep =
	| "idle" // 尚未开始
	| "downloading" // 下载中 (percent 实时)
	| "download-done" // 下载完成
	| "extracting" // 解压中
	| "done" // 就绪
	| "installed" // 检测到已安装
	| "error" // 出错

/**
 * 对外暴露的响应式状态
 */
export interface ResourceDownloadState {
	// 当前阶段
	step: ResourceStep
	// 进度百分比 (0-100)
	percent: number
	// 已下载字节, downloading 阶段有值 (未知总大小时按此折线)
	downloaded: number | null
	// 总字节, downloading 阶段有值; 总大小未知时为 null
	total: number | null
	// 错误信息, error 阶段有值
	message: string | null
}

// 后端 resource-download 事件载荷
interface ResourceDownloadEventPayload {
	resourceType: string
	step: string
	progress?: number | null
	downloaded?: number | null
	total?: number | null
	message?: string | null
}

// 中间态最小展示时长 (ms), 保证文案能看清再进入下一步
const DOWNLOAD_DONE_HOLD_MS = 700
const EXTRACTING_HOLD_MS = 500
const sleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms))

// 事件名, 与后端 commands::ensure_resource 保持一致
const EVENT_NAME = "resource-download"

/**
 * 创建一份资源下载控制器
 */
export const createResourceDownload = () => {
	const STATE = reactive<ResourceDownloadState>({
		step: "idle",
		percent: 0,
		downloaded: null,
		total: null,
		message: null,
	})
	// 事件监听句柄与顺序队列: 保证中间态文案停留可见
	let unlisten: UnlistenFn | null = null
	const pending: ResourceDownloadEventPayload[] = []
	let processing = false
	const flush = async (): Promise<void> => {
		if (processing) return
		processing = true
		while (pending.length > 0) {
			const ev = pending.shift()!
			await apply(ev)
		}
		processing = false
	}
	const push = (payload: ResourceDownloadEventPayload): void => {
		pending.push(payload)
		void flush()
	}
	// 处理后端事件: 更新状态, 并在需要停顿的阶段卡住以展示文案
	const apply = async (payload: ResourceDownloadEventPayload): Promise<void> => {
		const {step, downloaded, total, message} = payload
		switch (step) {
			case "downloading":
				STATE.step = "downloading"
				STATE.downloaded = downloaded ?? null
				STATE.total = total ?? null
				STATE.percent = calculatePercent(downloaded, total)
				STATE.message = null
				break
			case "download-done":
				// 先保持"下载完成"文案可见, 再进入解压
				await sleep(DOWNLOAD_DONE_HOLD_MS)
				STATE.step = "download-done"
				STATE.percent = 100
				await sleep(DOWNLOAD_DONE_HOLD_MS)
				break
			case "extracting":
				STATE.step = "extracting"
				STATE.message = null
				await sleep(EXTRACTING_HOLD_MS)
				break
			case "done":
				STATE.step = "done"
				STATE.percent = 100
				STATE.downloaded = null
				STATE.total = null
				STATE.message = null
				break
			case "installed":
				STATE.step = "installed"
				STATE.percent = 100
				STATE.message = null
				break
			case "error":
				STATE.step = "error"
				STATE.message = message ?? null
				STATE.percent = 100
				break
			default:
				break
		}
	}

	const onEvent = (e: { payload: ResourceDownloadEventPayload }): void => {
		push(e.payload)
	}

	const reset = (): void => {
		STATE.step = "idle"
		STATE.percent = 0
		STATE.downloaded = null
		STATE.total = null
		STATE.message = null
	}

	// 订阅事件并触发资源流程
	const run = async (command: "ensure_resource" | "update_resource", resourceType: string, name: string): Promise<void> => {
		reset()
		unlisten?.()
		unlisten = null
		unlisten = await listen<ResourceDownloadEventPayload>(EVENT_NAME, onEvent)
		try {
			await invoke(command, {resourceType, name})
		} catch (error) {
			console.error(`${command} 失败: ${resourceType}/${name}`, error)
			STATE.step = "error"
			STATE.message = String(error)
		}
	}

	const ensure = async (resourceType: string, name: string): Promise<void> => run("ensure_resource", resourceType, name)
	const update = async (resourceType: string, name: string): Promise<void> => run("update_resource", resourceType, name)

	// 仅检查资源是否已安装, 不触发下载. 返回是否已安装.
	// 检查成功会推进 STATE.step (已安装 → "installed"), 避免已安装场景下界面停在"检查中"不动.
	const check = async (resourceType: string, name: string): Promise<boolean> => {
		try {
			const installed = await invoke<boolean>("check_resource", {resourceType, name})
			if (installed) {
				STATE.step = "installed"
				STATE.percent = 100
				STATE.message = null
			}
			return installed
		} catch (error) {
			console.error(`检查资源失败: ${resourceType}/${name}`, error)
			STATE.step = "error"
			STATE.message = String(error)
			return false
		}
	}

	// 停止监听事件
	const stop = (): void => {
		unlisten?.()
		unlisten = null
	}

	return {state: readonly(STATE), ensure, update, check, stop}
}

// 根据字节计算百分比: 有总大小时用比值, 未知总大小时按 MB 折线爬升 (封顶 99)
const calculatePercent = (downloaded: number | null | undefined, total: number | null | undefined): number => {
	if (total && total > 0) {
		return downloaded ? Math.min(99, Math.round((downloaded * 100) / total)) : 0
	}
	if (downloaded != null) {
		return Math.min(99, Math.round(downloaded / (1024 * 1024)))
	}
	return 0
}

/**
 * 字节 → 可读大小 (供展示进度明细)
 * @param bytes 字节数
 */
export const formatBytes = (bytes: number): string => {
	if (!bytes) return "0 B"
	const UNITS = ["B", "KB", "MB", "GB"]
	const i = Math.min(UNITS.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)))
	return (bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 1) + " " + UNITS[i]
}
