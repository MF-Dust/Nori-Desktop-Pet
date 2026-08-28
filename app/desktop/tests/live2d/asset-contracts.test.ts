import {afterEach, describe, expect, it} from "vitest"
import {
	ASSET_BASE,
	assetUrl,
	defaultModels,
	parseBoolean,
	parseExpressionList,
	parseNumber,
	resolveModelFileBase,
} from "../../src/services/live2d/config"
import {MockHost} from "../helpers/mockHost"

describe("Live2D 资产与模型路径契约", () => {
	let mock: MockHost | null = null

	afterEach(() => {
		if (mock) {
			mock.restore()
			mock = null
		}
	})

	it("默认开发环境下 assetUrl 构造同源相对路径", () => {
		expect(ASSET_BASE).toBe("/nori-assets/")
		const URL = assetUrl("live2d/nori/Nori.model3.json")
		expect(URL).toBe("/nori-assets/live2d/nori/Nori.model3.json")
		expect(URL.startsWith("/")).toBe(true)
		expect(URL).not.toContain("//live2d")
		expect(URL).not.toMatch(/^[A-Za-z]:[\\/]/)
		expect(URL).not.toMatch(/^file:\/\//)
	})

	it("剥离相对路径前导斜杠避免双斜杠拼接", () => {
		expect(assetUrl("/live2d/arg-nori/ARGNori.model3.json")).toBe("/nori-assets/live2d/arg-nori/ARGNori.model3.json")
		expect(assetUrl("///live2d/arg-nori/ARGNori.model3.json")).toBe("/nori-assets/live2d/arg-nori/ARGNori.model3.json")
	})

	it("宿主注入随机前缀时正确继承生产同源资产基址", () => {
		mock = new MockHost({})
		mock.host.assetBase = "/e3b0c442/nori-assets/"
		mock.install()

		const URL = assetUrl("live2d/arg-nori/ARGNori.model3.json")
		expect(URL).toBe("/e3b0c442/nori-assets/live2d/arg-nori/ARGNori.model3.json")
		expect(URL).not.toMatch(/^https?:\/\/[^/]+\//)
	})

	it("内置模型与自定义模型的文件基名解析语义保持稳定", () => {
		expect(defaultModels["arg-nori"]).toBe("ARGNori")
		expect(defaultModels.nori).toBe("Nori")

		expect(resolveModelFileBase("arg-nori")).toBe("ARGNori")
		expect(resolveModelFileBase("nori")).toBe("Nori")
		expect(resolveModelFileBase("custom-pet-v2")).toBe("custom-pet-v2")
		expect(resolveModelFileBase("shizuku")).toBe("shizuku")
	})

	it("根据 Live2D 规范组装公开模型入口 URL", () => {
		const SPEC_BUILTIN = {directory: "arg-nori", fileBase: resolveModelFileBase("arg-nori")}
		const BUILTIN_URL = `${assetUrl(`live2d/${SPEC_BUILTIN.directory}`)}/${SPEC_BUILTIN.fileBase}.model3.json`
		expect(BUILTIN_URL).toBe("/nori-assets/live2d/arg-nori/ARGNori.model3.json")

		const SPEC_CUSTOM = {directory: "custom-avatar", fileBase: resolveModelFileBase("custom-avatar")}
		const CUSTOM_URL = `${assetUrl(`live2d/${SPEC_CUSTOM.directory}`)}/${SPEC_CUSTOM.fileBase}.model3.json`
		expect(CUSTOM_URL).toBe("/nori-assets/live2d/custom-avatar/custom-avatar.model3.json")
	})

	it("纯函数解析器正确处理布尔值、数值与表情列表", () => {
		expect(parseBoolean(true)).toBe(true)
		expect(parseBoolean(false)).toBe(false)
		expect(parseBoolean("1")).toBe(true)
		expect(parseBoolean("0")).toBe(false)
		expect(parseBoolean("true")).toBe(true)
		expect(parseBoolean("FALSE")).toBe(false)
		expect(parseBoolean("invalid")).toBeNull()
		expect(parseBoolean(123)).toBeNull()

		expect(parseNumber(1.25)).toBe(1.25)
		expect(parseNumber("1.25")).toBe(1.25)
		expect(parseNumber("-0.5")).toBe(-0.5)
		expect(parseNumber("0")).toBe(0)
		expect(parseNumber("")).toBeNull()
		expect(parseNumber("abc")).toBeNull()
		expect(parseNumber(NaN)).toBeNull()

		expect(parseExpressionList(["exp1", "exp2"])).toEqual(["exp1", "exp2"])
		expect(parseExpressionList('["f01", "f02"]')).toEqual(["f01", "f02"])
		expect(parseExpressionList("")).toEqual([])
		expect(parseExpressionList("invalid-json")).toEqual([])
		expect(parseExpressionList(null)).toEqual([])
	})
})
