/**
 * 自动眨眼插件
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/motion-manager.ts
 * → useMotionUpdatePluginAutoEyeBlink
 *
 * 在空闲动作时接管 ParamEyeLOpen/ParamEyeROpen 的眨眼控制：
 * - 随机间隔 3~8s 眨眼一次
 * - 闭眼 75ms (easeOutQuad), 开眼 150~300ms (easeInQuad)
 * - 当表情系统启用时，与表情值 Multiply 调制
 */
import type {MotionManagerPlugin} from "./index"

// 内联数学工具 (替代 three 依赖)
const clamp01 = (v: number) => Math.min(1, Math.max(0, v))
const easeOutQuad = (t: number) => 1 - (1 - t) * (1 - t)
const easeInQuad = (t: number) => t * t

interface BlinkState {
	phase: "idle" | "closing" | "opening"
	progress: number
	startLeft: number
	startRight: number
	delayMs: number
	openDurationMs: number
}

const BLINK_CLOSE_DURATION = 75
const MIN_BLINK_OPEN_DURATION = 150
const MAX_BLINK_OPEN_DURATION = 300
const MIN_DELAY = 3000
const MAX_DELAY = 8000

const randomRange = (min: number, max: number) => min + Math.random() * (max - min)

const createBlinkState = (): BlinkState => ({
	phase: "idle",
	progress: 0,
	startLeft: 1,
	startRight: 1,
	delayMs: randomRange(MIN_DELAY, MAX_DELAY),
	openDurationMs: randomRange(MIN_BLINK_OPEN_DURATION, MAX_BLINK_OPEN_DURATION),
})

/**
 * 创建自动眨眼插件
 */
export const useAutoBlinkPlugin = (): MotionManagerPlugin => {
	const state = createBlinkState()

	const updateBlink = (dt: number, baseLeft: number, baseRight: number) => {
		// idle: 等待计时
		if (state.phase === "idle") {
			state.delayMs = Math.max(0, state.delayMs - dt)
			if (state.delayMs === 0) {
				state.phase = "closing"
				state.progress = 0
				state.startLeft = baseLeft
				state.startRight = baseRight
			}
			return {eyeLOpen: baseLeft, eyeROpen: baseRight}
		}

		// closing: 闭眼
		if (state.phase === "closing") {
			state.progress = Math.min(1, state.progress + dt / BLINK_CLOSE_DURATION)
			const eased = easeOutQuad(state.progress)
			const eyeLOpen = clamp01(state.startLeft * (1 - eased))
			const eyeROpen = clamp01(state.startRight * (1 - eased))

			if (state.progress >= 1) {
				state.phase = "opening"
				state.progress = 0
				state.openDurationMs = randomRange(MIN_BLINK_OPEN_DURATION, MAX_BLINK_OPEN_DURATION)
			}
			return {eyeLOpen, eyeROpen}
		}

		// opening: 开眼
		state.progress = Math.min(1, state.progress + dt / state.openDurationMs)
		const eased = easeInQuad(state.progress)
		const eyeLOpen = clamp01(state.startLeft * eased)
		const eyeROpen = clamp01(state.startRight * eased)

		if (state.progress >= 1) {
			// 重置
			state.phase = "idle"
			state.progress = 0
			state.delayMs = randomRange(MIN_DELAY, MAX_DELAY)
		}
		return {eyeLOpen, eyeROpen}
	}

	return (ctx) => {
		if (!ctx.isIdleMotion || ctx.handled) return
		if (!ctx.live2dAutoBlinkEnabled) return

		const baseLeft = clamp01(ctx.modelParameters?.leftEyeOpen ?? 1)
		const baseRight = clamp01(ctx.modelParameters?.rightEyeOpen ?? 1)
		const safeDt = ctx.timeDelta * 1000 || 16

		// 读取当前眨眼值 (可能有表情写入)
		const currentLeft = ctx.model.getParameterValueById("ParamEyeLOpen") as number
		const currentRight = ctx.model.getParameterValueById("ParamEyeROpen") as number

		// 如果已几乎闭合 (由表情导致), 跳过眨眼
		if (state.phase === "idle" && currentLeft <= 0.15 && currentRight <= 0.15) {
			// 重置计时器
			state.phase = "idle"
			state.progress = 0
			state.delayMs = randomRange(MIN_DELAY, MAX_DELAY)
			ctx.model.setParameterValueById("ParamEyeLOpen", clamp01(currentLeft * baseLeft))
			ctx.model.setParameterValueById("ParamEyeROpen", clamp01(currentRight * baseRight))
			return
		}

		// 保存预眨眼基准值
		if (state.phase === "idle") {
			state.startLeft = currentLeft
			state.startRight = currentRight
		}

		const wasActive = state.phase !== "idle"
		const {eyeLOpen: blinkL, eyeROpen: blinkR} = updateBlink(safeDt, 1.0, 1.0)

		// 眨眼完成: 恢复预眨眼值
		if (wasActive && state.phase === "idle") {
			ctx.model.setParameterValueById("ParamEyeLOpen", clamp01(state.startLeft * baseLeft))
			ctx.model.setParameterValueById("ParamEyeROpen", clamp01(state.startRight * baseRight))
			ctx.markHandled()
			return
		}

		if (state.phase === "idle") return

		// 眨眼中: 预眨眼值 × 眨眼因子 × 手动基准
		ctx.model.setParameterValueById("ParamEyeLOpen", clamp01(state.startLeft * blinkL * baseLeft))
		ctx.model.setParameterValueById("ParamEyeROpen", clamp01(state.startRight * blinkR * baseRight))
		ctx.markHandled()
	}
}