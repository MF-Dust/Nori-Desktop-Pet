/**
 * 空闲动画禁用插件
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/motion-manager.ts
 * → useMotionUpdatePluginIdleDisable
 *
 * 当空闲动画被禁用时，停止所有运动但保持眨眼和空闲眼动。
 */
import type {MotionManagerPlugin} from "./index"

/**
 * 创建空闲动画禁用插件
 * 注册为 pre 阶段插件
 */
export const useIdleDisablePlugin = (): MotionManagerPlugin => {
	return (ctx) => {
		if (!ctx.live2dIdleAnimationEnabled && ctx.isIdleMotion) {
			ctx.motionManager.stopAllMotions()

			// 保持自动化眼动
			if (ctx.internalModel.eyeBlink != null) {
				ctx.internalModel.eyeBlink.updateParameters(ctx.model, ctx.timeDelta / 1000)
			}

			// 应用手动眼值
			const baseLeft = ctx.modelParameters?.leftEyeOpen ?? 1
			const baseRight = ctx.modelParameters?.rightEyeOpen ?? 1
			ctx.model.setParameterValueById("ParamEyeLOpen", baseLeft)
			ctx.model.setParameterValueById("ParamEyeROpen", baseRight)

			ctx.markHandled()
		}
	}
}