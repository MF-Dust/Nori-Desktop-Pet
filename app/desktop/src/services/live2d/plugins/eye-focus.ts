/**
 * 空闲眼球扫视与头部微动
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/animation.ts
 * + packages/stage-ui-live2d/src/utils/eye-motions.ts
 *
 * 在空闲动作时随机产生眼球扫视和头部微动，让模型看起来更生动。
 * 不依赖 three.js，内联 randFloat/lerp。
 */
import type {MotionManagerPlugin} from "./index"

const randFloat = (min: number, max: number) => Math.random() * (max - min) + min
const lerp = (a: number, b: number, t: number) => a + (b - a) * t

// 眼球扫视间隔分布 (来自 AIRI eye-motions.ts)
const EYE_SACCADE_INT_STEP = 400
const EYE_SACCADE_INT_P: [number, number][] = [
	[0.075, 800],
	[0.110, 0],
	[0.125, 0],
	[0.140, 0],
	[0.125, 0],
	[0.050, 0],
	[0.040, 0],
	[0.030, 0],
	[0.020, 0],
	[1.000, 0],
]
for (let i = 1; i < EYE_SACCADE_INT_P.length; i++) {
	EYE_SACCADE_INT_P[i][0] += EYE_SACCADE_INT_P[i - 1][0]
	EYE_SACCADE_INT_P[i][1] = EYE_SACCADE_INT_P[i - 1][1] + EYE_SACCADE_INT_STEP
}

const randomSaccadeInterval = (): number => {
	const r = Math.random()
	for (const [prob, base] of EYE_SACCADE_INT_P) {
		if (r <= prob) return base + Math.random() * EYE_SACCADE_INT_STEP
	}
	return EYE_SACCADE_INT_P[EYE_SACCADE_INT_P.length - 1][1] + Math.random() * EYE_SACCADE_INT_STEP
}

/**
 * 创建空闲眼球扫视与头部微动插件
 */
export const useIdleEyeFocusPlugin = (): MotionManagerPlugin => {
	let nextSaccadeAt = -1
	let focusTarget: [number, number] = [0, 0]
	let lastSaccadeAt = -1

	return (ctx) => {
		if (!ctx.isIdleMotion || ctx.handled) return
		if (!ctx.live2dForceIdleEyeAnimation) return

		const now = ctx.now

		// 触发新扫视
		if (now >= nextSaccadeAt || now < lastSaccadeAt) {
			focusTarget = [randFloat(-1, 1), randFloat(-1, 0.7)]
			lastSaccadeAt = now
			nextSaccadeAt = now + (randomSaccadeInterval() / 1000)
			ctx.internalModel.focusController.focus(focusTarget[0] * 0.5, focusTarget[1] * 0.5, false)
		}

		// 更新焦点插值
		ctx.internalModel.focusController.update(now - lastSaccadeAt)

		// 直接设置眼球参数
		const coreModel = ctx.internalModel.coreModel as any
		coreModel.setParameterValueById("ParamEyeBallX", lerp(
			coreModel.getParameterValueById("ParamEyeBallX") as number,
			focusTarget[0],
			0.3,
		))
		coreModel.setParameterValueById("ParamEyeBallY", lerp(
			coreModel.getParameterValueById("ParamEyeBallY") as number,
			focusTarget[1],
			0.3,
		))
	}
}