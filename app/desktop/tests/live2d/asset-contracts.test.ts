import {afterEach, describe, expect, it, vi} from "vitest"
import {MockHost} from "../helpers/mockHost"

describe("Live2D 资产与配置解析契约", () => {
	let mock: MockHost | null = null

	afterEach(() => {
		if (mock) {
			mock.restore()
			mock = null
		}
		vi.resetModules()
	})

	it("无宿主环境下使用默认同源相对基址并安全剥离前导斜杠", async () => {
		vi.resetModules()
		const {ASSET_BASE, assetUrl} = await import("../../src/services/live2d/config")

		expect(ASSET_BASE).toBe("/nori-assets/")
		expect(assetUrl("live2d/nori/Nori.model3.json")).toBe("/nori-assets/live2d/nori/Nori.model3.json")
		expect(assetUrl("/live2d/nori/Nori.model3.json")).toBe("/nori-assets/live2d/nori/Nori.model3.json")
		expect(assetUrl("///live2d/nori/Nori.model3.json")).toBe("/nori-assets/live2d/nori/Nori.model3.json")
	})

	it("宿主就绪时模块初始化捕获生产随机资产前缀", async () => {
		mock = new MockHost({})
		mock.host.assetBase = "/7a8b9c0d/nori-assets/"
		mock.install()

		vi.resetModules()
		const {ASSET_BASE, assetUrl} = await import("../../src/services/live2d/config")

		expect(ASSET_BASE).toBe("/7a8b9c0d/nori-assets/")
		expect(assetUrl("live2d/arg-nori/ARGNori.model3.json")).toBe(
			"/7a8b9c0d/nori-assets/live2d/arg-nori/ARGNori.model3.json",
		)
	})

	it("内置模型映射到对应 PascalCase 基名，自定义模型回退自身目录名", async () => {
		const {resolveModelFileBase} = await import("../../src/services/live2d/config")

		expect(resolveModelFileBase("arg-nori")).toBe("ARGNori")
		expect(resolveModelFileBase("nori")).toBe("Nori")
		expect(resolveModelFileBase("custom-pet")).toBe("custom-pet")
	})

	it("纯函数解析器正确处理布尔值、数值与表情列表边界", async () => {
		const {parseBoolean, parseExpressionList, parseNumber} = await import(
			"../../src/services/live2d/config"
		)

		expect(parseBoolean(true)).toBe(true)
		expect(parseBoolean(false)).toBe(false)
		expect(parseBoolean("1")).toBe(true)
		expect(parseBoolean("0")).toBe(false)
		expect(parseBoolean("true")).toBe(true)
		expect(parseBoolean("false")).toBe(false)
		expect(parseBoolean("invalid")).toBeNull()
		expect(parseBoolean(null)).toBeNull()

		expect(parseNumber(1.25)).toBe(1.25)
		expect(parseNumber("1.25")).toBe(1.25)
		expect(parseNumber("")).toBeNull()
		expect(parseNumber("abc")).toBeNull()
		expect(parseNumber(NaN)).toBeNull()

		expect(parseExpressionList(["f01", "f02"])).toEqual(["f01", "f02"])
		expect(parseExpressionList('["f01", "f02"]')).toEqual(["f01", "f02"])
		expect(parseExpressionList("")).toEqual([])
		expect(parseExpressionList("invalid-json")).toEqual([])
		expect(parseExpressionList(null)).toEqual([])
	})
})
