import {beforeEach, describe, expect, it, vi} from "vitest"
import {
	L2D_BEHAVIOR_DEFAULTS,
	L2D_BEHAVIOR_KEYS,
	l2dModelKey,
	parseBoolean,
	parseExpressionList,
	parseNumber,
	readBehaviorConfig,
} from "../../src/services/live2d/config"

// 模拟宿主 invoke: 返回一个内存配置表
const configMap = new Map<string, string>()

vi.mock("../../src/services/host/invoke", () => ({
	invoke: vi.fn(async (cmd: string, args?: Record<string, unknown>) => {
		if (cmd === "get_config") {
			const VALUE = configMap.get(String(args?.key))
			return VALUE === undefined ? null : VALUE
		}
		if (cmd === "set_config") {
			configMap.set(String(args?.key), String(args?.value))
			return null
		}
		throw new Error(`未知命令: ${cmd}`)
	}),
}))

describe("live2d config 解析函数", () => {
	it("parseBoolean 支持布尔/数字字符串", () => {
		expect(parseBoolean(true)).toBe(true)
		expect(parseBoolean("true")).toBe(true)
		expect(parseBoolean("1")).toBe(true)
		expect(parseBoolean(false)).toBe(false)
		expect(parseBoolean("false")).toBe(false)
		expect(parseBoolean("0")).toBe(false)
		expect(parseBoolean("yes")).toBeNull()
	})

	it("parseNumber 支持数字与数字字符串", () => {
		expect(parseNumber(42)).toBe(42)
		expect(parseNumber("1.25")).toBe(1.25)
		expect(parseNumber("-3")).toBe(-3)
		expect(parseNumber("abc")).toBeNull()
		expect(parseNumber("")).toBeNull()
	})

	it("parseExpressionList 支持数组与 JSON 字符串", () => {
		expect(parseExpressionList(["Smile", "Shy"])).toEqual(["Smile", "Shy"])
		expect(parseExpressionList("[\"Smile\",\"Shy\"]")).toEqual(["Smile", "Shy"])
		expect(parseExpressionList("not-json")).toEqual([])
		expect(parseExpressionList(123)).toEqual([])
	})

	it("l2dModelKey 生成按模型配置键", () => {
		expect(l2dModelKey("l2d_scale", "arg-nori")).toBe("l2d_scale_arg-nori")
		expect(l2dModelKey("l2d_expression", "nori")).toBe("l2d_expression_nori")
	})

	it("行为配置键与默认值完整", () => {
		expect(L2D_BEHAVIOR_KEYS).toContain("l2d_click_interaction")
		expect(L2D_BEHAVIOR_KEYS).toContain("l2d_render_scale")
		expect(L2D_BEHAVIOR_DEFAULTS.l2d_auto_blink).toBe(true)
		expect(L2D_BEHAVIOR_DEFAULTS.l2d_render_scale).toBe(2)
		expect(L2D_BEHAVIOR_DEFAULTS.l2d_max_fps).toBe(0)
		expect(L2D_BEHAVIOR_DEFAULTS.l2d_beat_sync).toBe(false)
	})
})

describe("readBehaviorConfig", () => {
	beforeEach(() => {
		configMap.clear()
	})

	it("读取布尔配置并解析", async () => {
		configMap.set("l2d_auto_blink", "false")
		expect(await readBehaviorConfig("l2d_auto_blink")).toBe(false)
	})

	it("读取数字配置并解析", async () => {
		configMap.set("l2d_render_scale", "1.5")
		expect(await readBehaviorConfig("l2d_render_scale")).toBe(1.5)
	})

	it("缺少配置时返回默认值", async () => {
		expect(await readBehaviorConfig("l2d_click_interaction")).toBe(true)
		expect(await readBehaviorConfig("l2d_render_scale")).toBe(2)
	})

	it("非法布尔值回退默认", async () => {
		configMap.set("l2d_shadow", "maybe")
		expect(await readBehaviorConfig("l2d_shadow")).toBe(true)
	})
})