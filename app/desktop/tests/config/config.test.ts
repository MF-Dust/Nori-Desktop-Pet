import {beforeEach, describe, expect, it, vi} from "vitest"

const MOCK_INVOKE = vi.hoisted(() => vi.fn())
vi.mock("../../src/services/host/invoke", () => ({invoke: MOCK_INVOKE}))

import {
	LANGUAGE_CONFIG_KEY,
	parseBoolean,
	parseNumber,
	parseString,
	readBooleanConfig,
	readNumberConfig,
	readStringConfig,
} from "../../src/services/config"

describe("通用配置读取器", () => {
	beforeEach(() => MOCK_INVOKE.mockReset())

	it("接受桥接返回的原始 Boolean、Number 和 String", async () => {
		MOCK_INVOKE.mockResolvedValueOnce(true).mockResolvedValueOnce(42).mockResolvedValueOnce("en-US")

		expect(await readBooleanConfig("flag", false)).toBe(true)
		expect(await readNumberConfig("count", 0)).toBe(42)
		expect(await readStringConfig("language", "zh-CN")).toBe("en-US")
	})

	it("null、非法值和桥接错误都使用 fallback", async () => {
		MOCK_INVOKE.mockResolvedValueOnce(null).mockResolvedValueOnce("not-a-number").mockRejectedValueOnce(new Error("bridge"))

		expect(await readBooleanConfig("flag", true)).toBe(true)
		expect(await readNumberConfig("count", 7)).toBe(7)
		expect(await readStringConfig("name", "fallback")).toBe("fallback")
	})

	it("解析器不把对象和空文本隐式转换成 truthy 值", () => {
		expect(parseBoolean({})).toBeNull()
		expect(parseBoolean("yes")).toBeNull()
		expect(parseNumber("1.25")).toBe(1.25)
		expect(parseNumber("1.25x")).toBeNull()
		expect(parseString(null)).toBeNull()
		expect(parseString(false)).toBe("false")
		expect(LANGUAGE_CONFIG_KEY).toBe("language")
	})
})
