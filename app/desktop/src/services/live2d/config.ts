import {host} from "../host"

/**
 * Live2D 预览资产适配器。
 * 模型配置、行为状态和持久化全部由 C# runtime 提供；这里仅保留同源资产 URL 与纯渲染解析函数。
 */
export const ASSET_BASE = host()?.assetBase ?? "/nori-assets/"

export const assetUrl = (relativePath: string): string => {
	const BASE = host()?.assetBase ?? ASSET_BASE
	return `${BASE}${relativePath.replace(/^\/+/, "")}`
}

export const defaultModels: Record<string, string> = {
	"arg-nori": "ARGNori",
	nori: "Nori",
}

export const resolveModelFileBase = (directory: string): string => defaultModels[directory] ?? directory

export const parseBoolean = (value: unknown): boolean | null => {
	if (typeof value === "boolean") return value
	if (typeof value !== "string") return null
	if (value === "1" || value.toLowerCase() === "true") return true
	if (value === "0" || value.toLowerCase() === "false") return false
	return null
}

export const parseNumber = (value: unknown): number | null => {
	if (typeof value === "number" && Number.isFinite(value)) return value
	if (typeof value !== "string" || value.trim() === "") return null
	const RESULT = Number(value)
	return Number.isFinite(RESULT) ? RESULT : null
}

export const parseExpressionList = (value: unknown): string[] => {
	if (Array.isArray(value)) return value.filter((item): item is string => typeof item === "string")
	if (typeof value !== "string" || value === "") return []
	try {
		const PARSED: unknown = JSON.parse(value)
		return Array.isArray(PARSED) ? PARSED.filter((item): item is string => typeof item === "string") : []
	} catch {
		return []
	}
}
