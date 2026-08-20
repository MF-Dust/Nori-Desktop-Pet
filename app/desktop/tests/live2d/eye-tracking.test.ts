import {describe, expect, it} from "vitest"
import {calcEyeFocus} from "../../src/services/live2d/composables/eye-tracking"

const BASE_OPTIONS = {
	canvas: {width: 400, height: 520},
	canvasRect: {left: 10, top: 20, width: 400, height: 520} as DOMRect,
	modelNormalizedScale: 2,
	modelWidth: 400,
	modelHeight: 520,
	renderScale: 2,
	modelScale: 1,
	eyeOffsetX: 0,
	eyeOffsetY: 0,
}

describe("eye-tracking 光标焦点映射", () => {
	it("按渲染比例换算世界坐标", () => {
		const FOCUS = calcEyeFocus({x: 110, y: 120}, BASE_OPTIONS)
		expect(FOCUS).not.toBeNull()
		// (110-10)*2 = 200, (120-20)*2 = 200
		expect(FOCUS?.x).toBeCloseTo(200)
		expect(FOCUS?.y).toBeCloseTo(200)
	})

	it("无画布矩形时返回 null", () => {
		const FOCUS = calcEyeFocus({x: 100, y: 100}, {...BASE_OPTIONS, canvasRect: null})
		expect(FOCUS).toBeNull()
	})

	it("眼睛偏移按模型尺寸与缩放换算", () => {
		const FOCUS = calcEyeFocus({x: 110, y: 120}, {
			...BASE_OPTIONS,
			eyeOffsetX: 10, // 10% of modelWidth*normalizedScale*scale
			eyeOffsetY: -5,
		})
		// offsetX = 10/100*400*2*1 = 80
		// offsetY = -5/100*520*2*1 = -52
		expect(FOCUS?.x).toBeCloseTo((110 - 10 + 80) * 2)
		expect(FOCUS?.y).toBeCloseTo((120 - 20 - 52) * 2)
	})
})