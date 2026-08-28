import {afterEach, describe, expect, it} from "vitest"
import {MockHost} from "../helpers/mockHost"
import {
	getCurrentWindowLabel,
	getCurrentWindowRoute,
	navigateToOwnWindow,
	WINDOW_ROUTES,
	type WindowLabel,
} from "../../src/services/window"
import {
	disablePlugin,
	enablePlugin,
	installLocalPlugin,
	listPlugins,
	uninstallPlugin,
} from "../../src/services/plugins"
import {RUNTIME} from "../../src/services/runtime"

/** 模拟 index.html 中的基址计算逻辑，确保生产与开发环境同源相对路径稳定 */
const computeBootstrapAssetBase = (pathname: string): string => {
	const INDEX = pathname.indexOf("/app/")
	return INDEX >= 0 ? `${pathname.slice(0, INDEX)}/nori-assets/` : "/nori-assets/"
}

describe("AppLauncher 与数据目录解耦契约", () => {
	let mock: MockHost | null = null

	afterEach(() => {
		if (mock) {
			mock.restore()
			mock = null
		}
	})

	describe("前端引导基址解析", () => {
		it("在开发环境下解析为标准 /nori-assets/ 相对基址", () => {
			expect(computeBootstrapAssetBase("/")).toBe("/nori-assets/")
			expect(computeBootstrapAssetBase("/index.html")).toBe("/nori-assets/")
		})

		it("在生产随机前缀下正确截取前缀并保留同源 nori-assets", () => {
			expect(computeBootstrapAssetBase("/a1b2c3d4/app/index.html")).toBe("/a1b2c3d4/nori-assets/")
			expect(computeBootstrapAssetBase("/8f14e45fceea/app/")).toBe("/8f14e45fceea/nori-assets/")
			expect(computeBootstrapAssetBase("/v2-slot-abc/app/sub/index.html")).toBe("/v2-slot-abc/nori-assets/")
		})
	})

	describe("窗口路由与 Label 映射", () => {
		it("窗口路由映射表仅依赖逻辑 Label，不依赖二进制或启动器路径", () => {
			expect(WINDOW_ROUTES["first-run"]).toBe("/first-run")
			expect(WINDOW_ROUTES.init).toBe("/init")
			expect(WINDOW_ROUTES.main).toBe("/main")
			expect(WINDOW_ROUTES.pet).toBeUndefined()
		})

		it("按宿主注入的 label 获取对应路由", async () => {
			mock = new MockHost({})
			mock.host.label = "main"
			mock.install()

			expect(await getCurrentWindowLabel()).toBe("main")
			expect(await getCurrentWindowRoute()).toBe("/main")
		})

		it("未知窗口 label 返回 null 且不触发异常", async () => {
			mock = new MockHost({})
			mock.host.label = "unknown-launcher-test"
			mock.install()

			expect(await getCurrentWindowLabel()).toBeNull()
			expect(await getCurrentWindowRoute()).toBeNull()
		})

		it("navigateToOwnWindow 完成单页内路由替换", async () => {
			mock = new MockHost({})
			mock.host.label = "first-run"
			mock.install()

			const REPLACED: string[] = []
			const ROUTER = {
				replace: async (path: string) => {
					REPLACED.push(path)
				},
				currentRoute: {value: {path: "/"}},
			}

			const TARGET = await navigateToOwnWindow(ROUTER)
			expect(TARGET).toBe("/first-run")
			expect(REPLACED).toEqual(["/first-run"])
		})
	})

	describe("插件管理服务行为", () => {
		it("插件列表与启停卸载命令不传递物理目录参数", async () => {
			mock = new MockHost({
				plugin_list: () => ({
					plugins: [
						{
							id: "test-plugin",
							name: "Test Plugin",
							description: "Desc",
							version: "1.0.0",
							author: "Author",
							homepage: null,
							repository: null,
							license: "MIT",
							state: "active",
							enabled: true,
							capabilities: [],
							optionalCapabilities: [],
							capabilityStatuses: [],
							errorCode: null,
							errorMessage: null,
							requiresRestart: false,
							iconUrl: null,
						},
					],
				}),
				plugin_install_local: () => ({cancelled: false, plugin: null}),
				plugin_enable: args => ({
					id: (args as {id: string}).id,
					name: "Test Plugin",
					description: "Desc",
					version: "1.0.0",
					author: "Author",
					homepage: null,
					repository: null,
					license: "MIT",
					state: "active",
					enabled: true,
					capabilities: [],
					optionalCapabilities: [],
					capabilityStatuses: [],
					errorCode: null,
					errorMessage: null,
					requiresRestart: false,
					iconUrl: null,
				}),
				plugin_disable: args => ({
					id: (args as {id: string}).id,
					name: "Test Plugin",
					description: "Desc",
					version: "1.0.0",
					author: "Author",
					homepage: null,
					repository: null,
					license: "MIT",
					state: "disabled",
					enabled: false,
					capabilities: [],
					optionalCapabilities: [],
					capabilityStatuses: [],
					errorCode: null,
					errorMessage: null,
					requiresRestart: false,
					iconUrl: null,
				}),
				plugin_uninstall: () => ({success: true, requiresRestart: false, plugin: null}),
			})
			mock.install()

			const PLUGINS = await listPlugins()
			expect(PLUGINS.length).toBe(1)
			expect(PLUGINS[0].id).toBe("test-plugin")

			await installLocalPlugin()
			await enablePlugin("test-plugin")
			await disablePlugin("test-plugin")
			await uninstallPlugin("test-plugin", true)

			expect(mock.calls).toEqual([
				{command: "plugin_list", args: undefined},
				{command: "plugin_install_local", args: undefined},
				{command: "plugin_enable", args: {id: "test-plugin"}},
				{command: "plugin_disable", args: {id: "test-plugin"}},
				{command: "plugin_uninstall", args: {id: "test-plugin", deleteData: true}},
			])
		})
	})

	describe("模型导入与元数据查询", () => {
		it("本地模型导入由宿主对话框与物理目录接管，前端仅传递来源类型", async () => {
			mock = new MockHost({
				model_import_local: () => ["imported-model-a"],
				model_get_meta: () => ({scale: 1.0}),
			})
			mock.install()

			const IMPORTED = await RUNTIME.importLocalModel("zip")
			expect(IMPORTED).toEqual(["imported-model-a"])

			const FOLDER_IMPORTED = await RUNTIME.importLocalModel("folder")
			expect(FOLDER_IMPORTED).toEqual(["imported-model-a"])

			const META = await RUNTIME.modelMeta("imported-model-a")
			expect(META.scale).toBe(1.0)

			expect(mock.calls).toEqual([
				{command: "model_import_local", args: {resourceType: "live2d", sourceKind: "zip"}},
				{command: "model_import_local", args: {resourceType: "live2d", sourceKind: "folder"}},
				{command: "model_get_meta", args: {modelId: "imported-model-a"}},
			])
		})
	})

	describe("日志与知识库文件夹打开命令", () => {
		it("打开日志与知识库目录不携带前端计算的物理路径", async () => {
			mock = new MockHost({
				open_log_folder: () => undefined,
				memory_knowledge_open: () => undefined,
				export_diagnostics: () => ({fileName: "nori-diag-2025.zip", bytes: 1024, skipped: []}),
			})
			mock.install()

			await RUNTIME.openLogFolder()
			await RUNTIME.memoryKnowledgeOpen()
			const EXPORT_RESULT = await RUNTIME.exportDiagnostics()

			expect(EXPORT_RESULT?.fileName).toBe("nori-diag-2025.zip")
			expect(mock.calls).toEqual([
				{command: "open_log_folder", args: undefined},
				{command: "memory_knowledge_open", args: undefined},
				{command: "export_diagnostics", args: undefined},
			])
		})
	})
})
