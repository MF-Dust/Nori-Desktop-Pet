/**
 * 模型适配缩放
 *
 * 标准化模型缩放: scale=1 时模型完整居中适配视口,
 * x=canvas.width/2, y=canvas.height/2 (anchor 0.5,0.5).
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/fit-model.ts
 */
export interface CanvasDim {
	width: number
	height: number
}

export interface ModelDim {
	width: number
	height: number
}

export interface NormalizedParams {
	scale: number
	x: number
	y: number
}

/**
 * 计算标准化参数
 * @param canvas 画布容器尺寸 (CSS px)
 * @param model 模型原始画布尺寸 (Live2D canvas 尺寸)
 */
export const calcFitModel = (
	canvas: CanvasDim,
	model: ModelDim,
): NormalizedParams => {
	const heightScale = canvas.height / model.height
	const widthScale = canvas.width / model.width
	const minScale = Math.max(1e-6, Math.min(heightScale, widthScale))

	return {
		scale: minScale,
		x: canvas.width / 2,
		y: canvas.height / 2,
	}
}