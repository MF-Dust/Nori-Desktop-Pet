/**
 * 设置搜索索引
 *
 * 原先每个二级页在 SettingsPanel 里手写一串 keywords, 新增设置项时没人回来补,
 * 于是搜「音色」搜不到语音页。这里改成直接从语言包生成索引:
 *   · 页面文案 (标题、字段名、说明) 全部进入检索文本 → 搜得到的就是屏幕上看得见的字
 *   · i18n 键名本身也进入检索文本 → 键名是英文, 中文界面下搜 telemetry / apikey 同样命中
 *   · 带 title 的直接子树算作「小节」, 命中时在列表项下方提示命中位置
 */
import {i18n} from "../i18n"

/** 语言包子树 */
export type MessageTree = {[key: string]: MessageNode}

/** 语言包节点: 字符串叶子或子树 */
export type MessageNode = string | MessageTree

/** 页内小节 (语言包里带 title 的直接子树) */
export interface SettingsSearchSection {
	title: string
	haystack: string
}

/** 单个二级页的检索条目 */
export interface SettingsSearchEntry {
	/** 该页全部文案与键名的归一化检索文本 */
	haystack: string
	/** 该页小节, 用于展示命中位置 */
	sections: SettingsSearchSection[]
}

/** 待索引的二级页 */
export interface SettingsSearchSource {
	/** 二级页 key, 同时是它在语言包里的子树名 */
	key: string
	/** 列表里显示的名字 (与子树标题不一定相同, 一并纳入检索) */
	label: string
	/** 该页拥有的语言包子树 */
	page: MessageNode | undefined
}

/** 归一化: 统一小写并折叠空白, 保证 includes 判定稳定 */
const normalize = (text: string): string => text.toLowerCase().replace(/\s+/g, " ").trim()

/** 递归收集子树里的字符串叶子与键名 */
const collectInto = (node: MessageNode | undefined, out: Set<string>): void => {
	if (node === undefined) return
	if (typeof node === "string") {
		const TEXT = normalize(node)
		if (TEXT) out.add(TEXT)
		return
	}
	for (const [key, value] of Object.entries(node)) {
		out.add(normalize(key))
		collectInto(value, out)
	}
}

/** 拼成单行检索文本 (换行分隔, 避免相邻片段拼出不存在的词) */
const joinParts = (parts: Set<string>): string => [...parts].join("\n")

/** 构建单页条目 */
const buildEntry = (source: SettingsSearchSource): SettingsSearchEntry => {
	const PARTS = new Set<string>()
	PARTS.add(normalize(source.key))
	PARTS.add(normalize(source.label))
	collectInto(source.page, PARTS)

	const SECTIONS: SettingsSearchSection[] = []
	if (source.page && typeof source.page !== "string") {
		for (const [key, child] of Object.entries(source.page)) {
			if (typeof child === "string" || typeof child.title !== "string") continue
			const CHILD_PARTS = new Set<string>([normalize(key)])
			collectInto(child, CHILD_PARTS)
			SECTIONS.push({title: child.title, haystack: joinParts(CHILD_PARTS)})
		}
	}

	return {haystack: joinParts(PARTS), sections: SECTIONS}
}

/**
 * 生成索引 (key → 条目)
 */
export const buildSettingsSearchIndex = (sources: readonly SettingsSearchSource[]): Map<string, SettingsSearchEntry> =>
	new Map(sources.map(source => [source.key, buildEntry(source)]))

/**
 * 命中判定
 *
 * 返回 null 表示未命中; 命中时返回可展示的小节标题 (命中的是页级文案时为空数组)。
 */
export const matchSettingsEntry = (entry: SettingsSearchEntry | undefined, needle: string): string[] | null => {
	const NEEDLE = normalize(needle)
	if (!NEEDLE) return []
	if (!entry?.haystack.includes(NEEDLE)) return null
	return entry.sections.filter(section => section.haystack.includes(NEEDLE)).map(section => section.title)
}

/**
 * 取当前语言包的 `views.main` 子树
 *
 * 在 computed 里调用即可跟随语言切换 (locale 与 messages 都是响应式的)。
 */
export const settingsMessageRoot = (): MessageTree | undefined => {
	const MESSAGES = i18n.global.messages.value as unknown as Record<string, MessageNode | undefined>
	const TREE = MESSAGES[i18n.global.locale.value] ?? MESSAGES["zh-CN"]
	if (!TREE || typeof TREE === "string") return undefined
	const VIEWS = TREE.views
	if (!VIEWS || typeof VIEWS === "string") return undefined
	const MAIN = VIEWS.main
	return MAIN && typeof MAIN !== "string" ? MAIN : undefined
}
