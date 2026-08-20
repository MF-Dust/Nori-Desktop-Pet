/**
 * 节拍同步插件
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/beat-sync.ts
 *
 * Spring-Mass-Damper 物理模型驱动 ParamAngleX/Y/Z 跟随节拍摆动。
 * 4 种节奏模式: punchy-v / balanced-v / swing-lr / sway-sine
 * 暴露 triggerBeat() 供外部节拍源调用。
 */
import type {MotionManagerPlugin} from "./index"
import {ref, type Ref} from "vue"

type BeatStylePattern = "v" | "swing" | "sway"
export type BeatSyncStyleName = "punchy-v" | "balanced-v" | "swing-lr" | "sway-sine"

interface BeatStyleConfig {
	topYaw: number
	topRoll: number
	bottomDip: number
	pattern: BeatStylePattern
	swingLift?: number
}

interface BeatSegment {
	start: number
	duration: number
	fromY: number
	fromZ: number
	toY: number
	toZ: number
}

const defaultStyles: Record<BeatSyncStyleName, BeatStyleConfig> = {
	"punchy-v": {topYaw: 10, topRoll: 8, bottomDip: 4, pattern: "v"},
	"balanced-v": {topYaw: 6, topRoll: 0, bottomDip: 6, pattern: "v"},
	"swing-lr": {topYaw: 8, topRoll: 0, bottomDip: 6, swingLift: 8, pattern: "swing"},
	"sway-sine": {topYaw: 10, topRoll: 0, bottomDip: 0, swingLift: 10, pattern: "sway"},
}

export interface BeatSyncController {
	targetX: Ref<number>
	targetY: Ref<number>
	targetZ: Ref<number>
	velocityX: Ref<number>
	velocityY: Ref<number>
	velocityZ: Ref<number>
	updateTargets: (now: number) => void
	triggerBeat: (timestamp?: number | null) => void
	setStyle: (style: BeatSyncStyleName) => void
	getStyle: () => BeatSyncStyleName
}

/**
 * 创建节拍同步控制器
 */
