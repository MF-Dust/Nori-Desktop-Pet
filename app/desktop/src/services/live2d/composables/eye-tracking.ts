/**
 * 光标坐标 → 模型焦点映射
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/eye-tracking.ts
 */
export interface EyeFocusSource {
	x: number
	y: number
}

export interface EyeFocusOptions {
	canvas: {width: number; height: number} | null
	canvasRect: DOMRect | null
	modelNormalizedScale: number
	modelWidth: number
	modelHeight: number
	renderScale: number
	modelScale: number
	eyeOffsetX: number
	eyeOffsetY: number
}

/**
 * 计算光标位置对应的模型焦点坐标
 * @returns {x, y} 焦点坐标，无效时返回 null
 */
export const calcEyeFocus = (
	source: EyeFocusSource,
	options: EyeFocusOptions,
): {x: number; y: number} | null => {
	const {canvasRect, modelNormalizedScale, modelWidth, modelHeight, renderScale, modelScale, eyeOffsetX, eyeOffsetY} = options

	if (!canvasRect) return null

	const eyeOffset = {
		x: (eyeOffsetX / 100) * modelWidth * modelNormalizedScale * modelScale,
		y: (eyeOffsetY / 100) * modelHeight * modelNormalizedScale * modelScale,
	}

	return {
		x: (source.x - canvasRect.left + eyeOffset.x) * renderScale,
		y: (source.y - canvasRect.top + eyeOffset.y) * renderScale,
	}
}