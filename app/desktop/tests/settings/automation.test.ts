import {describe, expect, it, beforeEach, vi} from "vitest"
import {createApp, h, nextTick} from "vue"
import {i18n} from "../../src/services/i18n"
import {RUNTIME} from "../../src/services/runtime"
import {feedback} from "../../src/services/feedback"
import AutomationSettings from "../../src/components/settings/AutomationSettings.vue"

describe("AutomationSettings.vue", () => {
	const mountComponent = () => {
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)
		const APP = createApp({
			render: () => h(AutomationSettings),
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
	})

	it("renders fallback state when backend automation capability is missing", async () => {
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
				gptsovitsPromptLang: "zh",
				sttProvider: "whisper",
				sttBaseUrl: "",
				hasSttApiKey: false,
				noticePending: false,
				speaking: false,
			},
			embedding: {configured: false, model: "", baseUrl: "", dimensions: "", hasApiKey: false},
			memory: {
				enabled: false,
				reflectionEnabled: false,
				decayEnabled: false,
				archiveEnabled: false,
				active: 0,
				atoms: 0,
				archived: 0,
				total: 0,
				knowledgePath: "",
				knowledgeChunks: 0,
				indexState: "ready",
				indexProcessed: 0,
				indexTotal: 0,
				ftsAvailable: true,
				reflectionRounds: 8,
				reflectionMinChars: 2500,
				recallTopK: 6,
				keywordTopK: 20,
				vectorTopK: 20,
				rrfK: 60,
				minSimilarity: 0.25,
				sourceRetentionThreshold: 0.75,
				archiveThreshold: 0.15,
				knowledgeEnabled: false,
				knowledgeWatch: false,
				debugRetrieval: false,
			},
			proactive: {idleEnabled: false, idleMinutes: 15, dailyGreeting: false, reminders: []},
			skills: [],
			enabledSkillsCount: 0,
			tools: [],
			emotion: {type: "neutral"},
			// automation is undefined
		} as any

		const MOUNT = mountComponent()
		try {
			await settleView()
			// Switches and buttons should be disabled
			const SWITCHES = MOUNT.container.querySelectorAll("button[role='switch']")
			expect(SWITCHES.length).toBeGreaterThanOrEqual(3)
			for (const SW of Array.from(SWITCHES)) {
				expect(SW.hasAttribute("disabled")).toBe(true)
			}
			const PROBE_BTN = Array.from(MOUNT.container.querySelectorAll("button")).find(b => b.getAttribute("role") !== "switch")
			expect(PROBE_BTN?.hasAttribute("disabled")).toBe(true)
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
		}
	})

	it("renders connected state and dispatches settings update on toggle", async () => {
		const INVOKED: {cmd: string; args: any}[] = []
		;(window as any).__nori.invoke = async (cmd: string, args: any) => {
			INVOKED.push({cmd, args})
			return null
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
				gptsovitsPromptLang: "zh",
				sttProvider: "whisper",
				sttBaseUrl: "",
				hasSttApiKey: false,
				noticePending: false,
				speaking: false,
			},
			embedding: {configured: false, model: "", baseUrl: "", dimensions: "", hasApiKey: false},
			memory: {
				enabled: false,
				reflectionEnabled: false,
				decayEnabled: false,
				archiveEnabled: false,
				active: 0,
				atoms: 0,
				archived: 0,
				total: 0,
				knowledgePath: "",
				knowledgeChunks: 0,
				indexState: "ready",
				indexProcessed: 0,
				indexTotal: 0,
				ftsAvailable: true,
				reflectionRounds: 8,
				reflectionMinChars: 2500,
				recallTopK: 6,
				keywordTopK: 20,
				vectorTopK: 20,
				rrfK: 60,
				minSimilarity: 0.25,
				sourceRetentionThreshold: 0.75,
				archiveThreshold: 0.15,
				knowledgeEnabled: false,
				knowledgeWatch: false,
				debugRetrieval: false,
			},
			proactive: {idleEnabled: false, idleMinutes: 15, dailyGreeting: false, reminders: []},
			skills: [],
			enabledSkillsCount: 0,
			tools: [],
			emotion: {type: "neutral"},
			automation: {
				enabled: true,
				desktopEnabled: true,
				browserEnabled: false,
				visionReady: true,
				capabilities: [
					{id: "desktop", name: "Desktop Automation", available: true},
					{id: "browser", name: "Browser Automation", available: false, unavailableReason: "Driver not found"},
				],
			},
		} as any

		const MOUNT = mountComponent()
		try {
			await settleView()
			const SWITCHES = MOUNT.container.querySelectorAll("button[role='switch']")
			expect(SWITCHES.length).toBeGreaterThanOrEqual(3)

			// Toggle master switch
			const MASTER_SWITCH = SWITCHES[0] as HTMLButtonElement
			expect(MASTER_SWITCH.disabled).toBe(false)
			expect(MASTER_SWITCH.getAttribute("aria-checked")).toBe("true")

			MASTER_SWITCH.click()
			await settleView()

			expect(INVOKED.some(c => c.cmd === "settings_update_automation")).toBe(true)
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
		}
	})

	it("handles vision probe failures via feedback.error", async () => {
		const FEEDBACK_ERROR_SPY = vi.spyOn(feedback, "error").mockImplementation(() => {})
		;(window as any).__nori.invoke = async (cmd: string) => {
			if (cmd === "automation_probe_vision") {
				throw new Error("Vision model timeout")
			}
			return null
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
				gptsovitsPromptLang: "zh",
				sttProvider: "whisper",
				sttBaseUrl: "",
				hasSttApiKey: false,
				noticePending: false,
				speaking: false,
			},
			embedding: {configured: false, model: "", baseUrl: "", dimensions: "", hasApiKey: false},
			memory: {
				enabled: false,
				reflectionEnabled: false,
				decayEnabled: false,
				archiveEnabled: false,
				active: 0,
				atoms: 0,
				archived: 0,
				total: 0,
				knowledgePath: "",
				knowledgeChunks: 0,
				indexState: "ready",
				indexProcessed: 0,
				indexTotal: 0,
				ftsAvailable: true,
				reflectionRounds: 8,
				reflectionMinChars: 2500,
				recallTopK: 6,
				keywordTopK: 20,
				vectorTopK: 20,
				rrfK: 60,
				minSimilarity: 0.25,
				sourceRetentionThreshold: 0.75,
				archiveThreshold: 0.15,
				knowledgeEnabled: false,
				knowledgeWatch: false,
				debugRetrieval: false,
			},
			proactive: {idleEnabled: false, idleMinutes: 15, dailyGreeting: false, reminders: []},
			skills: [],
			enabledSkillsCount: 0,
			tools: [],
			emotion: {type: "neutral"},
			automation: {
				enabled: true,
				desktopEnabled: true,
				browserEnabled: false,
				visionReady: false,
			},
		} as any

		const MOUNT = mountComponent()
		try {
			await settleView()
			const PROBE_BTN = Array.from(MOUNT.container.querySelectorAll("button")).find(b => b.getAttribute("role") !== "switch")
			expect(PROBE_BTN).toBeDefined()
			PROBE_BTN?.click()
			await settleView()

			expect(FEEDBACK_ERROR_SPY).toHaveBeenCalled()
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
		}
	})
})
