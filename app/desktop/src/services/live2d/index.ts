import {load, playExpression, setAngle, stop, stopExpression} from "live2d-easy-control"
import {assetUrl} from "./config"

/**
 * Live2D 模型描述
 */
export interface Live2DModelSpec {
	directory: string
	fileBase: string
}

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

	const canvas = (): HTMLCanvasElement | null => canvasEl

	const mount = async (model: Live2DModelSpec, options?: Live2DMountOptions): Promise<void> => {
		if (canvasEl && canvasEl.isConnected) canvasEl.remove()
		canvasEl = null
		await load(buildLoadConfig(model, options))
		canvasEl = document.body.querySelector("canvas")
		if (canvasEl) {
			canvasEl.style.pointerEvents = "none"
		}
	}

	const destroy = async (): Promise<void> => {
		try {
			await stop()
		} catch {
			/* 未加载时忽略 */
		}
		if (canvasEl && canvasEl.isConnected) canvasEl.remove()
		canvasEl = null
	}

	return {
		mount,
		lookAt: (e: MouseEvent, duration?: number) => setAngle(e, duration),
		playExpression: (name: string) => playExpression(name),
		stopExpression: () => stopExpression(),
		destroy,
		canvas,
	}
}

/**
 * Live2D 控制器
 */
export type Live2DController = ReturnType<typeof createLive2D>
