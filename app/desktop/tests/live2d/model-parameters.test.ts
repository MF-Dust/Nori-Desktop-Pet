import {describe, expect, it} from "vitest"
import {defaultModelParameters, modelParameters, resetModelParameters} from "../../src/services/live2d/stores/model-parameters"

describe("model-parameters 模型参数", () => {
	it("默认参数包含 22 个 Cubism 常用参数", () => {
		expect(Object.keys(defaultModelParameters)).toHaveLength(22)
		expect(defaultModelParameters.leftEyeOpen).toBe(1)
		expect(defaultModelParameters.rightEyeOpen).toBe(1)
		expect(defaultModelParameters.angleX).toBe(0)
		expect(defaultModelParameters.mouthOpen).toBe(0)
	})

	it("修改后 reset 恢复默认值", () => {
		modelParameters.angleX = 15
		modelParameters.leftEyeOpen = 0.2
		expect(modelParameters.angleX).toBe(15)

		resetModelParameters()
		expect(modelParameters.angleX).toBe(0)
		expect(modelParameters.leftEyeOpen).toBe(1)
	})
})