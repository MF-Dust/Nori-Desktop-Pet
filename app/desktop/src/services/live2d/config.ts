/**
 * 资产协议
 */
export const ASSET_ORIGIN = /Windows|Win/i.test(navigator.userAgent) ? "http://nori-asset.localhost" : "nori-asset://localhost"

/**
 * 资产 URL
 * @param relativePath
 */
export const assetUrl = (relativePath: string): string => `${ASSET_ORIGIN}/${relativePath.replace(/^\/+/, "")}`

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
