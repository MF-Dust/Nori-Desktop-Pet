import {describe, expect, it} from "vitest"
import {
	calculateModelViewportRect,
	clampNormalizedRect,
	clientToModelNormalizedPoint,
	containerToModelNormalizedPoint,
	createDefaultInteractionConfig,
	createDefaultInteractionRegion,
	findHitRegion,
	getRegionLocalPoint,
	isPointInNormalizedBounds,
	isPointInRegion,
	modelNormalizedToContainerRect,
	normalizeRectFromPoints,
	validateRegionBindings,
} from "../../src/services/live2d/interactions"
import type {InteractionRegion} from "../../src/services/runtime/types"

describe("Live2D 自定义交互区域纯函数", () => {
	describe("clampNormalizedRect", () => {
		it("保留合法的归一化矩形", () => {
			const rect = {x: 0.1, y: 0.2, width: 0.3, height: 0.4}
			const clamped = clampNormalizedRect(rect)
			expect(clamped).toEqual(rect)
		})

		it("裁剪负坐标到 0", () => {
			const rect = {x: -0.2, y: -0.1, width: 0.5, height: 0.5}
			const clamped = clampNormalizedRect(rect)
			expect(clamped.x).toBe(0)
			expect(clamped.y).toBe(0)
			expect(clamped.width).toBe(0.5)
			expect(clamped.height).toBe(0.5)
		})

		it("防止越过右下边界 x + width <= 1", () => {
			const rect = {x: 0.8, y: 0.7, width: 0.5, height: 0.6}
			const clamped = clampNormalizedRect(rect)
			expect(clamped.x).toBe(0.8)
			expect(clamped.y).toBe(0.7)
			expect(clamped.width).toBeCloseTo(0.2, 5)
			expect(clamped.height).toBeCloseTo(0.3, 5)
		})

		it("负宽高重置为 0", () => {
			const rect = {x: 0.5, y: 0.5, width: -0.2, height: -0.3}
			const clamped = clampNormalizedRect(rect)
			expect(clamped.width).toBe(0)
			expect(clamped.height).toBe(0)
		})
	})

	describe("normalizeRectFromPoints", () => {
		it("支持正向拖拽", () => {
			const p1 = {x: 0.2, y: 0.3}
			const p2 = {x: 0.6, y: 0.8}
			const rect = normalizeRectFromPoints(p1, p2)
			expect(rect.x).toBeCloseTo(0.2, 5)
			expect(rect.y).toBeCloseTo(0.3, 5)
			expect(rect.width).toBeCloseTo(0.4, 5)
			expect(rect.height).toBeCloseTo(0.5, 5)
		})

		it("支持反向拖拽 (右下到左上)", () => {
			const p1 = {x: 0.7, y: 0.9}
			const p2 = {x: 0.3, y: 0.4}
			const rect = normalizeRectFromPoints(p1, p2)
			expect(rect.x).toBeCloseTo(0.3, 5)
			expect(rect.y).toBeCloseTo(0.4, 5)
			expect(rect.width).toBeCloseTo(0.4, 5)
			expect(rect.height).toBeCloseTo(0.5, 5)
		})

		it("拖拽出边界时自动限制在 [0, 1]", () => {
			const p1 = {x: -0.5, y: -0.2}
			const p2 = {x: 1.5, y: 1.2}
			const rect = normalizeRectFromPoints(p1, p2)
			expect(rect.x).toBe(0)
			expect(rect.y).toBe(0)
			expect(rect.width).toBe(1)
			expect(rect.height).toBe(1)
		})
	})

	describe("calculateModelViewportRect", () => {
		it("按模型比例居中计算视口 (容器 400x520, 模型 400x520, userScale 1)", () => {
			const viewport = calculateModelViewportRect(
				{width: 400, height: 520},
				{width: 400, height: 520},
				1,
			)
			expect(viewport.left).toBe(0)
			expect(viewport.top).toBe(0)
			expect(viewport.width).toBe(400)
			expect(viewport.height).toBe(520)
		})

		it("宽容器下以高度贴合并水平居中", () => {
			const viewport = calculateModelViewportRect(
				{width: 600, height: 500},
				{width: 400, height: 500},
				1,
			)
			expect(viewport.width).toBe(400)
			expect(viewport.height).toBe(500)
			expect(viewport.left).toBe(100)
			expect(viewport.top).toBe(0)
		})

		it("考虑 userScale 缩放", () => {
			const viewport = calculateModelViewportRect(
				{width: 400, height: 500},
				{width: 400, height: 500},
				1.5,
			)
			expect(viewport.width).toBe(600)
			expect(viewport.height).toBe(750)
			expect(viewport.left).toBe(-100)
			expect(viewport.top).toBe(-125)
		})
	})

	describe("坐标转换 (client/container 与 normalized)", () => {
		const viewport = {left: 50, top: 100, width: 200, height: 300}
		const containerBounds = {left: 10, top: 20}

		it("containerToModelNormalizedPoint 计算正确的归一化点", () => {
			// 视口左上角 (50, 100)
			const pTopLeft = containerToModelNormalizedPoint(50, 100, viewport)
			expect(pTopLeft).toEqual({x: 0, y: 0})

			// 视口中心 (150, 250)
			const pCenter = containerToModelNormalizedPoint(150, 250, viewport)
			expect(pCenter).toEqual({x: 0.5, y: 0.5})

			// 视口右下角 (250, 400)
			const pBottomRight = containerToModelNormalizedPoint(250, 400, viewport)
			expect(pBottomRight).toEqual({x: 1, y: 1})
		})

		it("clientToModelNormalizedPoint 结合容器边界计算归一化点", () => {
			// clientX = 10 + 150 = 160, clientY = 20 + 250 = 270
			const point = clientToModelNormalizedPoint(160, 270, containerBounds, viewport)
			expect(point).toEqual({x: 0.5, y: 0.5})
		})

		it("modelNormalizedToContainerRect 正向转换为像素矩形", () => {
			const normRect = {x: 0.2, y: 0.3, width: 0.5, height: 0.4}
			const pixelRect = modelNormalizedToContainerRect(normRect, viewport)
			expect(pixelRect.left).toBe(50 + 0.2 * 200) // 90
			expect(pixelRect.top).toBe(100 + 0.3 * 300) // 190
			expect(pixelRect.width).toBe(0.5 * 200) // 100
			expect(pixelRect.height).toBe(0.4 * 300) // 120
		})
	})

	describe("isPointInNormalizedBounds & isPointInRegion", () => {
		it("判断归一化点是否在 [0, 1] 内", () => {
			expect(isPointInNormalizedBounds({x: 0.5, y: 0.5})).toBe(true)
			expect(isPointInNormalizedBounds({x: -0.1, y: 0.5})).toBe(false)
			expect(isPointInNormalizedBounds({x: 0.5, y: 1.1})).toBe(false)
		})

		it("判断点是否在区域内部", () => {
			const region: InteractionRegion = {
				id: "r1",
				name: "头部",
				reactionMode: "local",
				rect: {x: 0.2, y: 0.1, width: 0.6, height: 0.4},
				motion: {mode: "none"},
				expression: {mode: "none"},
			}
			expect(isPointInRegion(region, {x: 0.5, y: 0.3})).toBe(true)
			expect(isPointInRegion(region, {x: 0.1, y: 0.3})).toBe(false)
		})
	})

	describe("findHitRegion (面积最小优先重叠判定)", () => {
		const largeBody: InteractionRegion = {
			id: "body",
			name: "身体大区域",
			reactionMode: "local",
			rect: {x: 0.1, y: 0.2, width: 0.8, height: 0.7}, // area = 0.56
			motion: {mode: "none"},
			expression: {mode: "none"},
		}

		const smallBadge: InteractionRegion = {
			id: "badge",
			name: "胸章小区域",
			reactionMode: "ai",
			rect: {x: 0.4, y: 0.4, width: 0.2, height: 0.2}, // area = 0.04
			motion: {mode: "selected", group: "tap_body", name: "special"},
			expression: {mode: "none"},
		}

		const head: InteractionRegion = {
			id: "head",
			name: "头部",
			reactionMode: "local",
			rect: {x: 0.2, y: 0.05, width: 0.6, height: 0.25}, // area = 0.15
			motion: {mode: "none"},
			expression: {mode: "random"},
		}

		const regions = [largeBody, smallBadge, head]

		it("点击仅大区域的位置返回大区域", () => {
			const hit = findHitRegion(regions, {x: 0.15, y: 0.8})
			expect(hit?.id).toBe("body")
		})

		it("点击重叠区域时优先命中面积最小的区域 (胸章)", () => {
			const hit = findHitRegion(regions, {x: 0.5, y: 0.5})
			expect(hit?.id).toBe("badge")
		})

		it("点击空白未覆盖位置返回 null", () => {
			const hit = findHitRegion(regions, {x: 0.05, y: 0.01})
			expect(hit).toBeNull()
		})
	})

	describe("getRegionLocalPoint (区域局部归一化坐标)", () => {
		const region: InteractionRegion = {
			id: "r1",
			name: "区域",
			reactionMode: "local",
			rect: {x: 0.2, y: 0.4, width: 0.4, height: 0.4},
			motion: {mode: "none"},
			expression: {mode: "none"},
		}

		it("计算正确的局部坐标", () => {
			const localTopLeft = getRegionLocalPoint(region, {x: 0.2, y: 0.4})
			expect(localTopLeft).toEqual({x: 0, y: 0})

			const localCenter = getRegionLocalPoint(region, {x: 0.4, y: 0.6})
			expect(localCenter).toEqual({x: 0.5, y: 0.5})

			const localBottomRight = getRegionLocalPoint(region, {x: 0.6, y: 0.8})
			expect(localBottomRight).toEqual({x: 1, y: 1})
		})
	})

	describe("createDefaultInteractionConfig & createDefaultInteractionRegion", () => {
		it("创建空配置", () => {
			const config = createDefaultInteractionConfig()
			expect(config.version).toBe(1)
			expect(config.regions).toEqual([])
		})

		it("创建默认区域包含合法默认值", () => {
			const region = createDefaultInteractionRegion({name: "测试区域"})
			expect(region.id).toMatch(/^region_\d+_\w+$/)
			expect(region.name).toBe("测试区域")
			expect(region.reactionMode).toBe("local")
			expect(region.motion.mode).toBe("none")
			expect(region.expression.mode).toBe("none")
			expect(region.rect).toEqual({x: 0.25, y: 0.25, width: 0.5, height: 0.5})
		})
	})

	describe("validateRegionBindings", () => {
		const motions = [
			{group: "tap_body", names: ["tap_01", "tap_02"]},
			{group: "idle", names: ["idle_01"]},
		]
		const expressions = ["Happy", "Angry", "Smile"]

		it("none 与 random 模式无需绑定文件名，视为合法", () => {
			const region: InteractionRegion = {
				id: "r1",
				name: "测试",
				reactionMode: "local",
				rect: {x: 0, y: 0, width: 1, height: 1},
				motion: {mode: "random"},
				expression: {mode: "none"},
			}
			const result = validateRegionBindings(region, motions, expressions)
			expect(result.isValid).toBe(true)
		})

		it("selected 模式正确绑定动作与表情", () => {
			const region: InteractionRegion = {
				id: "r1",
				name: "测试",
				reactionMode: "local",
				rect: {x: 0, y: 0, width: 1, height: 1},
				motion: {mode: "selected", group: "tap_body", name: "tap_01"},
				expression: {mode: "selected", name: "Happy"},
			}
			const result = validateRegionBindings(region, motions, expressions)
			expect(result.isValid).toBe(true)
			expect(result.motionGroupValid).toBe(true)
			expect(result.motionNameValid).toBe(true)
			expect(result.expressionValid).toBe(true)
		})

		it("检测不存在的动作组与表情", () => {
			const region: InteractionRegion = {
				id: "r1",
				name: "测试",
				reactionMode: "local",
				rect: {x: 0, y: 0, width: 1, height: 1},
				motion: {mode: "selected", group: "unknown_group", name: "none"},
				expression: {mode: "selected", name: "NonExistent"},
			}
			const result = validateRegionBindings(region, motions, expressions)
			expect(result.isValid).toBe(false)
			expect(result.motionGroupValid).toBe(false)
			expect(result.expressionValid).toBe(false)
		})
	})
})
