/**
 * Live2D 自定义矩形交互区域纯函数与坐标变换
 *
 * 坐标语义: 模型画布归一化坐标，左上角 (0, 0)、右下角 (1, 1)，y 轴向下。
 * 预览与视口变换复用 calcFitModel 计算模型真实的可视边界，不将整个 CSS 容器误当模型画布。
 */
import {calcFitModel, type CanvasDim, type ModelDim} from "./composables/fit-model"
import type {
	InteractionAction,
	InteractionConfig,
	InteractionRect,
	InteractionRegion,
} from "../runtime/types"

/** 视口像素矩形 */
export interface ViewportPixelRect {
	left: number
	top: number
	width: number
	height: number
}

/** 点坐标 */
export interface Point2D {
	x: number
	y: number
}

/**
 * 约束归一化数值在 [min, max] 区间内
 */
const clampNum = (val: number, min = 0, max = 1): number => {
	if (!Number.isFinite(val)) return min
	return Math.max(min, Math.min(max, val))
}

/**
 * 归一化矩形裁剪与合法化
 *
 * 保证 x, y, width, height 均在 [0, 1] 之间，且 x + width <= 1, y + height <= 1。
 * 宽度或高度为负时纠正为 0。
 */
export const clampNormalizedRect = (rect: InteractionRect): InteractionRect => {
	const x = clampNum(rect.x, 0, 1)
	const y = clampNum(rect.y, 0, 1)
	const maxWidth = Math.max(0, 1 - x)
	const maxHeight = Math.max(0, 1 - y)
	const rawWidth = Math.max(0, Number.isFinite(rect.width) ? rect.width : 0)
	const rawHeight = Math.max(0, Number.isFinite(rect.height) ? rect.height : 0)
	const width = Math.min(maxWidth, rawWidth)
	const height = Math.min(maxHeight, rawHeight)

	return {
		x: Number(x.toFixed(6)),
		y: Number(y.toFixed(6)),
		width: Number(width.toFixed(6)),
		height: Number(height.toFixed(6)),
	}
}

/**
 * 从两点（如拖拽起始点与当前点）计算合法的归一化矩形
 */
export const normalizeRectFromPoints = (p1: Point2D, p2: Point2D): InteractionRect => {
	const x1 = clampNum(p1.x, 0, 1)
	const y1 = clampNum(p1.y, 0, 1)
	const x2 = clampNum(p2.x, 0, 1)
	const y2 = clampNum(p2.y, 0, 1)

	const x = Math.min(x1, x2)
	const y = Math.min(y1, y2)
	const width = Math.abs(x2 - x1)
	const height = Math.abs(y2 - y1)

	return clampNormalizedRect({x, y, width, height})
}

/**
 * 计算模型在画布/容器中的可视像素视口 (Viewport)
 *
 * 与 Live2D 控制器的 applyLayout 逻辑保持 1:1 一致：
 * 采用 calcFitModel 居中适配后乘以 userScale。
 */
export const calculateModelViewportRect = (
	container: CanvasDim,
	model: ModelDim,
	userScale = 1,
): ViewportPixelRect => {
	const safeContainer = {
		width: Math.max(1, container.width),
		height: Math.max(1, container.height),
	}
	const safeModel = {
		width: Math.max(1, model.width),
		height: Math.max(1, model.height),
	}
	const fit = calcFitModel(safeContainer, safeModel)
	const effectiveScale = fit.scale * Math.max(0.01, userScale)
	const width = safeModel.width * effectiveScale
	const height = safeModel.height * effectiveScale
	const left = fit.x - width / 2
	const top = fit.y - height / 2

	return {
		left: Number(left.toFixed(2)),
		top: Number(top.toFixed(2)),
		width: Number(width.toFixed(2)),
		height: Number(height.toFixed(2)),
	}
}

/**
 * 客户端像素坐标 (如 clientX/Y) 转换为模型画布归一化坐标 (0~1)
 *
 * @param clientX 鼠标/指针 clientX
 * @param clientY 鼠标/指针 clientY
 * @param containerBounds 容器元素的 getBoundingClientRect()
 * @param modelViewport 模型在容器内的像素视口
 * @returns 归一化坐标点；若模型视口不可用则返回 null
 */
export const clientToModelNormalizedPoint = (
	clientX: number,
	clientY: number,
	containerBounds: {left: number; top: number},
	modelViewport: ViewportPixelRect,
): Point2D | null => {
	if (modelViewport.width <= 0 || modelViewport.height <= 0) return null
	const relX = clientX - containerBounds.left
	const relY = clientY - containerBounds.top
	return containerToModelNormalizedPoint(relX, relY, modelViewport)
}

/**
 * 容器相对像素坐标 (0~containerWidth/Height) 转换为模型画布归一化坐标 (0~1)
 */
export const containerToModelNormalizedPoint = (
	containerX: number,
	containerY: number,
	modelViewport: ViewportPixelRect,
): Point2D | null => {
	if (modelViewport.width <= 0 || modelViewport.height <= 0) return null
	const nx = (containerX - modelViewport.left) / modelViewport.width
	const ny = (containerY - modelViewport.top) / modelViewport.height

	return {
		x: Number(nx.toFixed(6)),
		y: Number(ny.toFixed(6)),
	}
}

