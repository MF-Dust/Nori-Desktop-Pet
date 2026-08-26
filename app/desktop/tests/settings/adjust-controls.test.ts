import {describe, expect, it, beforeEach, vi} from "vitest"
import {createApp, h, nextTick, ref} from "vue"
import {i18n} from "../../src/services/i18n"
import {RUNTIME} from "../../src/services/runtime"
import AdjustControls from "../../src/components/settings/AdjustControls.vue"

describe("AdjustControls.vue", () => {
	beforeEach(() => {
		document.body.innerHTML = ""
		vi.restoreAllMocks()
		vi.spyOn(RUNTIME, "init").mockResolvedValue(undefined as any)
		vi.spyOn(RUNTIME, "modelMeta").mockImplementation(async (modelId: string) => {
			if (modelId === "arg-nori") {
				return {
					scale: 1,
					opacity: 1,
					renderScale: 2,
					qualityMode: "adaptive",
					maxFps: 0,
					shadow: true,
					expressions: ["00_Default", "01_KiraKira", "03_Angry"],
					motions: [],
					interactions: {version: 1, regions: []},
				} as any
			}
			return {
				scale: 1,
				opacity: 1,
				renderScale: 2,
				qualityMode: "adaptive",
				maxFps: 0,
				shadow: true,
				expressions: ["01_Smile", "02_Sad"],
				motions: [],
				interactions: {version: 1, regions: []},
			} as any
		})
	})

	const settle = async (): Promise<void> => {
		for (let i = 0; i < 6; i += 1) {
			await nextTick()
			await new Promise<void>(resolve => setTimeout(resolve, 10))
		}
		await nextTick()
	}

	it("挂载时根据 modelId 加载表情列表并能选择与清空", async () => {
		const emittedExpressions: string[][] = []
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)

		const APP = createApp({
			render: () => h(AdjustControls, {
				modelId: "arg-nori",
				modelName: "ARG Nori",
				initialExpressions: [],
				onExpressions: (list: string[]) => emittedExpressions.push(list),
			}),
		})
		APP.use(i18n)
		APP.mount(CONTAINER)

		await settle()

		const BUTTONS = Array.from(CONTAINER.querySelectorAll("button.pill-choice")) as HTMLButtonElement[]
		expect(BUTTONS.length).toBe(4) // "无" + 3个表情

		// 点击第一个表情 "00_Default"
		BUTTONS[1].click()
		await settle()
		expect(emittedExpressions.at(-1)).toEqual(["00_Default"])

		// 再次点击反选
		BUTTONS[1].click()
		await settle()
		expect(emittedExpressions.at(-1)).toEqual([])

		// 点击第二个表情后点 "无" 清空
		BUTTONS[2].click()
		await settle()
		expect(emittedExpressions.at(-1)).toEqual(["01_KiraKira"])

		BUTTONS[0].click()
		await settle()
		expect(emittedExpressions.at(-1)).toEqual([])

		APP.unmount()
	})

	it("响应式: modelId 切换时动态重新拉取对应模型的表情列表", async () => {
		const modelIdRef = ref("arg-nori")
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)

		const APP = createApp({
			render: () => h(AdjustControls, {
				modelId: modelIdRef.value,
				modelName: modelIdRef.value,
			}),
		})
		APP.use(i18n)
		APP.mount(CONTAINER)

		await settle()

		let buttons = Array.from(CONTAINER.querySelectorAll("button.pill-choice")) as HTMLButtonElement[]
		expect(buttons.length).toBe(4) // 无 + 3个

		// 切换模型至 nori
		modelIdRef.value = "nori"
		await settle()

		buttons = Array.from(CONTAINER.querySelectorAll("button.pill-choice")) as HTMLButtonElement[]
		expect(buttons.length).toBe(3) // 无 + 2个

		APP.unmount()
	})

	it("响应式: initialExpressions 改变时同步更新选中状态", async () => {
		const initialListRef = ref<string[]>([])
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)

		const APP = createApp({
			render: () => h(AdjustControls, {
				modelId: "arg-nori",
				initialExpressions: initialListRef.value,
			}),
		})
		APP.use(i18n)
		APP.mount(CONTAINER)

		await settle()

		let buttons = Array.from(CONTAINER.querySelectorAll("button.pill-choice")) as HTMLButtonElement[]
		// 初始 "无" 处于选中态
		expect(buttons[0].getAttribute("aria-pressed")).toBe("true")

		// 外部更新 initialExpressions
		initialListRef.value = ["03_Angry"]
		await settle()

		buttons = Array.from(CONTAINER.querySelectorAll("button.pill-choice")) as HTMLButtonElement[]
		expect(buttons[0].getAttribute("aria-pressed")).toBe("false")
		expect(buttons[3].getAttribute("aria-pressed")).toBe("true")

		APP.unmount()
	})
})
