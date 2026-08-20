/**
 * 口型同步插件
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/motion-manager.ts
 * → useMotionUpdatePluginLipSync
 *
 * 说话时接管 ParamMouthOpenY，静音后 200ms 平滑释放 + 500ms 闭口保持。
 * 在 final 阶段执行。
 */
import type {MotionManagerPlugin} from "./index"
import {type Ref} from "vue"

const RELEASE_DURATION_MS = 200
const HANDOFF_HOLD_MS = 500

const smoothstep = (t: number) => t * t * (3 - 2 * t)

/**
 * 创建口型同步插件
 * @param mouthOpenSize 嘴形张开度 0~1
 * @param nowSpeaking 是否正在说话
 */
export const useLipSyncPlugin = (
	mouthOpenSize: Ref<number>,
	nowSpeaking: Ref<boolean>,
): MotionManagerPlugin => {
	let releaseRemainingMs = 0
	let handoffRemainingMs = 0
	let lastForcedValue = 0

	return (ctx) => {
		if (nowSpeaking.value) {
			lastForcedValue = mouthOpenSize.value
			releaseRemainingMs = RELEASE_DURATION_MS
			handoffRemainingMs = HANDOFF_HOLD_MS
			ctx.model.setParameterValueById("ParamMouthOpenY", mouthOpenSize.value)
			return
		}

		if (releaseRemainingMs <= 0) {
			if (handoffRemainingMs > 0) {
				handoffRemainingMs = Math.max(0, handoffRemainingMs - ctx.timeDelta * 1000)
				ctx.model.setParameterValueById("ParamMouthOpenY", 0)
			}
			return
		}

		releaseRemainingMs = Math.max(0, releaseRemainingMs - ctx.timeDelta * 1000)
		const blend = smoothstep(1 - releaseRemainingMs / RELEASE_DURATION_MS)

		const motionValue = ctx.model.getParameterValueById("ParamMouthOpenY") as number
		const blended = lastForcedValue * (1 - blend) + motionValue * blend

		ctx.model.setParameterValueById("ParamMouthOpenY", blended)
	}
}