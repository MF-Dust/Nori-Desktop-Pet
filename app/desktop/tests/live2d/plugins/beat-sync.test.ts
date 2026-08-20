import {describe, expect, it} from "vitest"
import {createBeatSyncController} from "../../../src/services/live2d/plugins/beat-sync"

describe("beat-sync 节拍控制器", () => {
	it("首次节拍只初始化, 后续节拍生成摆动目标", () => {
		const BEAT = createBeatSyncController()

		BEAT.triggerBeat(1000)
		expect(BEAT.targetY.value).toBe(0)
		expect(BEAT.targetZ.value).toBe(0)

		// 0.6s 后第二拍
		BEAT.triggerBeat(1600)
		// 推进到拍中段, 目标值应开始偏离初始位置
		BEAT.updateTargets(1.75)
		expect(BEAT.targetY.value).not.toBe(0)
		expect(BEAT.targetZ.value).not.toBe(0)
	})

	it("切换节奏风格生效", () => {
		const BEAT = createBeatSyncController()
		expect(BEAT.getStyle()).toBe("sway-sine")

		BEAT.setStyle("punchy-v")
		expect(BEAT.getStyle()).toBe("punchy-v")
	})

	it("长时间无节拍后释放回初始位置", () => {
		const BEAT = createBeatSyncController()

		BEAT.triggerBeat(1000)
		BEAT.triggerBeat(1600)
		BEAT.updateTargets(1.75)
		expect(BEAT.targetY.value).not.toBe(0)

		// 超过 releaseDelay (1.8s) 后应回到 0
		BEAT.updateTargets(10)
		expect(BEAT.targetY.value).toBe(0)
		expect(BEAT.targetZ.value).toBe(0)
	})

	it("支持数字时间戳与默认时间戳", () => {
		const BEAT = createBeatSyncController()
		expect(() => BEAT.triggerBeat(2000)).not.toThrow()
		expect(() => BEAT.triggerBeat(null)).not.toThrow()
	})
})