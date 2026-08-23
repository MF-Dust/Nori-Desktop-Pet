import {readFileSync} from "node:fs"
import {resolve} from "node:path"
import {describe, expect, it} from "vitest"
import {CSS_VARIABLES} from "../../src/assets/style/tokens"
import {naiveThemeOverrides} from "../../src/assets/style/naiveOverrides"

const ROOT = resolve(__dirname, "../..")

/** 取 theme.less 里 :root 块声明的变量 */
const readThemeVariables = (): Record<string, string> => {
	const SOURCE = readFileSync(resolve(ROOT, "src/assets/style/theme.less"), "utf8")
	const BLOCK = /:root\s*\{([\s\S]*?)\n\}/.exec(SOURCE)
	expect(BLOCK, "theme.less 缺少 :root 令牌块").not.toBeNull()
	const ENTRIES: Record<string, string> = {}
	for (const line of (BLOCK as RegExpExecArray)[1].split("\n")) {
		const MATCH = /^\s*(--[\w-]+)\s*:\s*(.+?);\s*$/.exec(line)
		if (MATCH) ENTRIES[MATCH[1]] = MATCH[2].trim()
	}
	return ENTRIES
}

describe("设计令牌单一色源", () => {
	it("theme.less 的 :root 与 tokens.ts 逐条一致", () => {
		const THEME = readThemeVariables()
		expect(Object.keys(THEME).sort()).toEqual(Object.keys(CSS_VARIABLES).sort())
		for (const [name, value] of Object.entries(CSS_VARIABLES)) {
			expect(THEME[name], `令牌 ${name} 漂移`).toBe(value)
		}
	})

	it("naive 主题覆盖派生自令牌 (不出现旧的低对比度色值)", () => {
		const JSON_TEXT = JSON.stringify(naiveThemeOverrides)
		// 旧值: 次要文字 / 禁用色 / 占位色
		for (const stale of ["#8ba8be", "#4a677d", "rgba(139, 168, 190, 0.55)", "#c7d9e8", "#f0f8ff"]) {
			expect(JSON_TEXT, `naive 主题仍在用旧色值 ${stale}`).not.toContain(stale)
		}
		expect(naiveThemeOverrides.common?.textColor3).toBe(CSS_VARIABLES["--text-muted"])
		expect(naiveThemeOverrides.common?.primaryColor).toBe(CSS_VARIABLES["--nori-teal"])
	})
})
