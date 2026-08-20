import {describe, expect, it, vi} from "vitest"
import {useAutoBlinkPlugin} from "../../../src/services/live2d/plugins/auto-blink"

describe("auto-blink 自动眨眼插件", () => {
	const makeCtx = () => {
		const model = {
			getParameterValueById: vi.fn(() => 1),
			setParameterValueById: vi.fn(),
		}
		let handled = false
		return {
			model,
			ctx: {
				model,
				now: 0,
				timeDelta: 0.016,
				modelParameters: {leftEyeOpen: 1, rightEyeOpen: 1},
				live2dAutoBlinkEnabled: true,
				live2dForceAutoBlinkEnabled: true,
				isIdleMotion: true,
				handled: false,
				markHandled: () => { handled = true },
			},
			get handled() { return handled },
		}
	}

	const lastEyeValue = (ctx: ReturnType<typeof makeCtx>) => {
		const CALLS = ctx.model.setParameterValueById.mock.calls as Array<[string, number]>
		for (let i = CALLS.length - 1; i >= 0; i--) {
			if (CALLS[i][0] === "ParamEyeLOpen") return CALLS[i][1]
		}
		return 1
	}

	it("非空闲动作时不干扰", () => {
		const STATE = makeCtx()
		STATE.ctx.isIdleMotion = false
		const plugin = useAutoBlinkPlugin()
		plugin(STATE.ctx)
		expect(STATE.model.setParameterValueById).not.toHaveBeenCalled()
	})

	it("禁用自动眨眼时直接返回", () => {
		const STATE = makeCtx()
		STATE.ctx.live2dAutoBlinkEnabled = false
		const plugin = useAutoBlinkPlugin()
		plugin(STATE.ctx)
		expect(STATE.model.setParameterValueById).not.toHaveBeenCalled()
	})

	it("持续推进帧会在随机间隔后发生眨眼", () => {
		const STATE = makeCtx()
		const plugin = useAutoBlinkPlugin()

		let blinked = false
		for (let i = 0; i < 500; i++) {
			// 每帧推进 100ms, 3~8s 内必然触发眨眼
			STATE.ctx.timeDelta = 0.1
			plugin(STATE.ctx)
			const VALUE = lastEyeValue(STATE)
			if (VALUE < 0.99) {
				blinked = true
				break
			}
		}
		expect(blinked).toBe(true)
	})
})