/**
 * 模型归一化矩形转换为容器内像素矩形
 */
export const modelNormalizedToContainerRect = (
	rect: InteractionRect,
	modelViewport: ViewportPixelRect,
): ViewportPixelRect => {
	const clamped = clampNormalizedRect(rect)
	const left = modelViewport.left + clamped.x * modelViewport.width
	const top = modelViewport.top + clamped.y * modelViewport.height
	const width = clamped.width * modelViewport.width
	const height = clamped.height * modelViewport.height

	return {
		left: Number(left.toFixed(2)),
		top: Number(top.toFixed(2)),
		width: Number(width.toFixed(2)),
		height: Number(height.toFixed(2)),
	}
}

/**
 * 检查点是否在归一化边界 [0, 1] 内部
 */
export const isPointInNormalizedBounds = (point: Point2D): boolean => {
	return point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1
}

/**
 * 检查点是否命中特定交互区域
 */
export const isPointInRegion = (region: InteractionRegion, point: Point2D): boolean => {
	const rect = region.rect
	return (
		point.x >= rect.x &&
		point.x <= rect.x + rect.width &&
		point.y >= rect.y &&
		point.y <= rect.y + rect.height
	)
}

/**
 * 区域命中判定（重叠时面积最小优先）
 *
 * 遍历所有区域，找到所有包含该归一化点的区域；
 * 按照区域面积 (width * height) 升序排列，返回面积最小的最精细区域。
 */
export const findHitRegion = (
	regions: InteractionRegion[],
	point: Point2D,
): InteractionRegion | null => {
	if (!regions || regions.length === 0) return null

	const hits: {region: InteractionRegion; area: number}[] = []
	for (const region of regions) {
		if (isPointInRegion(region, point)) {
			const area = region.rect.width * region.rect.height
			hits.push({region, area})
		}
	}

	if (hits.length === 0) return null

	// 面积最小优先；若面积相同则后声明的优先（上层覆盖）
	hits.sort((a, b) => a.area - b.area)
	return hits[0].region
}

/**
 * 计算点在命中区域内部的归一化局部坐标 (0~1)
 *
 * 区域左上角为 (0,0)，右下角为 (1,1)
 */
export const getRegionLocalPoint = (
	region: InteractionRegion,
	point: Point2D,
): Point2D | null => {
	const {width, height, x, y} = region.rect
	if (width <= 0 || height <= 0) return null
	const lx = (point.x - x) / width
	const ly = (point.y - y) / height

	return {
		x: Number(clampNum(lx, 0, 1).toFixed(6)),
		y: Number(clampNum(ly, 0, 1).toFixed(6)),
	}
}

/**
 * 生成唯一区域 ID
 */
export const generateRegionId = (): string => {
	const RANDOM_SUFFIX = Math.random().toString(36).slice(2, 8)
	return `region_${Date.now()}_${RANDOM_SUFFIX}`
}

/**
 * 创建默认的空交互配置
 */
export const createDefaultInteractionConfig = (): InteractionConfig => {
	return {
		version: 1,
		regions: [],
	}
}

/**
 * 创建新的互动区域对象
 */
export const createDefaultInteractionRegion = (
	partial: Partial<InteractionRegion> = {},
): InteractionRegion => {
	const defaultMotion: InteractionAction = {mode: "none"}
	const defaultExpression: InteractionAction = {mode: "none"}
	const defaultRect: InteractionRect = {x: 0.25, y: 0.25, width: 0.5, height: 0.5}

	return {
		id: partial.id ?? generateRegionId(),
		name: partial.name ?? "",
		reactionMode: partial.reactionMode ?? "local",
		rect: partial.rect ? clampNormalizedRect(partial.rect) : defaultRect,
		motion: partial.motion ? {...partial.motion} : defaultMotion,
		expression: partial.expression ? {...partial.expression} : defaultExpression,
	}
}

/**
 * 校验区域的动作与表情绑定是否在模型资源中真实存在
 */
export const validateRegionBindings = (
	region: InteractionRegion,
	availableMotions: {group: string; names: string[]}[],
	availableExpressions: string[],
): {
	motionGroupValid: boolean
	motionNameValid: boolean
	expressionValid: boolean
	isValid: boolean
} => {
	let motionGroupValid = true
	let motionNameValid = true
	let expressionValid = true

	if (region.motion.mode === "selected") {
		if (region.motion.group) {
			const groupMatch = availableMotions.find(g => g.group === region.motion.group)
			if (!groupMatch) {
				motionGroupValid = false
				motionNameValid = false
			} else if (region.motion.name) {
				motionNameValid = groupMatch.names.includes(region.motion.name)
			}
		} else {
			motionGroupValid = false
		}
	}

	if (region.expression.mode === "selected") {
		if (region.expression.name) {
			expressionValid = availableExpressions.includes(region.expression.name)
		} else {
			expressionValid = false
		}
	}

	const isValid = motionGroupValid && motionNameValid && expressionValid
	return {motionGroupValid, motionNameValid, expressionValid, isValid}
}
