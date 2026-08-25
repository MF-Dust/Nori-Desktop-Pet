import {describe, expect, it, beforeEach, vi} from "vitest"
import {createApp, h, nextTick} from "vue"
import {i18n} from "../../src/services/i18n"
import {RUNTIME} from "../../src/services/runtime"
import {feedback} from "../../src/services/feedback"
import OperationDrawer from "../../src/components/automation/OperationDrawer.vue"
import AutomationTaskCard from "../../src/components/automation/AutomationTaskCard.vue"

describe("OperationDrawer & AutomationTaskCard", () => {
	const mountComponent = (props = {}) => {
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)
		const APP = createApp({
			render: () => h(OperationDrawer, props),
		})
		APP.use(i18n)
		APP.mount(CONTAINER)
		return {app: APP, container: CONTAINER}
	}

	const settleView = async (): Promise<void> => {
		for (let index = 0; index < 4; index += 1) await nextTick()
		await new Promise<void>(resolve => setTimeout(resolve, 0))
		await nextTick()
	}

	beforeEach(() => {
		vi.restoreAllMocks()
		;(window as any).__nori = {
			assetBase: "/nori-assets/",
			label: "main",
			invoke: async (_cmd: string, _args: any) => null,
			emit: () => {},
			listen: () => () => {},
			dispatch: () => {},
		}

		RUNTIME.snapshot.value = {
			version: 1,
			app: {appVersion: "0.1.0", platform: "windows", debugCrashTestsAvailable: false, safeMode: false},
			general: {language: "zh-CN", petAutoSummon: true, sidebarCollapsed: false},
			telemetry: {consent: "granted", enabled: true, available: true},
			secretIssues: [],
			ai: {configured: true, provider: "openai", baseUrl: "", model: "gpt-4o", persona: "nori", hasApiKey: true},
			models: {selected: "arg-nori", items: [], scale: 1.0, expressions: []},
			pet: {visible: true},
			platform: {
				os: "windows",
				sessionType: "windows",
				supportsGlobalCursor: true,
				supportsWindowDrag: true,
				supportsHitThrough: true,
				supportsTopmost: true,
				supportsTray: true,
			},
			behaviors: {
				clickInteraction: true,
				autoBlink: true,
				eyeTracking: true,
				idleEyeAnimation: true,
				idleAnimation: true,
				expressionEnabled: true,
				lipSync: true,
				shadow: true,
				beatSync: false,
				renderScale: 1.0,
				maxFps: 60,
				aiInteraction: false,
			},
			voice: {
				volume: 1.0,
				ttsProvider: "openai",
				ttsBaseUrl: "",
				hasTtsApiKey: false,
				ttsVoice: "nova",
				ttsSpeed: 1.0,
				ttsAutoPlay: true,
				gptsovitsBaseUrl: "",
				gptsovitsRefAudio: "",
				gptsovitsPromptText: "",
				gptsovitsPromptLang: "",
				sttProvider: "",
				sttBaseUrl: "",
				hasSttApiKey: false,
			},
			embedding: {configured: true, baseUrl: "", dimensions: "1536", model: "text-embedding-3-small", hasApiKey: true},
			memory: {enabled: true, shortTermCount: 0, longTermCount: 0, vectorIndexReady: true},
			proactive: {idleEnabled: true, idleMinutes: 10, dailyGreeting: true, lastGreetingDate: null},
			skills: [],
			enabledSkillsCount: 0,
			tools: [],
			emotion: {type: "neutral"},
			automation: {
				enabled: true,
				desktopEnabled: true,
				browserEnabled: true,
				visionReady: true,
				activeTask: null,
				tasks: [],
			},
		}
	})

	it("does not render capsule when there are no active tasks", async () => {
		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		expect(BUTTON).toBeNull()
	})

	it("renders running task capsule and opens drawer", async () => {
		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.activeTask = {
				id: "task-12345678-abcd",
				state: "running",
				currentStep: 2,
				totalSteps: 5,
				progress: 40,
			}
		}

		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		expect(BUTTON).not.toBeNull()
		expect(BUTTON?.textContent).toContain("执行中")

		// Click to open drawer
		BUTTON?.click()
		await settleView()

		const DIALOG = document.body.querySelector("[role='dialog']")
		expect(DIALOG).not.toBeNull()
		expect(DIALOG?.textContent).toContain("行动中心")
		expect(DIALOG?.textContent).toContain("2")
		expect(DIALOG?.textContent).toContain("5")
	})

	it("handles approval flow with approve and reject", async () => {
		let approvalResult: any = null
		;(window as any).__nori.invoke = async (cmd: string, args: any) => {
			if (cmd === "approval_respond") {
				approvalResult = args
				return true
			}
			return null
		}

		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.activeTask = {
				id: "task-approval-1",
				approvalRequestId: "req-1",
				state: "awaiting_approval",
				actionKinds: ["pointer", "keyboard"],
			}
		}

		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		expect(BUTTON?.textContent).toContain("等待安全审批")

		BUTTON?.click()
		await settleView()

		const APPROVE_BTN = Array.from(document.body.querySelectorAll("button")).find(b => b.textContent?.includes("同意"))
		expect(APPROVE_BTN).toBeDefined()
		APPROVE_BTN?.click()
		await settleView()

		expect(approvalResult).toEqual({requestId: "req-1", approved: true})
	})

	it("handles task cancellation and stop all", async () => {
		let stoppedTaskId: string | null = null
		let stoppedAll = false
		;(window as any).__nori.invoke = async (cmd: string, args: any) => {
			if (cmd === "automation_stop_task") {
				stoppedTaskId = args.taskId
				return
			}
			if (cmd === "automation_stop_all") {
				stoppedAll = true
				return
			}
		}

		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.tasks = [
				{id: "task-to-cancel", state: "running"},
			]
		}

		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		BUTTON?.click()
		await settleView()

		const CANCEL_BTN = Array.from(document.body.querySelectorAll("button")).find(b => b.textContent?.includes("取消任务"))
		expect(CANCEL_BTN).toBeDefined()
		CANCEL_BTN?.click()
		await settleView()
		expect(stoppedTaskId).toBe("task-to-cancel")

		const STOP_ALL_BTN = Array.from(document.body.querySelectorAll("button")).find(b => b.textContent?.includes("全部停止"))
		expect(STOP_ALL_BTN).toBeDefined()
		STOP_ALL_BTN?.click()
		await settleView()
		expect(stoppedAll).toBe(true)
	})

	it("displays failure reason for failed tasks", async () => {
		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.tasks = [
				{id: "task-failed", state: "failed", failureCode: "timeout"},
			]
		}

		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		expect(BUTTON?.textContent).toContain("失败")

		BUTTON?.click()
		await settleView()

		const DIALOG = document.body.querySelector("[role='dialog']")
		expect(DIALOG?.textContent).toContain("超时")
	})

	it("closes drawer when Escape key is pressed", async () => {
		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.activeTask = {
				id: "task-escape",
				state: "running",
			}
		}

		const {container} = mountComponent()
		await settleView()

		const BUTTON = container.querySelector("button")
		BUTTON?.click()
		await settleView()

		expect(document.body.querySelector("[role='dialog']")).not.toBeNull()

		// Trigger Escape
		window.dispatchEvent(new KeyboardEvent("keydown", {key: "Escape"}))
		await settleView()

		expect(document.body.querySelector("[role='dialog']")).toBeNull()
	})

	it("invokes feedback.error when host command fails", async () => {
		const ERROR_SPY = vi.spyOn(feedback, "error")
		;(window as any).__nori.invoke = async () => {
			throw new Error("Command failed in host")
		}

		if (RUNTIME.snapshot.value?.automation) {
			RUNTIME.snapshot.value.automation.activeTask = {
				id: "task-fail-host",
				state: "running",
			}
		}

		const {container} = mountComponent()
		await settleView()

		container.querySelector("button")?.click()
		await settleView()

		const CANCEL_BTN = Array.from(document.body.querySelectorAll("button")).find(b => b.textContent?.includes("取消任务"))
		CANCEL_BTN?.click()
		await settleView()

		expect(ERROR_SPY).toHaveBeenCalled()
	})
})