export const createBeatSyncController = (): BeatSyncController => {
	const targetX = ref(0)
	const targetY = ref(0)
	const targetZ = ref(0)
	const velocityX = ref(0)
	const velocityY = ref(0)
	const velocityZ = ref(0)
	const segments: BeatSegment[] = []
	const currentTopSide = ref<"left" | "right">("left")
	const primed = ref(false)
	const patternStarted = ref(false)
	const lastBeatTimestamp = ref<number | null>(null)
	const style = ref<BeatSyncStyleName>("sway-sine")
	const baseY = ref(0)
	const baseZ = ref(0)

	const lerp = (from: number, to: number, t: number) => from + (to - from) * t
	const easeOutCubic = (t: number) => 1 - (1 - t) ** 3

	const getStyleConfig = (): BeatStyleConfig => defaultStyles[style.value] || defaultStyles["punchy-v"]

	const getTopPose = (side: "left" | "right") => {
		const {topYaw, topRoll, swingLift, pattern} = getStyleConfig()
		const direction = side === "left" ? -1 : 1
		const zOffset = (pattern === "swing" || pattern === "sway") ? (swingLift ?? topRoll) : topRoll
		return {
			y: baseY.value + direction * topYaw,
			z: baseZ.value + (pattern === "swing" || pattern === "sway" ? zOffset : direction * zOffset),
		}
	}

	const getBottomPose = () => {
		const {bottomDip} = getStyleConfig()
		return {y: baseY.value, z: baseZ.value - bottomDip}
	}

	const releaseDelayMs = 1800

	const updateTargets = (now: number) => {
		let currentY = targetY.value || baseY.value
		let currentZ = targetZ.value || baseZ.value

		while (segments.length) {
			const segment = segments[0]
			if (now < segment.start) {
				currentY = segment.fromY
				currentZ = segment.fromZ
				break
			}
			const progress = Math.min(1, (now - segment.start) / Math.max(segment.duration, 1))
			const eased = easeOutCubic(progress)
			currentY = lerp(segment.fromY, segment.toY, eased)
			currentZ = lerp(segment.fromZ, segment.toZ, eased)
			if (progress >= 1) {
				segments.shift()
				continue
			}
			break
		}

		const lastBeat = lastBeatTimestamp.value
		const timeSinceBeat = primed.value && lastBeat != null ? (now - lastBeat) : Infinity
		const shouldRelease = primed.value && !segments.length && timeSinceBeat > releaseDelayMs / 1000

		if (shouldRelease) {
			primed.value = false
			patternStarted.value = false
			currentTopSide.value = "left"
			segments.length = 0
			lastBeatTimestamp.value = null
			currentY = baseY.value
			currentZ = baseZ.value
			velocityY.value *= 0.5
			velocityZ.value *= 0.5
		}

		targetY.value = currentY
		targetZ.value = currentZ
	}

	const triggerBeat = (timestamp?: number | null) => {
		const now = timestamp != null && Number.isFinite(timestamp)
			? Number(timestamp)
			: (typeof performance !== "undefined" ? performance.now() : Date.now())
		const nowSeconds = now / 1000

		updateTargets(nowSeconds)

		baseY.value = targetY.value || 0
		baseZ.value = targetZ.value || 0

		if (!primed.value) {
			primed.value = true
			lastBeatTimestamp.value = nowSeconds
			return
		}

		const interval = lastBeatTimestamp.value != null
			? Math.min(2, Math.max(0.22, nowSeconds - lastBeatTimestamp.value))
			: 0.6
		lastBeatTimestamp.value = nowSeconds
		const halfDuration = Math.max(0.08, interval / 2)

		const startPose = {y: targetY.value, z: targetZ.value}
		segments.length = 0

		const styleConfig = getStyleConfig()
		const nextSide = currentTopSide.value === "left" ? "right" : "left"

		if (styleConfig.pattern === "v") {
			if (!patternStarted.value) {
				const topPose = getTopPose("left")
				segments.push({
					start: nowSeconds,
					duration: halfDuration,
					fromY: startPose.y,
					fromZ: startPose.z,
					toY: topPose.y,
					toZ: topPose.z,
				})
				patternStarted.value = true
				currentTopSide.value = "left"
				return
			}

			const bottomPose = getBottomPose()
			const nextTopPose = getTopPose(nextSide)
			segments.push({
				start: nowSeconds,
				duration: halfDuration,
				fromY: startPose.y,
				fromZ: startPose.z,
				toY: bottomPose.y,
				toZ: bottomPose.z,
			})
			segments.push({
				start: nowSeconds + halfDuration,
				duration: halfDuration,
				fromY: bottomPose.y,
				fromZ: bottomPose.z,
				toY: nextTopPose.y,
				toZ: nextTopPose.z,
			})
			currentTopSide.value = nextSide
		} else if (styleConfig.pattern === "swing") {
			const sidePose = getTopPose(currentTopSide.value)
			const oppositePose = getTopPose(nextSide)
			const sideDuration = Math.max(0.06, interval * 0.35)
			const crossDuration = Math.max(0.06, interval - sideDuration)

			segments.push({
				start: nowSeconds,
				duration: sideDuration,
				fromY: startPose.y,
				fromZ: startPose.z,
				toY: sidePose.y,
				toZ: sidePose.z,
			})
			segments.push({
				start: nowSeconds + sideDuration,
				duration: crossDuration,
				fromY: sidePose.y,
				fromZ: sidePose.z,
				toY: oppositePose.y,
				toZ: oppositePose.z,
			})
			patternStarted.value = true
			currentTopSide.value = nextSide
		} else if (styleConfig.pattern === "sway") {
			const sidePose = getTopPose(currentTopSide.value)
			const oppositePose = getTopPose(nextSide)
			const lift = styleConfig.swingLift ?? 10

			if (!patternStarted.value) {
				segments.push({
					start: nowSeconds,
					duration: halfDuration,
					fromY: startPose.y,
					fromZ: startPose.z,
					toY: sidePose.y,
					toZ: sidePose.z,
				})
				patternStarted.value = true
				currentTopSide.value = currentTopSide.value
				return
			}

			const leg1 = Math.max(0.06, interval * 0.5)
			const leg2 = Math.max(0.06, interval - leg1)

			segments.push({
				start: nowSeconds,
				duration: leg1,
				fromY: startPose.y,
				fromZ: startPose.z,
				toY: 0,
				toZ: baseZ.value + lift,
			})
			segments.push({
				start: nowSeconds + leg1,
				duration: leg2,
				fromY: 0,
				fromZ: baseZ.value + lift,
				toY: oppositePose.y,
				toZ: oppositePose.z,
			})
			patternStarted.value = true
			currentTopSide.value = nextSide
		}
	}

	return {
		targetX,
		targetY,
		targetZ,
		velocityX,
		velocityY,
		velocityZ,
		triggerBeat,
		setStyle: (s: BeatSyncStyleName) => { style.value = s },
		getStyle: () => style.value,
		updateTargets,
	}
}

