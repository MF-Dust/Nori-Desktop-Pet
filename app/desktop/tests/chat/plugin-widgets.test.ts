import {afterEach, beforeEach, describe, expect, it, vi} from "vitest"
import {createApp, h, nextTick} from "vue"
import PluginWidgets from "../../src/components/chat/PluginWidgets.vue"

const {invokeMock} = vi.hoisted(() => ({
	invokeMock: vi.fn(),
}))

vi.mock("../../src/services/host/invoke", () => ({
	invoke: invokeMock,
}))

describe("PluginWidgets.vue", () => {
	const mounts: Array<{app: ReturnType<typeof createApp>; container: HTMLDivElement}> = []

	beforeEach(() => {
		vi.useFakeTimers()
		invokeMock.mockReset()
	})

	afterEach(() => {
		for (const mount of mounts) {
			mount.app.unmount()
			mount.container.remove()
		}
		mounts.length = 0
		vi.useRealTimers()
	})

	it("starts with no widgets, polls every 10s, and defaults newly appeared widgets to expanded", async () => {
		// 初始调用: 无部件
		invokeMock.mockResolvedValueOnce({widgets: []})

		const container = document.createElement("div")
		document.body.appendChild(container)
		const app = createApp({
			render: () => h(PluginWidgets),
		})
		app.mount(container)
		mounts.push({app, container})

		// 等待首次 mount 和 refresh 执行
		await nextTick()
		await Promise.resolve()
		await nextTick()

		expect(invokeMock).toHaveBeenCalledWith("plugin_widgets")
		expect(container.querySelector("iframe")).toBeNull()
		expect(container.textContent?.trim()).toBe("")

		// 10秒后轮询返回新插件卡片
		invokeMock.mockResolvedValueOnce({
			widgets: [
				{
					pluginId: "nori.plugin.cloudmusic",
					title: "网易云音乐",
					entry: "http://localhost:5173/card.html",
				},
			],
		})

		await vi.advanceTimersByTimeAsync(10_000)
		await nextTick()

		// 验证卡片已渲染，且默认展开 (iframe 存在并包含正确属性)
		expect(container.textContent).toContain("网易云音乐")
		const iframe = container.querySelector("iframe")
		expect(iframe).not.toBeNull()
		expect(iframe?.getAttribute("src")).toBe("http://localhost:5173/card.html")
		expect(iframe?.style.height).toBe("22rem")

		// 测试点击折叠/展开
		const toggleBtn = container.querySelector("button")
		expect(toggleBtn).not.toBeNull()

		toggleBtn?.click()
		await nextTick()
		expect(container.querySelector("iframe")).toBeNull()

		toggleBtn?.click()
		await nextTick()
		expect(container.querySelector("iframe")).not.toBeNull()
	})
})
