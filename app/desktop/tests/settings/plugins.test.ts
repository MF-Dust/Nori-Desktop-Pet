import {beforeEach, describe, expect, it, vi} from "vitest"
import {createApp, h, nextTick} from "vue"
import ZH from "../../src/services/i18n/locales/zh-CN"
import {i18n} from "../../src/services/i18n"
import {mergePluginMessages} from "../../src/services/i18n/pluginMessages"
import {RUNTIME} from "../../src/services/runtime"
import PluginsSettings from "../../src/components/settings/PluginsSettings.vue"
import type {PluginInfo} from "../../src/services/plugins"

const plugin = (state: PluginInfo["state"], overrides: Partial<PluginInfo> = {}): PluginInfo => ({
	id: "io.nori.test",
	name: "Test Plugin",
	description: "A test plugin extension",
	version: "1.2.3",
	author: "Nori",
	homepage: null,
	repository: null,
	license: "MIT",
	state,
	enabled: state === "active",
	capabilities: ["ui.webview"],
	optionalCapabilities: [],
	capabilityStatuses: [{id: "ui.webview", declared: true, granted: true, available: state === "active"}],
	errorCode: state === "failed" ? "plugin.activation_failed" : null,
	errorMessage: state === "failed" ? "Activation failed" : null,
	requiresRestart: state === "pending_restart",
	iconUrl: null,
	...overrides,
})

const settle = async (): Promise<void> => {
	for (let index = 0; index < 4; index += 1) await nextTick()
	await new Promise<void>(resolve => setTimeout(resolve, 0))
	await nextTick()
}

const mount = () => {
	const container = document.createElement("div")
	document.body.appendChild(container)
	const app = createApp({render: () => h(PluginsSettings)})
	app.use(i18n)
	app.mount(container)
	return {app, container}
}

const button = (root: ParentNode, text: string): HTMLButtonElement => {
	const found = Array.from(root.querySelectorAll("button")).find(item => item.textContent?.includes(text))
	if (!(found instanceof HTMLButtonElement)) throw new Error(`button not found: ${text}`)
	return found
}

describe("PluginsSettings.vue", () => {
	beforeEach(() => {
		vi.restoreAllMocks()
		localStorage.clear()
		document.body.innerHTML = ""
		i18n.global.setLocaleMessage("zh-CN", mergePluginMessages("zh-CN", ZH))
		i18n.global.locale.value = "zh-CN"
		RUNTIME.snapshot.value = {app: {safeMode: false}} as any
		vi.spyOn(RUNTIME, "init").mockResolvedValue()
	})

	it("renders empty state and opens picker only after first trust confirmation", async () => {
		const invoked: string[] = []
		;(window as any).__nori = {
			assetBase: "/nori-assets/",
			label: "main",
			invoke: async (cmd: string) => {
				invoked.push(cmd)
				if (cmd === "plugin_list") return {plugins: []}
				if (cmd === "plugin_install_local") return {cancelled: true, plugin: null}
				return null
			},
			emit: () => {}, listen: () => () => {}, dispatch: () => {},
		}

		const view = mount()
		try {
			await settle()
			expect(view.container.textContent).toContain("还没有安装插件")
			button(view.container, "安装本地插件").click()
			await settle()
			expect(invoked.filter(cmd => cmd === "plugin_install_local")).toHaveLength(0)
			expect(document.body.textContent).toContain("信任本地插件")
			button(document.body, "我了解风险").click()
			await settle()
			expect(invoked.filter(cmd => cmd === "plugin_install_local")).toHaveLength(1)
		} finally {
			view.app.unmount()
			view.container.remove()
		}
	})

	it("renders lifecycle states and uses returned DTO for enable and disable", async () => {
		let items = [
			plugin("active"),
			plugin("disabled", {id: "io.nori.disabled", name: "Disabled Plugin", enabled: false}),
			plugin("failed", {id: "io.nori.failed", name: "Failed Plugin", enabled: true}),
			plugin("pending_restart", {id: "io.nori.restart", name: "Restart Plugin", enabled: false}),
		]
		const calls: {cmd: string; args: any}[] = []
		;(window as any).__nori = {
			assetBase: "/nori-assets/", label: "main",
			invoke: async (cmd: string, args: any) => {
				calls.push({cmd, args})
				if (cmd === "plugin_list") return {plugins: items}
				if (cmd === "plugin_disable") return plugin("disabled", {enabled: false})
				if (cmd === "plugin_enable") return plugin("active", {id: args.id, enabled: true})
				return null
			},
			emit: () => {}, listen: () => () => {}, dispatch: () => {},
		}

		const view = mount()
		try {
			await settle()
			expect(view.container.textContent).toContain("已启用")
			expect(view.container.textContent).toContain("已禁用")
			expect(view.container.textContent).toContain("启动失败")
			expect(view.container.textContent).toContain("等待重启")
			expect(view.container.textContent).toContain("需要重启 Nori")
			button(view.container, "禁用").click()
			await settle()
			expect(calls.some(call => call.cmd === "plugin_disable" && call.args.id === "io.nori.test")).toBe(true)
		} finally {
			view.app.unmount()
			view.container.remove()
		}
	})

	it("passes deleteData only after uninstall confirmation and hides install in Safe Mode", async () => {
		RUNTIME.snapshot.value = {app: {safeMode: true}} as any
		const target = plugin("disabled", {enabled: false})
		const calls: {cmd: string; args: any}[] = []
		;(window as any).__nori = {
			assetBase: "/nori-assets/", label: "main",
			invoke: async (cmd: string, args: any) => {
				calls.push({cmd, args})
				if (cmd === "plugin_list") return {plugins: [target]}
				if (cmd === "plugin_uninstall") return {success: true, requiresRestart: false, plugin: null}
				return null
			},
			emit: () => {}, listen: () => () => {}, dispatch: () => {},
		}

		const view = mount()
		try {
			await settle()
			expect(view.container.textContent).toContain("安全模式")
			expect(Array.from(view.container.querySelectorAll("button")).some(item => item.textContent?.includes("安装本地插件"))).toBe(false)
			button(view.container, "卸载").click()
			await settle()
			const checkbox = document.body.querySelector("input[type='checkbox']") as HTMLInputElement
			expect(checkbox).toBeTruthy()
			checkbox.click()
			await nextTick()
			button(document.body, "卸载").click()
			await settle()
			expect(calls.some(call => call.cmd === "plugin_uninstall" && call.args.id === target.id && call.args.deleteData === true)).toBe(true)
		} finally {
			view.app.unmount()
			view.container.remove()
		}
	})
})
