/**
 * 资产协议
 */
import {invoke} from "../host/invoke"
import {host} from "../host"

/**
 * 资产基址
 *
 * 宿主把前端与资源挂在同一个回环服务的同一前缀下, 因此这里是同源相对路径:
 * 生产 /<secret>/nori-assets/, 开发 /nori-assets/ (由 vite 代理到宿主)
 */
export const ASSET_BASE = host()?.assetBase ?? "/nori-assets/"

/**
 * 资产 URL
 * @param relativePath
 */
export const assetUrl = (relativePath: string): string => `${ASSET_BASE}${relativePath.replace(/^\/+/, "")}`

/**
 * 默认模型映射
 */
export const defaultModels: Record<string, string> = {
	"arg-nori": "ARGNori",
	"nori": "Nori",
}

/**
 * 解析模型文件基础路径
 */
export const resolveModelFileBase = (directory: string): string => defaultModels[directory] ?? directory

/**
 * 全局 Live2D 行为配置键
 */
export const L2D_BEHAVIOR_KEYS = [
	"l2d_click_interaction",
	"l2d_auto_blink",
	"l2d_eye_tracking",
	"l2d_idle_eye_animation",
	"l2d_idle_animation",
	"l2d_expression_enabled",
	"l2d_lip_sync",
	"l2d_shadow",
	"l2d_render_scale",
	"l2d_max_fps",
	"l2d_beat_sync",
] as const

/**
 * 全局 Live2D 行为配置键类型
 */
export type L2DBehaviorKey = (typeof L2D_BEHAVIOR_KEYS)[number]

/**
 * 全局 Live2D 行为配置默认值
 */
export const L2D_BEHAVIOR_DEFAULTS: Record<L2DBehaviorKey, string | number | boolean> = {
	l2d_click_interaction: true,
	l2d_auto_blink: true,
	l2d_eye_tracking: true,
	l2d_idle_eye_animation: true,
	l2d_idle_animation: true,
	l2d_expression_enabled: true,
	l2d_lip_sync: true,
	l2d_shadow: true,
	l2d_render_scale: 2,
	l2d_max_fps: 0,
	l2d_beat_sync: false,
}

/**
 * 解析布尔配置值
 */
export const parseBoolean = (value: unknown): boolean | null => {
	if (typeof value === "boolean") return value
	if (typeof value === "string") {
		if (value === "true" || value === "1") return true
		if (value === "false" || value === "0") return false
	}
	if (typeof value === "number") return value !== 0
	return null
}

/**
 * 读取全局 Live2D 行为配置
 */
export const readBehaviorConfig = async (key: L2DBehaviorKey): Promise<typeof L2D_BEHAVIOR_DEFAULTS[L2DBehaviorKey]> => {
	const DEFAULT = L2D_BEHAVIOR_DEFAULTS[key]
	try {
		const VALUE = await invoke<unknown>("get_config", {key})
		if (VALUE == null) return DEFAULT
		if (typeof DEFAULT === "boolean") {
			const PARSED = parseBoolean(VALUE)
			if (PARSED != null) return PARSED
		} else if (typeof DEFAULT === "number") {
			const PARSED = parseNumber(VALUE)
			if (PARSED != null) return PARSED
		} else {
			return String(VALUE)
		}
	} catch {
		/* 读取失败使用默认值 */
	}
	return DEFAULT
}

/**
 * 写入全局 Live2D 行为配置
 */
export const writeBehaviorConfig = async (key: L2DBehaviorKey, value: string | number | boolean): Promise<void> => {
	await invoke("set_config", {key, value: String(value)})
}

/**
 * 读取所有全局 Live2D 行为配置
 */
export const readAllBehaviorConfigs = async (): Promise<Record<string, string | number | boolean>> => {
	const result: Record<string, string | number | boolean> = {}
	for (const key of L2D_BEHAVIOR_KEYS) {
		result[key] = await readBehaviorConfig(key)
	}
	return result
}

/**
 * 桌宠显示调整配置键 (按模型存储)
 */
export const L2D_CONFIG_KEYS = ["l2d_scale", "l2d_offset_x", "l2d_offset_y", "l2d_expression"] as const

/**
 * 桌宠显示调整配置键类型
 */
export type L2DConfigKey = (typeof L2D_CONFIG_KEYS)[number]

/**
 * 按模型生成配置键 (无模型后缀时兼容旧版全局键)
 */
export const l2dModelKey = (base: L2DConfigKey, modelId: string): string => `${base}_${modelId}`

/**
 * 解析数字配置值
 */
export const parseNumber = (value: unknown): number | null => {
	if (typeof value === "number") return value
	if (typeof value === "string" && value !== "") {
		const NUM = parseFloat(value)
		return Number.isNaN(NUM) ? null : NUM
	}
	return null
}

/**
 * 解析表情列表配置值 (数组或 JSON 字符串)
 */
export const parseExpressionList = (value: unknown): string[] => {
	if (Array.isArray(value)) return value.filter((item): item is string => typeof item === "string")
	if (typeof value === "string" && value !== "") {
		try {
			const PARSED = JSON.parse(value)
			if (Array.isArray(PARSED)) return PARSED.filter((item): item is string => typeof item === "string")
		} catch {
			/* 非 JSON 字符串 */
		}
	}
	return []
}

/**
 * 读取模型配置: 优先按模型键, 回退旧版全局键
 */
export const readModelConfig = async <T>(
	modelId: string,
	base: L2DConfigKey,
	parse: (value: unknown) => T | null,
	fallback: T
): Promise<T> => {
	for (const KEY of [l2dModelKey(base, modelId), base]) {
		try {
			const VALUE = await invoke<unknown>("get_config", {key: KEY})
			if (VALUE != null) {
				const PARSED = parse(VALUE)
				if (PARSED != null) return PARSED
			}
		} catch {
			/* 读取失败继续尝试 */
		}
	}
	return fallback
}
