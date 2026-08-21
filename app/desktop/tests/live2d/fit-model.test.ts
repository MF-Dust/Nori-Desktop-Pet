import {describe, expect, it} from "vitest"
import {
	calcFitModel,
	calculateSafeBaseSize,
	DEFAULT_PET_WIDTH,
	DEFAULT_PET_HEIGHT,
	MAX_PET_BASE_WIDTH,
	MAX_PET_BASE_HEIGHT,
	MIN_PET_BASE_WIDTH,
	MIN_PET_BASE_HEIGHT,
} from "../../src/services/live2d/composables/fit-model"

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

	describe("calculateSafeBaseSize 安全基准尺寸计算", () => {
		it("在合理范围内的模型尺寸直接保留", () => {
			const SIZE = calculateSafeBaseSize(400, 520)
			expect(SIZE.width).toBe(400)
			expect(SIZE.height).toBe(520)
		})

		it("超大 2048x2048 模型缩放到安全基准尺寸，且不超过上限", () => {
			const SIZE = calculateSafeBaseSize(2048, 2048)
			expect(SIZE.width).toBeLessThanOrEqual(MAX_PET_BASE_WIDTH)
			expect(SIZE.height).toBeLessThanOrEqual(MAX_PET_BASE_HEIGHT)
			expect(SIZE.width).toBe(520)
			expect(SIZE.height).toBe(520)
		})

		it("全身立绘模型 (2048x4096, 比例 1:2) 保持等比并限制尺寸", () => {
			const SIZE = calculateSafeBaseSize(2048, 4096)
			expect(SIZE.width).toBe(260)
			expect(SIZE.height).toBe(520)
		})

		it("超宽模型 (4096x2048) 限制最大宽度并在安全高度范围内", () => {
			const SIZE = calculateSafeBaseSize(4096, 2048)
			expect(SIZE.width).toBeLessThanOrEqual(MAX_PET_BASE_WIDTH)
			expect(SIZE.height).toBeGreaterThanOrEqual(MIN_PET_BASE_HEIGHT)
			expect(SIZE.height).toBeLessThanOrEqual(MAX_PET_BASE_HEIGHT)
		})

		it("非法输入 (0, 负数, NaN, undefined) 回退到默认尺寸", () => {
			expect(calculateSafeBaseSize(0, 0)).toEqual({width: DEFAULT_PET_WIDTH, height: DEFAULT_PET_HEIGHT})
			expect(calculateSafeBaseSize(-100, 500)).toEqual({width: DEFAULT_PET_WIDTH, height: DEFAULT_PET_HEIGHT})
			expect(calculateSafeBaseSize(Number.NaN, 500)).toEqual({width: DEFAULT_PET_WIDTH, height: DEFAULT_PET_HEIGHT})
			expect(calculateSafeBaseSize(undefined, undefined)).toEqual({width: DEFAULT_PET_WIDTH, height: DEFAULT_PET_HEIGHT})
		})
	})
})