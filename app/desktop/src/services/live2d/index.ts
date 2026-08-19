import {
	getAllMotionsInfo,
	load,
	playExpression,
	playMotion,
	removePointClickEvent,
	removePointMovedEvent,
	setAngle,
	setPointClickEvent,
	setPointMovedEvent,
	stop,
	stopExpression,
} from "live2d-easy-control"
import {assetUrl} from "./config"

/**
 * Live2D 模型描述
 */
export interface Live2DModelSpec {
	directory: string
	fileBase: string
}

/**
 * 动作组: 组名 + 该组下的动作名数组
 * (动作名为 motion3.json 文件名, 不含扩展名, 与模型目录一致)
 */
export interface MotionGroup {
	group: string
	names: string[]
}

/**
 * 动作播放优先级 (与 Cubism 一致)
 * 0 = 无 / 1 = 待机 / 2 = 普通 / 3 = 强制 (可打断当前动作)
 */
export const MOTION_PRIORITY = {
	none: 0,
	idle: 1,
	normal: 2,
	force: 3,
} as const

/**
 * Live2D 挂载选项
 */
export interface Live2DMountOptions {
	/**
	 * 画布 CSS 宽度 (默认 100%)
	 */
	canvasWidth?: string

	/**
	 * 画布 CSS 高度 (默认 100%)
	 */
	canvasHeight?: string
}

// 构建加载配置
const buildLoadConfig = (model: Live2DModelSpec, options: Live2DMountOptions = {}): Record<string, unknown> => ({
	modelDir: model.fileBase,
	resourcesPath: `${assetUrl(`live2d/${model.directory}`)}/`,
	canvasSize: "auto",
	canvasWidth: options.canvasWidth ?? "100%",
	canvasHeight: options.canvasHeight ?? "100%",
})

/**
 * 创建 Live2D 控制器
 *
 * live2d-easy-control 每次 load 都会新建 canvas 并 append 到 body,
 * stop 时不会清理 DOM, 这里负责跨实例的画布清理与获取
 */
export const createLive2D = () => {
	let canvasEl: HTMLCanvasElement | null = null
	let interactionEnabled = false
	let loaded = false

	const canvas = (): HTMLCanvasElement | null => canvasEl

	// 读取当前 WebGL 帧的 alpha, 只把模型实际绘制的像素视为可交互区域
	const isPointOnModel = (clientX: number, clientY: number): boolean => {
		if (!canvasEl || !canvasEl.isConnected) return false
		const RECT = canvasEl.getBoundingClientRect()
		if (RECT.width <= 0 || RECT.height <= 0 || clientX < RECT.left || clientX >= RECT.right || clientY < RECT.top || clientY >= RECT.bottom) {
			return false
		}
		const GL = canvasEl.getContext("webgl2")
		if (!GL) return true
		const X = Math.min(canvasEl.width - 1, Math.max(0, Math.floor((clientX - RECT.left) * canvasEl.width / RECT.width)))
		const Y = Math.min(canvasEl.height - 1, Math.max(0, Math.floor((RECT.bottom - clientY) * canvasEl.height / RECT.height)))
		const PIXEL = new Uint8Array(4)
		try {
			GL.readPixels(X, Y, 1, 1, GL.RGBA, GL.UNSIGNED_BYTE, PIXEL)
			return PIXEL[3] > 8
		} catch {
			// 不支持像素读取时退回画布范围, 保证低端环境仍可操作
			return true
		}
	}

	const mount = async (model: Live2DModelSpec, options?: Live2DMountOptions): Promise<void> => {
		if (loaded) await destroy()
		else if (canvasEl && canvasEl.isConnected) canvasEl.remove()
		canvasEl = null
		interactionEnabled = false
		await load(buildLoadConfig(model, options))
		loaded = true
		canvasEl = document.body.querySelector("canvas")
		if (canvasEl) {
			canvasEl.style.pointerEvents = "auto"
			await setPointMovedEvent()
			await setPointClickEvent()
			interactionEnabled = true
		}
	}

	const destroy = async (): Promise<void> => {
		if (!loaded) {
			if (canvasEl && canvasEl.isConnected) canvasEl.remove()
			canvasEl = null
			return
		}
		if (interactionEnabled) {
			try {
				await removePointClickEvent()
				await removePointMovedEvent()
			} catch {
				/* 未加载时忽略 */
			}
			interactionEnabled = false
		}
		try {
			await stop()
		} catch {
			/* 未加载时忽略 */
		}
		loaded = false
		if (canvasEl && canvasEl.isConnected) canvasEl.remove()
		canvasEl = null
	}

	// 获取模型全部动作组: [{group, names[]}] (模型未加载时返回 null)
	const getMotions = async (): Promise<MotionGroup[] | null> => {
		try {
			return await getAllMotionsInfo()
		} catch {
			return null
		}
	}

	// 按组 + 序号播放动作 (默认强制优先级, 可打断待机/普通动作)
	const playMotionByIndex = async (group: string, no: number, priority = MOTION_PRIORITY.force): Promise<boolean> => {
		try {
			await playMotion(group, no, priority)
			return true
		} catch {
			return false
		}
	}

	// 按动作名播放: 在所有组中查找该名字并播放 (找不到返回 false)
	const playMotionByName = async (name: string, priority = MOTION_PRIORITY.force): Promise<boolean> => {
		const GROUPS = await getMotions()
		if (!GROUPS) return false
		for (const GROUP of GROUPS) {
			const INDEX = GROUP.names.findIndex((n) => n === name)
			if (INDEX >= 0) return playMotionByIndex(GROUP.group, INDEX, priority)
		}
		return false
	}

	return {
		mount,
		lookAt: (e: MouseEvent, duration?: number) => setAngle(e, duration),
		playExpression: (name: string) => playExpression(name),
		stopExpression: () => stopExpression(),
		destroy,
		canvas,
		isPointOnModel,
		getMotions,
		playMotionByIndex,
		playMotionByName,
	}
}

/**
 * Live2D 控制器
 */
export type Live2DController = ReturnType<typeof createLive2D>
