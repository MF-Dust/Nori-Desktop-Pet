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
}

/**
 * 解析模型文件基础路径
 */
export const resolveModelFileBase = (directory: string): string => defaultModels[directory] ?? directory

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
