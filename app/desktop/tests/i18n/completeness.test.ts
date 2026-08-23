import {readdirSync, readFileSync, statSync} from "node:fs"
import {join, relative, resolve} from "node:path"
import {describe, expect, it} from "vitest"
import ZH from "../../src/services/i18n/locales/zh-CN"
import EN from "../../src/services/i18n/locales/en-US"

const ROOT = resolve(__dirname, "../..")
const SRC = join(ROOT, "src")

type Tree = {[key: string]: string | Tree}

/** 摊平成点号路径集合 */
const flatten = (node: Tree, prefix = ""): Map<string, string> => {
	const OUT = new Map<string, string>()
	for (const [key, value] of Object.entries(node)) {
		const PATH = prefix ? `${prefix}.${key}` : key
		if (typeof value === "string") OUT.set(PATH, value)
		else for (const [path, text] of flatten(value, PATH)) OUT.set(path, text)
	}
	return OUT
}

const listFiles = (dir: string, extension: string): string[] => {
	const OUT: string[] = []
	for (const entry of readdirSync(dir)) {
		const FULL = join(dir, entry)
		if (statSync(FULL).isDirectory()) OUT.push(...listFiles(FULL, extension))
		else if (entry.endsWith(extension)) OUT.push(FULL)
	}
	return OUT
}

const ZH_KEYS = flatten(ZH as Tree)
const EN_KEYS = flatten(EN as Tree)

describe("i18n 完整性", () => {
	it("zh-CN 与 en-US 键集合完全一致", () => {
		const MISSING_EN = [...ZH_KEYS.keys()].filter(key => !EN_KEYS.has(key))
		const MISSING_ZH = [...EN_KEYS.keys()].filter(key => !ZH_KEYS.has(key))
		expect(MISSING_EN, "en-US 缺少这些键").toEqual([])
		expect(MISSING_ZH, "zh-CN 缺少这些键").toEqual([])
	})

	it("useLanguages 访问器覆盖全部键", () => {
		const SOURCE = readFileSync(join(SRC, "services/i18n/useLanguages.ts"), "utf8")
		const REFERENCED = new Set([...SOURCE.matchAll(/t\("([^"]+)"\)/g)].map(match => match[1]))
		const UNCOVERED = [...ZH_KEYS.keys()].filter(key => !REFERENCED.has(key))
		expect(UNCOVERED, "这些键没有出现在 useLanguages 里").toEqual([])
		const STALE = [...REFERENCED].filter(key => !ZH_KEYS.has(key))
		expect(STALE, "useLanguages 引用了不存在的键").toEqual([])
	})

	it("en-US 没有残留中文", () => {
		const OFFENDERS = [...EN_KEYS.entries()]
			.filter(([, value]) => /[\u4e00-\u9fff]/.test(value))
			.map(([key, value]) => `${key}: ${value}`)
		expect(OFFENDERS).toEqual([])
	})

	it("组件模板里没有中文字面量 (全部走 i18n)", () => {
		const OFFENDERS: string[] = []
		for (const file of listFiles(SRC, ".vue")) {
			const SOURCE = readFileSync(file, "utf8")
			const TEMPLATE = /<template>([\s\S]*)<\/template>/.exec(SOURCE)
			if (!TEMPLATE) continue
			// 注释里的中文说明是允许的
			const BODY = TEMPLATE[1].replace(/<!--[\s\S]*?-->/g, "")
			const HITS = BODY.match(/[\u4e00-\u9fff]+/g)
			if (HITS) OFFENDERS.push(`${relative(SRC, file).replace(/\\/g, "/")}: ${HITS.slice(0, 5).join(" ")}`)
		}
		expect(OFFENDERS).toEqual([])
	})
})
