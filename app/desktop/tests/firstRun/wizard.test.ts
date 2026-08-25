import {describe, expect, it} from "vitest"
import {createWizard, WIZARD_STEPS} from "../../src/services/firstRun/wizard"

/** 一路推到末步 (步骤数会随向导增减, 测试不写死次数) */
const toLastStep = (wizard: ReturnType<typeof createWizard>): void => {
	while (wizard.next()) { /* 直到末步 */ }
}

describe("首次运行向导状态机", () => {
	it("AI 配置步位于形象与就绪之间, 且不阻断前进", () => {
		expect(WIZARD_STEPS).toEqual(["welcome", "language", "model", "ai", "ready"])

		const wizard = createWizard(async () => {})
		wizard.next()
		wizard.next()
		wizard.next()
		expect(wizard.snapshot().step).toBe("ai")
		// 可跳过: 什么都不填也能继续
		expect(wizard.snapshot().canNext).toBe(true)
		expect(wizard.next()).toBe(true)
		expect(wizard.snapshot().step).toBe("ready")
	})

	it("按步骤顺序前进与后退, 边界不越界", () => {
		const wizard = createWizard(async () => {})

		expect(wizard.snapshot().step).toBe("welcome")
		expect(wizard.prev()).toBe(false)

		expect(wizard.next()).toBe(true)
		expect(wizard.next()).toBe(true)
		expect(wizard.snapshot().step).toBe("model")

		toLastStep(wizard)
		expect(wizard.snapshot().isLast).toBe(true)
		// 末步不再有下一步 (原实现在这里静默 no-op, 观感像卡死)
		expect(wizard.next()).toBe(false)
		expect(wizard.snapshot().index).toBe(WIZARD_STEPS.length - 1)

		expect(wizard.prev()).toBe(true)
		expect(wizard.snapshot().direction).toBe(-1)
	})

	it("步骤被阻断时禁止前进, 清除后恢复", () => {
		const wizard = createWizard(async () => {})
		wizard.next()
		wizard.next()

		wizard.blockStep("保存模型选择失败")
		expect(wizard.snapshot().canNext).toBe(false)
		expect(wizard.snapshot().stepError).toBe("保存模型选择失败")
		expect(wizard.next()).toBe(false)

		wizard.clearStep()
		expect(wizard.snapshot().canNext).toBe(true)
		expect(wizard.next()).toBe(true)
	})

	it("提交失败停在末步并暴露错误, 可重试成功", async () => {
		let attempt = 0
		const wizard = createWizard(async () => {
			attempt += 1
			if (attempt === 1) throw new Error("宿主不可用")
		})
		toLastStep(wizard)

		expect(await wizard.finish()).toBe(false)
		expect(wizard.snapshot().finishState).toBe("failed")
		expect(wizard.snapshot().finishError).toBe("宿主不可用")
		expect(wizard.snapshot().isLast).toBe(true)

		expect(await wizard.finish()).toBe(true)
		expect(wizard.snapshot().finishState).toBe("idle")
		expect(wizard.snapshot().finishError).toBe("")
	})

	it("提交进行中禁止重复提交与后退", async () => {
		let release: (() => void) | null = null
		const wizard = createWizard(() => new Promise<void>(resolve => {
			release = resolve
		}))
		toLastStep(wizard)

		const pending = wizard.finish()
		expect(wizard.snapshot().finishState).toBe("submitting")
		expect(wizard.snapshot().canPrev).toBe(false)
		expect(await wizard.finish()).toBe(false)

		release?.()
		expect(await pending).toBe(true)
	})
})
