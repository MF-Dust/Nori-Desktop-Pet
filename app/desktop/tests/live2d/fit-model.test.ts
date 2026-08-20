import {describe, expect, it} from "vitest"
import {calcFitModel} from "../../src/services/live2d/composables/fit-model"

describe("fit-model 模型适配", () => {
	it("scale=1 时模型完整居中适配视口", () => {
		const NORMALIZED = calcFitModel(
			{width: 400, height: 520},
			{width: 400, height: 520},
		)
		// 400/400 = 1, 520/520 = 1, min = 1
		expect(NORMALIZED.scale).toBeCloseTo(1)
		expect(NORMALIZED.x).toBe(200)
		expect(NORMALIZED.y).toBe(260)
	})

	it("宽高比例不同时取较小缩放", () => {
		const NORMALIZED = calcFitModel(
			{width: 200, height: 520},
			{width: 400, height: 520},
		)
		// 宽度: 200/400 = 0.5; 高度: 520/520 = 1
		expect(NORMALIZED.scale).toBeCloseTo(0.5)
		expect(NORMALIZED.x).toBe(100)
		expect(NORMALIZED.y).toBe(260)
	})

	it("非法尺寸时返回极小缩放而不是 NaN", () => {
		const NORMALIZED = calcFitModel(
			{width: 0, height: 0},
			{width: 400, height: 520},
		)
		expect(Number.isNaN(NORMALIZED.scale)).toBe(false)
		expect(NORMALIZED.scale).toBeGreaterThan(0)
	})
})