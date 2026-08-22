import {invoke} from "../host/invoke"

/**
 * 规范配置键
 */
export const LANGUAGE_CONFIG_KEY = "language"

/**
 * 读取桥接返回的原始配置值
 */
export const readRawConfig = async (key: string): Promise<unknown | null> => invoke<unknown | null>("get_config", {key})

/**
 * 将标量配置安全解析为字符串
 */
export const parseString = (value: unknown): string | null => {
	if (typeof value === "string") return value
	if (typeof value === "number" && Number.isFinite(value)) return String(value)
	if (typeof value === "boolean") return value ? "true" : "false"
	return null
}

/**
 * 将标量配置安全解析为数字
 */
export const parseNumber = (value: unknown): number | null => {
	if (typeof value === "number") return Number.isFinite(value) ? value : null
	if (typeof value !== "string" || value.trim() === "") return null
	const NUMBER = Number(value)
	return Number.isFinite(NUMBER) ? NUMBER : null
}

/**
 * 将桥接配置安全解析为布尔值
 */
export const parseBoolean = (value: unknown): boolean | null => {
	if (typeof value === "boolean") return value
	if (typeof value === "number") return Number.isFinite(value) ? value !== 0 : null
	if (typeof value === "string") {
		switch (value.trim().toLowerCase()) {
			case "true":
			case "1":
				return true
			case "false":
			case "0":
				return false
		}
	}
	return null
}

/**
 * 读取字符串配置, 非法值或桥接失败时返回 fallback
 */
export const readStringConfig = async (key: string, fallback: string): Promise<string> => {
	try {
		return parseString(await readRawConfig(key)) ?? fallback
	} catch {
		return fallback
	}
}

/**
 * 读取数字配置, 非法值或桥接失败时返回 fallback
 */
export const readNumberConfig = async (key: string, fallback: number): Promise<number> => {
	try {
		return parseNumber(await readRawConfig(key)) ?? fallback
	} catch {
		return fallback
	}
}

/**
 * 读取布尔配置, 非法值或桥接失败时返回 fallback
 */
export const readBooleanConfig = async (key: string, fallback: boolean): Promise<boolean> => {
	try {
		return parseBoolean(await readRawConfig(key)) ?? fallback
	} catch {
		return fallback
	}
}

// 保留 PascalCase 导出, 方便新模块遵循前端导出命名约定.
export const ReadRawConfig = readRawConfig
export const ReadStringConfig = readStringConfig
export const ReadNumberConfig = readNumberConfig
export const ReadBooleanConfig = readBooleanConfig
export const ParseString = parseString
export const ParseNumber = parseNumber
export const ParseBoolean = parseBoolean
