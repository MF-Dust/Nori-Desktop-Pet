import {beforeEach, describe, expect, it} from "vitest"
import {expressionStore} from "../../src/services/live2d/stores/expression-store"

describe("expression-store 表情存储", () => {
	beforeEach(() => {
		expressionStore.dispose()
	})

	it("注册表情组与参数条目", () => {
		expressionStore.registerExpressions("arg-nori", [
			{
				name: "Smile",
				parameters: [
					{parameterId: "ParamMouthOpenY", blend: "Overwrite", value: 0.8},
				],
			},
		], [
			{
				name: "ParamMouthOpenY",
				parameterId: "ParamMouthOpenY",
				blend: "Overwrite",
				currentValue: 0,
				defaultValue: 0,
				modelDefault: 0,
				targetValue: 0.8,
			},
		])

		expect(expressionStore.allGroupNames()).toEqual(["Smile"])
		expect(expressionStore.allNames()).toEqual(["ParamMouthOpenY"])
		expect(expressionStore.resolve("Smile")?.kind).toBe("group")
		expect(expressionStore.resolve("ParamMouthOpenY")?.kind).toBe("param")
		expect(expressionStore.resolve("Missing")).toBeNull()
	})

	it("play 激活表情组到 exp3 值", () => {
		expressionStore.registerExpressions("arg-nori", [
			{
				name: "Cry",
				parameters: [
					{parameterId: "ParamTear", blend: "Add", value: 1},
					{parameterId: "ParamEyeWet", blend: "Multiply", value: 0},
				],
			},
		], [
			{
				name: "ParamTear",
				parameterId: "ParamTear",
				blend: "Add",
				currentValue: 0,
				defaultValue: 0,
				modelDefault: 0,
				targetValue: 1,
			},
			{
				name: "ParamEyeWet",
				parameterId: "ParamEyeWet",
				blend: "Multiply",
				currentValue: 1,
				defaultValue: 1,
				modelDefault: 1,
				targetValue: 0,
			},
		])

		expect(expressionStore.play("Cry")).toBe(true)
		expect(expressionStore.expressions.get("ParamTear")?.currentValue).toBe(1)
		expect(expressionStore.expressions.get("ParamEyeWet")?.currentValue).toBe(0)
		expect(expressionStore.play("Missing")).toBe(false)
	})

	it("toggle 在默认值与目标值之间切换", () => {
		expressionStore.registerExpressions("arg-nori", [
			{
				name: "Shy",
				parameters: [
					{parameterId: "ParamCheek", blend: "Overwrite", value: 0.6},
				],
			},
		], [
			{
				name: "ParamCheek",
				parameterId: "ParamCheek",
				blend: "Overwrite",
				currentValue: 0,
				defaultValue: 0,
				modelDefault: 0,
				targetValue: 0.6,
			},
		])

		expressionStore.toggle("Shy")
		expect(expressionStore.expressions.get("ParamCheek")?.currentValue).toBe(0.6)
		expressionStore.toggle("Shy")
		expect(expressionStore.expressions.get("ParamCheek")?.currentValue).toBe(0)
	})

	it("stop/resetAll 重置到模型默认值", () => {
		expressionStore.registerExpressions("arg-nori", [
			{
				name: "Smile",
				parameters: [
					{parameterId: "ParamMouthOpenY", blend: "Overwrite", value: 0.8},
				],
			},
		], [
			{
				name: "ParamMouthOpenY",
				parameterId: "ParamMouthOpenY",
				blend: "Overwrite",
				currentValue: 0,
				defaultValue: 0,
				modelDefault: 0,
				targetValue: 0.8,
			},
		])

		expressionStore.play("Smile")
		expressionStore.stop()
		expect(expressionStore.expressions.get("ParamMouthOpenY")?.currentValue).toBe(0)

		expressionStore.play("Smile")
		expressionStore.resetAll()
		expect(expressionStore.expressions.get("ParamMouthOpenY")?.currentValue).toBe(0)
	})

	it("dispose 清空全部状态", () => {
		expressionStore.registerExpressions("arg-nori", [
			{
				name: "Smile",
				parameters: [
					{parameterId: "ParamMouthOpenY", blend: "Overwrite", value: 0.8},
				],
			},
		], [
			{
				name: "ParamMouthOpenY",
				parameterId: "ParamMouthOpenY",
				blend: "Overwrite",
				currentValue: 0,
				defaultValue: 0,
				modelDefault: 0,
				targetValue: 0.8,
			},
		])

		expressionStore.dispose()
		expect(expressionStore.allGroupNames()).toEqual([])
		expect(expressionStore.allNames()).toEqual([])
		expect(expressionStore.modelId.value).toBe("")
	})
})