import {readFileSync} from "node:fs"
import {join} from "node:path"
import {describe, expect, it} from "vitest"

const ROOT = process.cwd()

describe("设置卡片滚动布局", () => {
	it("AppCard 不被 flex 容器压缩导致内部内容裁切", () => {
		const CARD = readFileSync(join(ROOT, "src/components/ui/AppCard.vue"), "utf8")
		const ROOT_CLASS = CARD.match(/<section class="([^"]+)"/)?.[1] ?? ""

		expect(ROOT_CLASS.split(/\s+/)).toContain("shrink-0")
	})
})
