import {describe, expect, it, vi} from "vitest"
import {ref} from "vue"
import {useLipSyncPlugin} from "../../../src/services/live2d/plugins/lip-sync"

describe("lip-sync 口型同步插件", () => {
	const makeModel = () => {
		const setCalls: Array<[string, number]> = []
		return {
			setCalls,
			model: {
				getParameterValueById: vi.fn(() => 0.1),
				setParameterValueById: vi.fn((id: string, value: number) => {
					setCalls.push([id, value])
				}),
			},
		}
	}

	const makeCtx = (model: Record<string, unknown>, timeDelta = 1 / 60) => ({
		model,
		now: 0,
		timeDelta,
	})

	it("说话时接管 ParamMouthOpenY", () => {
		const {model, setCalls} = makeModel()
		const mouth = ref(0.6)
		const speaking = ref(true)
		const plugin = useLipSyncPlugin(mouth, speaking)

		plugin(makeCtx(model))

		expect(setCalls.at(-1)?.[0]).toBe("ParamMouthOpenY")
		expect(setCalls.at(-1)?.[1]).toBeCloseTo(0.6)
	})

	it("静音后先保持闭口, 释放完成后交还控制权", () => {
		const {model, setCalls} = makeModel()
		const mouth = ref(0)
		const speaking = ref(false)
		const plugin = useLipSyncPlugin(mouth, speaking)

		// 先说话一帧, 进入释放/保持状态
		speaking.value = true
		plugin(makeCtx(model, 0.05))
		speaking.value = false
		const CALLS_AFTER_SPEECH = setCalls.length

		// 连续推进 1.5s (超过 200ms 释放 + 500ms 保持), 期间应有写入
		for (let i = 0; i < 30; i++) {
			plugin(makeCtx(model, 0.05))
		}
		expect(setCalls.length).toBeGreaterThan(CALLS_AFTER_SPEECH)

		// 释放+保持完成后插件不再写入
		const CALLS_BEFORE_EXTRA = setCalls.length
		for (let i = 0; i < 5; i++) {
			plugin(makeCtx(model, 0.05))
		}
		expect(setCalls.length).toBe(CALLS_BEFORE_EXTRA)
	})
})