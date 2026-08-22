import {describe, expect, it} from "vitest"
import {ShouldAutoSummonPet} from "../../src/services/runtime/launch"

describe("初始化自动唤出桌宠", () => {
	it("缺省配置沿用自动唤出", () => {
		expect(ShouldAutoSummonPet(undefined)).toBe(true)
		expect(ShouldAutoSummonPet(null)).toBe(true)
	})

	it("显式关闭时保持桌宠隐藏", () => {
		expect(ShouldAutoSummonPet(false)).toBe(false)
		expect(ShouldAutoSummonPet(true)).toBe(true)
	})
})
