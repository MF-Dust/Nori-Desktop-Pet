import {describe, expect, it} from "vitest"
import {hasBlockStructure, splitAssistantMessage} from "../../src/services/chat/split"

describe("助手回复分段", () => {
	it("换行优先", () => {
		expect(splitAssistantMessage("第一句\n第二句\n\n第三句")).toEqual(["第一句", "第二句", "第三句"])
	})

	it("单段落按句末标点切分", () => {
		expect(splitAssistantMessage("你好呀！今天想聊点什么？"))
			.toEqual(["你好呀！", "今天想聊点什么？"])
	})

	it("超长句子按逗号二次切分", () => {
		const LONG = `${"很长的描述".repeat(12)}，${"再来一段".repeat(10)}。`
		const PARTS = splitAssistantMessage(LONG)
		expect(PARTS.length).toBeGreaterThan(1)
		expect(PARTS.every(part => part.length > 0)).toBe(true)
	})

	it("含结构化 Markdown 时不拆分", () => {
		const CASES = [
			"这是说明\n```ts\nconst a = 1\n```",
			"步骤:\n- 第一步\n- 第二步",
			"| 列 | 值 |\n| --- | --- |\n| a | 1 |",
			"# 标题\n正文",
			"> 引用\n正文",
			"1. 第一条\n2. 第二条",
		]
		for (const text of CASES) {
			expect(hasBlockStructure(text), text).toBe(true)
			expect(splitAssistantMessage(text)).toHaveLength(1)
		}
	})

	it("流式进行中不拆分", () => {
		expect(splitAssistantMessage("你好呀！今天想聊点什么？", {streaming: true}))
			.toEqual(["你好呀！今天想聊点什么？"])
	})

	it("空白输入返回空数组", () => {
		expect(splitAssistantMessage("   \n  ")).toEqual([])
	})
})
