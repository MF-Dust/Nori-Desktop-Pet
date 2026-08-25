import {describe, expect, it} from "vitest"
import ZH from "../../src/services/i18n/locales/zh-CN"
import {buildSettingsSearchIndex, matchSettingsEntry} from "../../src/services/settings/searchIndex"
import type {MessageTree} from "../../src/services/settings/searchIndex"

const MAIN = (ZH as unknown as {views: {main: MessageTree}}).views.main

/** 与 SettingsPanel 一致的二级页列表 (key 即语言包子树名) */
const TAB_KEYS = ["ai", "voice", "proactive", "skills", "mcp", "general", "debug", "about"] as const

const REAL_INDEX = buildSettingsSearchIndex(TAB_KEYS.map(key => ({key, label: key, page: MAIN[key]})))

/** 搜某个词能命中哪些二级页 */
const hits = (needle: string): string[] =>
	TAB_KEYS.filter(key => matchSettingsEntry(REAL_INDEX.get(key), needle) !== null)

describe("设置搜索索引", () => {
	const FAKE = buildSettingsSearchIndex([
		{
			key: "general",
			label: "系统常规",
			page: {
				title: "系统与常规设置",
				telemetry: {
					title: "诊断与隐私",
					enabled: "发送匿名诊断数据",
				},
				startup: {
					title: "启动与运行行为",
					autoSummon: "启动时自动唤出桌宠",
				},
				loose: "没有小节的散装文案",
			},
		},
	])
	const ENTRY = FAKE.get("general")

	it("命中字段文案, 并报出所在小节", () => {
		expect(matchSettingsEntry(ENTRY, "匿名诊断")).toEqual(["诊断与隐私"])
		expect(matchSettingsEntry(ENTRY, "唤出桌宠")).toEqual(["启动与运行行为"])
	})

	it("命中英文键名 (中文语言包下也能搜英文术语)", () => {
		expect(matchSettingsEntry(ENTRY, "telemetry")).toEqual(["诊断与隐私"])
		expect(matchSettingsEntry(ENTRY, "autoSummon")).toEqual(["启动与运行行为"])
	})

	it("命中页级文案时返回空小节列表", () => {
		expect(matchSettingsEntry(ENTRY, "散装")).toEqual([])
		expect(matchSettingsEntry(ENTRY, "系统常规")).toEqual([])
	})

	it("大小写与首尾空白不影响判定", () => {
		expect(matchSettingsEntry(ENTRY, "  TELEMETRY ")).toEqual(["诊断与隐私"])
	})

	it("未命中返回 null, 空搜索视为全部命中", () => {
		expect(matchSettingsEntry(ENTRY, "不存在的词")).toBeNull()
		expect(matchSettingsEntry(ENTRY, "")).toEqual([])
		expect(matchSettingsEntry(undefined, "telemetry")).toBeNull()
	})

	// 回归: 手写 keywords 换成语言包生成后, 原来那批关键词必须照样能搜到
	it("旧手写关键词仍然命中对应二级页", () => {
		expect(hits("apikey")).toContain("ai")
		expect(hits("人设")).toContain("ai")
		expect(hits("tts")).toContain("voice")
		expect(hits("音量")).toContain("voice")
		expect(hits("reminder")).toContain("proactive")
		expect(hits("提醒")).toContain("proactive")
		expect(hits("skill")).toContain("skills")
		expect(hits("mcp")).toContain("mcp")
		expect(hits("telemetry")).toContain("general")
		expect(hits("language")).toContain("general")
		expect(hits("log")).toContain("debug")
		expect(hits("license")).toContain("about")
		expect(hits("协议")).toContain("about")
	})

	it("每个二级页都有可检索文本", () => {
		for (const key of TAB_KEYS) expect(REAL_INDEX.get(key)?.haystack.length, key).toBeGreaterThan(80)
	})
})