/**
 * 创建节拍同步插件
 * 注册为 pre 阶段插件
 */
export const useBeatSyncPlugin = (beatSync: BeatSyncController): MotionManagerPlugin => {
	const stiffness = 120
	const damping = 16
	const mass = 1

	return (ctx) => {
		if (!ctx.live2dBeatSyncEnabled || !ctx.live2dIdleAnimationEnabled) return

		beatSync.updateTargets(ctx.now)

		// Semi-implicit Euler
		let paramAngleX = ctx.model.getParameterValueById("ParamAngleX") as number
		let paramAngleY = ctx.model.getParameterValueById("ParamAngleY") as number
		let paramAngleZ = ctx.model.getParameterValueById("ParamAngleZ") as number

		// X
		{
			const target = beatSync.targetX.value
			const pos = paramAngleX
			const vel = beatSync.velocityX.value
			const accel = (stiffness * (target - pos) - damping * vel) / mass
			beatSync.velocityX.value = vel + accel * ctx.timeDelta
			paramAngleX = pos + beatSync.velocityX.value * ctx.timeDelta
			if (Math.abs(target - paramAngleX) < 0.01 && Math.abs(beatSync.velocityX.value) < 0.01) {
				paramAngleX = target
				beatSync.velocityX.value = 0
			}
		}

		// Y
		{
			const target = beatSync.targetY.value
			const pos = paramAngleY
			const vel = beatSync.velocityY.value
			const accel = (stiffness * (target - pos) - damping * vel) / mass
			beatSync.velocityY.value = vel + accel * ctx.timeDelta
			paramAngleY = pos + beatSync.velocityY.value * ctx.timeDelta
			if (Math.abs(target - paramAngleY) < 0.01 && Math.abs(beatSync.velocityY.value) < 0.01) {
				paramAngleY = target
				beatSync.velocityY.value = 0
			}
		}

		// Z
		{
			const target = beatSync.targetZ.value
			const pos = paramAngleZ
			const vel = beatSync.velocityZ.value
			const accel = (stiffness * (target - pos) - damping * vel) / mass
			beatSync.velocityZ.value = vel + accel * ctx.timeDelta
			paramAngleZ = pos + beatSync.velocityZ.value * ctx.timeDelta
			if (Math.abs(target - paramAngleZ) < 0.01 && Math.abs(beatSync.velocityZ.value) < 0.01) {
				paramAngleZ = target
				beatSync.velocityZ.value = 0
			}
		}

		ctx.model.setParameterValueById("ParamAngleX", paramAngleX)
		ctx.model.setParameterValueById("ParamAngleY", paramAngleY)
		ctx.model.setParameterValueById("ParamAngleZ", paramAngleZ)
	}
}