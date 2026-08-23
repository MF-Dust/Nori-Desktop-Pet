import {readdirSync, readFileSync, statSync} from "node:fs"
import {join, relative, resolve} from "node:path"
import {describe, expect, it} from "vitest"

const ROOT = resolve(__dirname, "../..")
const SRC = join(ROOT, "src")

/**
 * 尚未完成原子化迁移的文件 (P2 已全量清空)
 *
 * 迁移完成的文件不允许再出现 scoped 样式块、px 长度、裸 hex 色值与超小字号。
 * 名单保留作为逗号后置的逗号阀: 日后新增组件如果要临时开特例, 必须显式列在这里。
 */
const LEGACY_FILES: string[] = []

const listFiles = (dir: string, extension: string): string[] => {
	const OUT: string[] = []
	for (const entry of readdirSync(dir)) {
		const FULL = join(dir, entry)
		if (statSync(FULL).isDirectory()) OUT.push(...listFiles(FULL, extension))
		else if (entry.endsWith(extension)) OUT.push(FULL)
	}
	return OUT
}

const relativeSrc = (file: string): string => relative(SRC, file).replace(/\\/g, "/")

const migratedVueFiles = (): string[] =>
	listFiles(SRC, ".vue").filter(file => !LEGACY_FILES.includes(relativeSrc(file)))

describe("样式规范静态检查", () => {
	it("迁移完成的组件不再带 scoped 样式块", () => {
		const OFFENDERS = migratedVueFiles().filter(file => readFileSync(file, "utf8").includes("<style scoped"))
		expect(OFFENDERS.map(relativeSrc)).toEqual([])
	})

	it("迁移完成的组件不使用 px 长度", () => {
		const OFFENDERS: string[] = []
		for (const file of migratedVueFiles()) {
			const MATCH = readFileSync(file, "utf8").match(/\b\d+(\.\d+)?px\b/g)
			if (MATCH) OFFENDERS.push(`${relativeSrc(file)}: ${MATCH.join(", ")}`)
		}
		expect(OFFENDERS).toEqual([])
	})

	it("迁移完成的组件不写裸 hex 色值 (统一走令牌)", () => {
		const OFFENDERS: string[] = []
		for (const file of migratedVueFiles()) {
			// 后面不能再跟十六进制字符 (Uno 任意值里的 #xxx_0% 也要能抳到)
			const MATCH = readFileSync(file, "utf8").match(/#[0-9a-fA-F]{3,8}(?![0-9a-fA-F])/g)
			if (MATCH) OFFENDERS.push(`${relativeSrc(file)}: ${MATCH.join(", ")}`)
		}
		expect(OFFENDERS).toEqual([])
	})

	it("迁移完成的组件没有小于 1.15rem 的字号", () => {
		const OFFENDERS: string[] = []
		for (const file of [...migratedVueFiles(), ...listFiles(SRC, ".ts")]) {
			const SOURCE = readFileSync(file, "utf8")
			for (const match of SOURCE.matchAll(/font-size:\s*([\d.]+)rem|text-\[([\d.]+)rem\]/g)) {
				const SIZE = Number(match[1] ?? match[2])
				if (SIZE < 1.15) OFFENDERS.push(`${relativeSrc(file)}: ${SIZE}rem`)
			}
		}
		expect(OFFENDERS).toEqual([])
	})

	it("legacy 名单只包含真实存在且仍未迁移的文件", () => {
		const ALL = new Set(listFiles(SRC, ".vue").map(relativeSrc))
		for (const file of LEGACY_FILES) {
			expect(ALL.has(file), `${file} 已不存在, 请从 legacy 名单移除`).toBe(true)
			expect(readFileSync(join(SRC, file), "utf8")).toContain("<style scoped")
		}
	})
})
