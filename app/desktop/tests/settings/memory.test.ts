import {describe, expect, it, beforeEach, vi} from "vitest"
import {createApp, h, nextTick} from "vue"
import useLanguage, {i18n} from "../../src/services/i18n"
import {RUNTIME, type MemoryAtom, type MemoryItem, type MemorySource} from "../../src/services/runtime"
import {feedback} from "../../src/services/feedback"
import MemorySettings from "../../src/components/settings/MemorySettings.vue"
import {MockHost} from "../helpers/mockHost"

describe("MemorySettings.vue", () => {
	const mountComponent = () => {
		const CONTAINER = document.createElement("div")
		document.body.appendChild(CONTAINER)
		const APP = createApp({
			render: () => h(MemorySettings),
		})
		APP.use(i18n)
		APP.mount(CONTAINER)
		return {app: APP, container: CONTAINER}
	}

	const settleView = async (): Promise<void> => {
		for (let index = 0; index < 6; index += 1) {
			await nextTick()
			await new Promise<void>(resolve => setTimeout(resolve, 10))
		}
		await nextTick()
	}

	const createMockSnapshot = (overrides?: any) => ({
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
			enabled: true,
			reflectionEnabled: true,
			decayEnabled: true,
			archiveEnabled: true,
			active: 12,
			atoms: 34,
			archived: 5,
			total: 17,
			knowledgePath: "/data/resources/knowledge",
			knowledgeChunks: 8,
			indexState: "ready",
			indexProcessed: 17,
			indexTotal: 17,
			ftsAvailable: true,
			reflectionRounds: 8,
			reflectionMinChars: 2500,
			recallTopK: 6,
			keywordTopK: 20,
			vectorTopK: 20,
			rrfK: 60,
			minSimilarity: 0.25,
			sourceRetentionThreshold: 0.8,
			archiveThreshold: 0.15,
			knowledgeEnabled: true,
			knowledgeWatch: true,
			debugRetrieval: false,
		},
		...overrides,
	})

	const mockMemoryItem: MemoryItem = {
		id: 101,
		type: "fact",
		content: "主人最喜欢的饮料是冰美式",
		canonicalSummary: "用户喜好冰美式咖啡",
		personaSummary: "主人爱喝冰美式",
		importance: 0.9,
		confidence: 0.85,
		source: "agent",
		kind: "preference",
		status: "active",
		tags: "饮食, 偏好",
		createdAt: "2025-01-10T10:00:00Z",
		updatedAt: "2025-01-11T12:00:00Z",
		lastAccessedAt: "2025-01-12T08:30:00Z",
		lastReinforcedAt: "2025-01-12T08:30:00Z",
		accessCount: 4,
		reinforcementCount: 2,
		ttlDays: 30,
		expiresAt: "2025-02-10T10:00:00Z",
	}

	const mockAtoms: MemoryAtom[] = [
		{
			id: 201,
			parentMemoryId: 101,
			atomType: "preference",
			content: "喜好：冰美式咖啡",
			importance: 0.9,
			confidence: 0.85,
			status: "active",
			createdAt: "2025-01-10T10:00:00Z",
			decayType: "exponential",
			reinforcementCount: 2,
		},
	]

	const mockSources: MemorySource[] = [
		{
			id: 301,
			memoryId: 101,
			role: "user",
			content: "今天好热啊，给我来杯冰美式",
			sequence: 1,
			messageTime: "2025-01-10T09:59:00Z",
		},
		{
			id: 302,
			memoryId: 101,
			role: "assistant",
			content: "好的主人，已经为您记下最爱冰美式啦！",
			sequence: 2,
			messageTime: "2025-01-10T10:00:00Z",
		},
	]

	beforeEach(() => {
		vi.restoreAllMocks()
		useLanguage.setLanguage("zh-CN")
		RUNTIME.snapshot.value = createMockSnapshot()
	})

	it("renders overview stat tiles and switches tabs", async () => {
		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [mockMemoryItem], total: 1}),
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()
			expect(MOUNT.container.textContent).toContain("记忆资产")
			expect(MOUNT.container.textContent).toContain("12")
			expect(MOUNT.container.textContent).toContain("34")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("renders detailed memory view with content, kind, source messages, timestamps, and expiration", async () => {
		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [mockMemoryItem], total: 1}),
			memory_get: () => ({item: mockMemoryItem, atoms: mockAtoms, sources: mockSources}),
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			// 切换到长期记忆 tab
			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const MEMORIES_TAB = Array.from(TABS).find(b => b.textContent?.includes("长期记忆"))
			expect(MEMORIES_TAB).toBeTruthy()
			MEMORIES_TAB?.click()
			await settleView()

			// 点击打开记忆详情
			const ITEM_ROW = MOUNT.container.querySelector<HTMLElement>(".cursor-pointer.bg-overlay-4")
			expect(ITEM_ROW).toBeTruthy()
			ITEM_ROW?.click()
			await settleView()

			// 验证详情展示
			expect(document.body.textContent).toContain("#101")
			const DIALOG_TEXTAREAS = Array.from(document.body.querySelectorAll<HTMLTextAreaElement>("[role='dialog'] textarea"))
			expect(DIALOG_TEXTAREAS.some(t => t.value === "主人最喜欢的饮料是冰美式")).toBe(true)
			expect(DIALOG_TEXTAREAS.some(t => t.value === "用户喜好冰美式咖啡")).toBe(true)
			expect(DIALOG_TEXTAREAS.some(t => t.value === "主人爱喝冰美式")).toBe(true)
			expect(document.body.textContent).toContain("来源对话上下文")
			expect(document.body.textContent).toContain("今天好热啊，给我来杯冰美式")
			expect(document.body.textContent).toContain("好的主人，已经为您记下最爱冰美式啦！")
			expect(document.body.textContent).toContain("生命周期与时间")
			expect(document.body.textContent).toContain("4 / 2")

			// 展开高级解释 / 事实原子区域
			const ADVANCED_TOGGLE = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("事实原子与底层溯源")
			)
			expect(ADVANCED_TOGGLE).toBeTruthy()
			ADVANCED_TOGGLE?.click()
			await settleView()

			expect(document.body.textContent).toContain("喜好：冰美式咖啡")
			expect(document.body.textContent).toContain("exponential")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("displays fallback text when source messages or atoms are empty", async () => {
		const EMPTY_DETAIL_ITEM: MemoryItem = {
			...mockMemoryItem,
			id: 102,
			content: "没有来源对话的记忆",
			lastAccessedAt: undefined,
			lastReinforcedAt: undefined,
			expiresAt: undefined,
		}

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [EMPTY_DETAIL_ITEM], total: 1}),
			memory_get: () => ({item: EMPTY_DETAIL_ITEM, atoms: [], sources: []}),
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const MEMORIES_TAB = Array.from(TABS).find(b => b.textContent?.includes("长期记忆"))
			MEMORIES_TAB?.click()
			await settleView()

			const ITEM_ROW = MOUNT.container.querySelector<HTMLElement>(".cursor-pointer.bg-overlay-4")
			ITEM_ROW?.click()
			await settleView()

			expect(document.body.textContent).toContain("暂无关联来源对话")
			expect(document.body.textContent).toContain("暂无访问记录")
			expect(document.body.textContent).toContain("永久有效")

			// 展开高级溯源区，验证无原子提示
			const ADVANCED_TOGGLE = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("事实原子与底层溯源")
			)
			ADVANCED_TOGGLE?.click()
			await settleView()

			expect(document.body.textContent).toContain("暂无事实原子")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("guards against unsaved edits with confirmation dialog", async () => {
		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [mockMemoryItem], total: 1}),
			memory_get: () => ({item: mockMemoryItem, atoms: mockAtoms, sources: mockSources}),
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const MEMORIES_TAB = Array.from(TABS).find(b => b.textContent?.includes("长期记忆"))
			MEMORIES_TAB?.click()
			await settleView()

			const ITEM_ROW = MOUNT.container.querySelector<HTMLElement>(".cursor-pointer.bg-overlay-4")
			ITEM_ROW?.click()
			await settleView()

			// 修改弹窗内的正文 textarea
			const DIALOG_TEXTAREA = document.body.querySelector<HTMLTextAreaElement>("[role='dialog'] textarea")
			expect(DIALOG_TEXTAREA).toBeTruthy()
			if (DIALOG_TEXTAREA) {
				DIALOG_TEXTAREA.value = "修改后的新内容"
				DIALOG_TEXTAREA.dispatchEvent(new Event("input"))
			}
			await settleView()

			// 尝试点击取消关闭
			const CANCEL_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("[role='dialog'] button")).find(b =>
				b.textContent?.trim() === "取消"
			)
			expect(CANCEL_BTN).toBeTruthy()
			CANCEL_BTN?.click()
			await settleView()

			// 应触发未保存确认弹窗
			expect(document.body.textContent).toContain("未保存的修改")
			expect(document.body.textContent).toContain("您有尚未保存的修改")

			// 点击“继续编辑”，弹窗保持
			const KEEP_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("继续编辑")
			)
			expect(KEEP_BTN).toBeTruthy()
			KEEP_BTN?.click()
			await settleView()

			const DIALOG_TEXTAREA_AFTER = document.body.querySelector<HTMLTextAreaElement>("[role='dialog'] textarea")
			expect(DIALOG_TEXTAREA_AFTER?.value).toBe("修改后的新内容")

			// 再次取消并选择“放弃修改”
			CANCEL_BTN?.click()
			await settleView()

			const DISCARD_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("放弃修改")
			)
			expect(DISCARD_BTN).toBeTruthy()
			DISCARD_BTN?.click()
			await settleView()

			// 详情模态框应已关闭
			expect(document.body.textContent).not.toContain("生命周期与时间")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("handles archive, restore, and delete operations with confirmation and error feedback", async () => {
		const FEEDBACK_SPY = vi.spyOn(feedback, "error").mockImplementation(() => {})
		let archiveCalls = 0
		let deleteCalls = 0

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [mockMemoryItem], total: 1}),
			memory_archive: () => {
				archiveCalls += 1
				throw new Error("归档失败测试")
			},
			memory_delete: () => {
				deleteCalls += 1
				throw new Error("删除失败测试")
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const MEMORIES_TAB = Array.from(TABS).find(b => b.textContent?.includes("长期记忆"))
			MEMORIES_TAB?.click()
			await settleView()

			// 点击归档按钮 (带 package 图标的操作按钮)
			const ARCHIVE_BTN = MOUNT.container.querySelector<HTMLButtonElement>("button[aria-label='归档此记忆']")
			expect(ARCHIVE_BTN).toBeTruthy()
			ARCHIVE_BTN?.click()
			await settleView()

			// 确认归档弹窗
			expect(document.body.textContent).toContain("归档记忆")
			const CONFIRM_ARCHIVE_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("归档此记忆") && b.classList.contains("btn-primary")
			)
			expect(CONFIRM_ARCHIVE_BTN).toBeTruthy()
			CONFIRM_ARCHIVE_BTN?.click()
			await settleView()

			expect(archiveCalls).toBe(1)
			expect(FEEDBACK_SPY).toHaveBeenCalledWith("归档记忆失败", expect.any(Error))

			// 点击删除按钮
			const DELETE_BTN = MOUNT.container.querySelector<HTMLButtonElement>("button[aria-label='删除此记忆']")
			expect(DELETE_BTN).toBeTruthy()
			DELETE_BTN?.click()
			await settleView()

			// 确认删除弹窗
			expect(document.body.textContent).toContain("确定删除这条记忆吗？")
			const CONFIRM_DELETE_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.trim() === "删除" && b.classList.contains("btn-danger")
			)
			expect(CONFIRM_DELETE_BTN).toBeTruthy()
			CONFIRM_DELETE_BTN?.click()
			await settleView()

			expect(deleteCalls).toBe(1)
			expect(FEEDBACK_SPY).toHaveBeenCalledWith("删除记忆失败", expect.any(Error))
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("renders empty state and error state with retry button", async () => {
		let shouldFail = true
		let loadCalls = 0

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => {
				loadCalls += 1
				if (shouldFail) {
					throw new Error("网络错误")
				}
				return {items: [], total: 0}
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const MEMORIES_TAB = Array.from(TABS).find(b => b.textContent?.includes("长期记忆"))
			MEMORIES_TAB?.click()
			await settleView()

			// 首次加载失败，显示错误重试条
			expect(MOUNT.container.textContent).toContain("记忆列表加载失败")
			const RETRY_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("重新加载")
			)
			expect(RETRY_BTN).toBeTruthy()

			// 修复并重试
			shouldFail = false
			RETRY_BTN?.click()
			await settleView()

			expect(loadCalls).toBeGreaterThanOrEqual(2)
			expect(MOUNT.container.textContent).toContain("暂无已保存的长期记忆")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("supports restore operation in archive tab", async () => {
		const ARCHIVED_ITEM: MemoryItem = {
			...mockMemoryItem,
			id: 103,
			status: "archived",
			content: "已归档的旧偏好",
		}
		let restoreCalls = 0

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [ARCHIVED_ITEM], total: 1}),
			memory_restore: () => {
				restoreCalls += 1
				return true
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const ARCHIVE_TAB = Array.from(TABS).find(b => b.textContent?.includes("归档"))
			ARCHIVE_TAB?.click()
			await settleView()

			expect(MOUNT.container.textContent).toContain("已归档的旧偏好")
			const RESTORE_BTN = MOUNT.container.querySelector<HTMLButtonElement>("button[aria-label='恢复']")
			expect(RESTORE_BTN).toBeTruthy()
			RESTORE_BTN?.click()
			await settleView()

			// 确认恢复弹窗
			expect(document.body.textContent).toContain("恢复记忆")
			const CONFIRM_RESTORE_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.trim() === "恢复" && b.classList.contains("btn-primary")
			)
			expect(CONFIRM_RESTORE_BTN).toBeTruthy()
			CONFIRM_RESTORE_BTN?.click()
			await settleView()

			expect(restoreCalls).toBe(1)
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("runs recall debugger and displays structured diagnostic hits", async () => {
		let queryParam = ""
		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [], total: 0}),
			memory_recall_debug: (args: any) => {
				queryParam = args.query
				return {
					trace: {
						query: args.query,
						expandedQuery: `${args.query} (expanded)`,
						keywordHits: [{memoryId: 101, score: 0.88, rank: 1}],
						vectorHits: [{memoryId: 101, score: 0.92, rank: 1}],
						atomHits: [{memoryId: 101, score: 0.85, rank: 1}],
						rrfHits: [{memoryId: 101, score: 0.95, rank: 1}],
						filteredIds: [],
						injectedIds: [101],
					},
					personal: [mockMemoryItem],
					atoms: mockAtoms,
					knowledge: [{id: 1, heading: "Nori 饮食设定", content: "设定内容", awareness: "ambient", score: 0.9}],
					echoes: [{content: "冰美式残响", score: 0.7}],
				}
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const DEBUGGER_TAB = Array.from(TABS).find(b => b.textContent?.includes("检索调试"))
			DEBUGGER_TAB?.click()
			await settleView()

			const INPUT = MOUNT.container.querySelector<HTMLInputElement>("input")
			expect(INPUT).toBeTruthy()
			if (INPUT) {
				INPUT.value = "主人喜欢什么饮料"
				INPUT.dispatchEvent(new Event("input"))
			}
			await settleView()

			const RUN_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("开始检索")
			)
			expect(RUN_BTN).toBeTruthy()
			RUN_BTN?.click()
			await settleView()

			expect(queryParam).toBe("主人喜欢什么饮料")
			expect(MOUNT.container.textContent).toContain("主人喜欢什么饮料 (expanded)")
			expect(MOUNT.container.textContent).toContain("0.8800")
			expect(MOUNT.container.textContent).toContain("0.9200")
			expect(MOUNT.container.textContent).toContain("0.9500")
			expect(MOUNT.container.textContent).toContain("Nori 饮食设定")
			expect(MOUNT.container.textContent).toContain("冰美式残响")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("exports memory data with sanitized stats and handles download/copy actions", async () => {
		let exportCalled = false
		window.URL.createObjectURL = vi.fn(() => "blob:mock-url")
		window.URL.revokeObjectURL = vi.fn()
		Object.assign(navigator, {
			clipboard: {
				writeText: vi.fn().mockResolvedValue(undefined),
			},
		})

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [mockMemoryItem], total: 1}),
			memory_export: () => {
				exportCalled = true
				return {
					fileName: "nori-memory-export-test.json",
					totalCount: 15,
					activeCount: 12,
					archivedCount: 3,
					atomCount: 20,
					sanitizedFields: ["content", "canonicalSummary", "personaSummary", "kind", "importance", "confidence", "tags"],
					exportedAt: "2025-01-15T12:00:00Z",
					content: JSON.stringify({totalCount: 15, items: []}),
				}
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			// 切换到记忆迁移 tab
			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const TRANSFER_TAB = Array.from(TABS).find(b => b.textContent?.includes("记忆迁移"))
			expect(TRANSFER_TAB).toBeTruthy()
			TRANSFER_TAB?.click()
			await settleView()

			expect(MOUNT.container.textContent).toContain("导出记忆数据")
			expect(MOUNT.container.textContent).toContain("导入记忆数据")

			// 点击导出按钮
			const EXPORT_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("导出脱敏记忆")
			)
			expect(EXPORT_BTN).toBeTruthy()
			EXPORT_BTN?.click()
			await settleView()

			expect(exportCalled).toBe(true)
			expect(MOUNT.container.textContent).toContain("导出数据概览")
			expect(MOUNT.container.textContent).toContain("15")
			expect(MOUNT.container.textContent).toContain("12")
			expect(MOUNT.container.textContent).toContain("3")
			expect(MOUNT.container.textContent).toContain("canonicalSummary")
			expect(MOUNT.container.textContent).toContain("personaSummary")
			expect(MOUNT.container.textContent).toContain("安全说明：原始向量、未脱敏聊天原文及工具执行参数已自动剔除")

			// 点击下载脱敏文件
			const DOWNLOAD_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("下载脱敏文件")
			)
			expect(DOWNLOAD_BTN).toBeTruthy()
			DOWNLOAD_BTN?.click()
			expect(window.URL.createObjectURL).toHaveBeenCalled()

			// 点击复制导出内容
			const COPY_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("复制导出内容")
			)
			expect(COPY_BTN).toBeTruthy()
			COPY_BTN?.click()
			expect(navigator.clipboard.writeText).toHaveBeenCalled()
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("handles export failure with feedback.error and error retry banner without faking success", async () => {
		const FEEDBACK_SPY = vi.spyOn(feedback, "error").mockImplementation(() => {})
		let exportAttempts = 0

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [], total: 0}),
			memory_export: () => {
				exportAttempts += 1
				throw new Error("未知的命令: memory_export")
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const TRANSFER_TAB = Array.from(TABS).find(b => b.textContent?.includes("记忆迁移"))
			TRANSFER_TAB?.click()
			await settleView()

			const EXPORT_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("导出脱敏记忆")
			)
			EXPORT_BTN?.click()
			await settleView()

			expect(exportAttempts).toBe(1)
			expect(FEEDBACK_SPY).toHaveBeenCalledWith("导出记忆失败", expect.any(Error))
			expect(MOUNT.container.textContent).toContain("未知的命令: memory_export")
			expect(MOUNT.container.textContent).not.toContain("导出数据概览")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("screens import file format, size limit, and invalid JSON", async () => {
		const FEEDBACK_SPY = vi.spyOn(feedback, "error").mockImplementation(() => {})

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [], total: 0}),
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const TRANSFER_TAB = Array.from(TABS).find(b => b.textContent?.includes("记忆迁移"))
			TRANSFER_TAB?.click()
			await settleView()

			const FILE_INPUT = MOUNT.container.querySelector<HTMLInputElement>("input[type='file']")
			expect(FILE_INPUT).toBeTruthy()

			// 1. 非 .json 文件测试
			const TXT_FILE = new File(["hello world"], "notes.txt", {type: "text/plain"})
			Object.defineProperty(FILE_INPUT, "files", {
				value: [TXT_FILE],
				configurable: true,
			})
			FILE_INPUT?.dispatchEvent(new Event("change"))
			await settleView()

			expect(FEEDBACK_SPY).toHaveBeenCalledWith("文件格式无效，仅支持 .json 文件")
			expect(MOUNT.container.textContent).toContain("文件格式无效，仅支持 .json 文件")

			// 2. 超大文件测试 (> 5MB)
			const OVERSIZED_FILE = new File(["a"], "large.json", {type: "application/json"})
			Object.defineProperty(OVERSIZED_FILE, "size", {value: 6 * 1024 * 1024, configurable: true})
			Object.defineProperty(FILE_INPUT, "files", {
				value: [OVERSIZED_FILE],
				configurable: true,
			})
			FILE_INPUT?.dispatchEvent(new Event("change"))
			await settleView()

			expect(FEEDBACK_SPY).toHaveBeenCalledWith("文件过大，单文件不得超过 5MB")
			expect(MOUNT.container.textContent).toContain("文件过大，单文件不得超过 5MB")

			// 3. 非法 JSON 内容测试
			const INVALID_JSON_FILE = new File(["{ invalid json"], "bad.json", {type: "application/json"})
			INVALID_JSON_FILE.text = vi.fn().mockResolvedValue("{ invalid json")
			Object.defineProperty(FILE_INPUT, "files", {
				value: [INVALID_JSON_FILE],
				configurable: true,
			})
			FILE_INPUT?.dispatchEvent(new Event("change"))
			await settleView()

			expect(FEEDBACK_SPY).toHaveBeenCalledWith("JSON 解析失败，请检查文件内容是否为合法 JSON")
			expect(MOUNT.container.textContent).toContain("JSON 解析失败，请检查文件内容是否为合法 JSON")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("previews memory import, shows conflict analysis, and commits import to refresh list", async () => {
		let previewCalled = false
		let commitCalled = false
		let listReloads = 0

		const VALID_BACKUP_JSON = JSON.stringify({
			version: 1,
			memories: [
				{content: "主人爱喝拿铁", kind: "preference", importance: 0.9},
				{content: "主人最喜欢的饮料是冰美式", kind: "preference", importance: 0.9},
				{content: "主人工作地点在北京", kind: "factual", importance: 0.8},
			],
		})

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => {
				listReloads += 1
				return {items: [mockMemoryItem], total: 1}
			},
			memory_import_preview: (args: any) => {
				previewCalled = true
				expect(args.fileName).toBe("backup.json")
				return {
					valid: true,
					totalCount: 3,
					newCount: 1,
					duplicateCount: 1,
					conflictCount: 1,
					errorCount: 0,
					previewToken: "tok-preview-999",
					items: [
						{id: 1, contentSummary: "主人爱喝拿铁", kind: "preference", importance: 0.9, conflictType: "none"},
						{id: 2, contentSummary: "主人最喜欢的饮料是冰美式", kind: "preference", importance: 0.9, conflictType: "duplicate"},
						{id: 3, contentSummary: "主人工作地点在北京", kind: "factual", importance: 0.8, conflictType: "conflict", conflictReason: "本地已有不同设定"},
					],
				}
			},
			memory_import_commit: (args: any) => {
				commitCalled = true
				expect(args.previewToken).toBe("tok-preview-999")
				expect(args.conflictStrategy).toBe("skip")
				return {
					success: true,
					importedCount: 2,
					updatedCount: 0,
					skippedCount: 1,
				}
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const TRANSFER_TAB = Array.from(TABS).find(b => b.textContent?.includes("记忆迁移"))
			TRANSFER_TAB?.click()
			await settleView()

			const FILE_INPUT = MOUNT.container.querySelector<HTMLInputElement>("input[type='file']")
			const VALID_FILE = new File([VALID_BACKUP_JSON], "backup.json", {type: "application/json"})
			VALID_FILE.text = vi.fn().mockResolvedValue(VALID_BACKUP_JSON)
			Object.defineProperty(FILE_INPUT, "files", {
				value: [VALID_FILE],
				configurable: true,
			})
			FILE_INPUT?.dispatchEvent(new Event("change"))
			await settleView()

			expect(MOUNT.container.textContent).toContain("已选择文件: backup.json")

			// 点击解析并预览
			const PREVIEW_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("解析并预览")
			)
			expect(PREVIEW_BTN).toBeTruthy()
			PREVIEW_BTN?.click()
			await settleView()

			expect(previewCalled).toBe(true)
			expect(MOUNT.container.textContent).toContain("导入检测概览")
			expect(MOUNT.container.textContent).toContain("主人爱喝拿铁")
			expect(MOUNT.container.textContent).toContain("全新记忆")
			expect(MOUNT.container.textContent).toContain("已存在相同条目")
			expect(MOUNT.container.textContent).toContain("检测到冲突")
			expect(MOUNT.container.textContent).toContain("本地已有不同设定")
			expect(MOUNT.container.textContent).toContain("预览说明：仅展示脱敏摘要与核心元数据，不展示或保存内部原始向量与对话原文。")

			// 点击确认并执行导入
			const COMMIT_TRIGGER_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("确认并执行导入")
			)
			expect(COMMIT_TRIGGER_BTN).toBeTruthy()
			COMMIT_TRIGGER_BTN?.click()
			await settleView()

			// 确认模态框出现
			expect(document.body.textContent).toContain("确认导入记忆数据")
			const CONFIRM_COMMIT_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.trim() === "确认导入" && b.classList.contains("btn-primary")
			)
			expect(CONFIRM_COMMIT_BTN).toBeTruthy()
			CONFIRM_COMMIT_BTN?.click()
			await settleView()

			expect(commitCalled).toBe(true)
			expect(listReloads).toBeGreaterThanOrEqual(2)
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})

	it("handles import commit failure and unknown command without faking success", async () => {
		const FEEDBACK_SPY = vi.spyOn(feedback, "error").mockImplementation(() => {})
		let commitAttempts = 0

		const VALID_BACKUP_JSON = JSON.stringify({memories: [{content: "测试记忆"}]})

		const HOST = new MockHost({
			ui_get_snapshot: () => createMockSnapshot(),
			memory_knowledge_status: () => ({state: "ready", processed: 8, total: 8}),
			memory_list_page: () => ({items: [], total: 0}),
			memory_import_preview: () => ({
				valid: true,
				totalCount: 1,
				newCount: 1,
				duplicateCount: 0,
				conflictCount: 0,
				errorCount: 0,
				previewToken: "tok-fail",
				items: [{id: 1, contentSummary: "测试记忆", kind: "general", conflictType: "none"}],
			}),
			memory_import_commit: () => {
				commitAttempts += 1
				throw new Error("未知的命令: memory_import_commit")
			},
		})
		HOST.install()

		const MOUNT = mountComponent()
		try {
			await settleView()

			const TABS = MOUNT.container.querySelectorAll<HTMLButtonElement>("button")
			const TRANSFER_TAB = Array.from(TABS).find(b => b.textContent?.includes("记忆迁移"))
			TRANSFER_TAB?.click()
			await settleView()

			const FILE_INPUT = MOUNT.container.querySelector<HTMLInputElement>("input[type='file']")
			const VALID_FILE = new File([VALID_BACKUP_JSON], "backup.json", {type: "application/json"})
			VALID_FILE.text = vi.fn().mockResolvedValue(VALID_BACKUP_JSON)
			Object.defineProperty(FILE_INPUT, "files", {
				value: [VALID_FILE],
				configurable: true,
			})
			FILE_INPUT?.dispatchEvent(new Event("change"))
			await settleView()

			const PREVIEW_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("解析并预览")
			)
			PREVIEW_BTN?.click()
			await settleView()

			const COMMIT_TRIGGER_BTN = Array.from(MOUNT.container.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.includes("确认并执行导入")
			)
			COMMIT_TRIGGER_BTN?.click()
			await settleView()

			const CONFIRM_COMMIT_BTN = Array.from(document.body.querySelectorAll<HTMLButtonElement>("button")).find(b =>
				b.textContent?.trim() === "确认导入" && b.classList.contains("btn-primary")
			)
			CONFIRM_COMMIT_BTN?.click()
			await settleView()

			expect(commitAttempts).toBe(1)
			expect(FEEDBACK_SPY).toHaveBeenCalledWith("导入记忆失败", expect.any(Error))
			expect(MOUNT.container.textContent).toContain("未知的命令: memory_import_commit")
		} finally {
			MOUNT.app.unmount()
			MOUNT.container.remove()
			HOST.restore()
		}
	})
})
