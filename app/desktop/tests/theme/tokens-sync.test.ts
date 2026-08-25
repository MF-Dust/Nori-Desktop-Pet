import {readFileSync} from "node:fs"
import {resolve} from "node:path"
import {describe, expect, it} from "vitest"
import {COLORS, CSS_VARIABLES, SHADOWS} from "../../src/assets/style/tokens"
import {naiveThemeOverrides} from "../../src/assets/style/naiveOverrides"

const ROOT = resolve(__dirname, "../..")
const NAIVE_OVERRIDES = "src/assets/style/naiveOverrides.ts"

/** 颜色字面量: 裸 hex 与 rgb()/rgba() */
const COLOR_LITERAL = /#[0-9a-fA-F]{3,8}(?![0-9a-fA-F])|rgba?\([^)]*\)/g

/** 取一段文本里的所有颜色字面量 */
const readColorLiterals = (text: string): string[] => text.match(COLOR_LITERAL) ?? []

/** 归一化色值, 让 `rgba(10, 28, 44, 0.55)` 与紧凑写法能比较 */
const normalizeColor = (literal: string): string => literal.replace(/\s+/g, "").toLowerCase()

/** tokens.ts 认可的全部色值 (含阴影里嵌的色值, 阴影本身也是令牌) */
const TOKEN_COLORS = new Set([
	...Object.values(COLORS).flatMap(readColorLiterals),
	...Object.values(SHADOWS).flatMap(readColorLiterals),
].map(normalizeColor))

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

	it("naiveOverrides.ts 里不写裸色值 (hex / rgb / rgba 一律从 tokens.ts 引)", () => {
		const SOURCE = readFileSync(resolve(ROOT, NAIVE_OVERRIDES), "utf8")
		const OFFENDERS = [...new Set(readColorLiterals(SOURCE))]
			.map(literal => `${NAIVE_OVERRIDES}: 裸色值 ${literal}, 请改成 COLORS / SHADOWS 里的令牌`)
		expect(OFFENDERS).toEqual([])
	})

	it("naive 主题里的每个色值都能追溯到 tokens.ts", () => {
		// 除了看源码, 还要看展开后的值: 色值也可能从别的模块组装进来
		const OFFENDERS = [...new Set(readColorLiterals(JSON.stringify(naiveThemeOverrides)))]
			.filter(literal => !TOKEN_COLORS.has(normalizeColor(literal)))
			.map(literal => `${literal} 不在 tokens.ts 的色值集合里 (COLORS + SHADOWS)`)
		expect(OFFENDERS).toEqual([])
	})
})
