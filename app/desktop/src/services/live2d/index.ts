import {load, setAngle, stop} from "live2d-easy-control"
import {assetUrl} from "./config"

/**
 * Live2D 模型描述
 */
export interface Live2DModelSpec {
	directory: string
	fileBase: string
}

// 构建加载配置
const buildLoadConfig = (model: Live2DModelSpec): Record<string, unknown> => ({
	modelDir: model.fileBase,
	resourcesPath: `${assetUrl(`live2d/${model.directory}`)}/`,
	canvasSize: "auto",
	canvasWidth: "100%",
	canvasHeight: "100%",
})

/**
 * 创建 Live2D 控制器
 */
export const createLive2D = () => {
	const mount = async (model: Live2DModelSpec): Promise<void> => {
		await load(buildLoadConfig(model))
	}

	const destroy = async (): Promise<void> => {
		try {
			await stop()
		} catch {
			/* 未加载时忽略 */
		}
	}

	return {
		mount,
		lookAt: (e: MouseEvent, duration?: number) => setAngle(e, duration),
		destroy,
	}
}

/**
 * Live2D 控制器
 */
export type Live2DController = ReturnType<typeof createLive2D>
