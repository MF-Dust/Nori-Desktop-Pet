import {beforeEach, describe, expect, it, vi} from "vitest"

const MOCKS = vi.hoisted(() => ({
	invoke: vi.fn(),
	embed: vi.fn(),
}))

vi.mock("../../src/services/host/invoke", () => ({invoke: MOCKS.invoke}))
vi.mock("../../src/services/embedding", () => ({
	embeddingService: {embed: MOCKS.embed},
}))

import {MemoryService} from "../../src/services/memory"

describe("MemoryService 向量重建", () => {
	beforeEach(() => {
		MOCKS.invoke.mockReset()
		MOCKS.embed.mockReset()
	})

	it("按 id 游标遍历全部分页并更新每条向量", async () => {
		MOCKS.invoke.mockImplementation(async (command: string, args?: Record<string, unknown>) => {
			if (command === "get_unembedded_memories" && args?.afterId === 0) {
				return [
					{id: 3, content: "一"},
					{id: 5, content: "二"},
				]
			}
			if (command === "get_unembedded_memories" && args?.afterId === 5) {
				return [{id: 8, content: "三"}]
			}
			if (command === "get_unembedded_memories" && args?.afterId === 8) return []
			return true
		})
		MOCKS.embed.mockImplementation(async (content: string) => [content.length])

		const SERVICE = new MemoryService()
		expect(await SERVICE.reembedAll()).toBe(3)
		expect(MOCKS.invoke.mock.calls.filter(([command]) => command === "get_unembedded_memories")).toEqual([
			["get_unembedded_memories", {limit: 100, afterId: 0}],
			["get_unembedded_memories", {limit: 100, afterId: 5}],
			["get_unembedded_memories", {limit: 100, afterId: 8}],
		])
		expect(MOCKS.invoke.mock.calls.filter(([command]) => command === "update_memory_embedding")).toHaveLength(3)
	})

	it("更新文本时提交新向量", async () => {
		MOCKS.embed.mockResolvedValue([0.1, 0.2])
		MOCKS.invoke.mockResolvedValue(true)

		const SERVICE = new MemoryService()
		expect(await SERVICE.update(7, "新内容", 0.8, "标签")).toBe(true)
		expect(MOCKS.invoke).toHaveBeenCalledWith("update_memory", {
			id: 7,
			content: "新内容",
			importance: 0.8,
			tags: "标签",
			embedding: "[0.1,0.2]",
		})
	})

	it("生成向量失败时仍提交更新以清空旧向量", async () => {
		MOCKS.embed.mockResolvedValue(null)
		MOCKS.invoke.mockResolvedValue(true)

		const SERVICE = new MemoryService()
		await SERVICE.update(7, "新内容")
		expect(MOCKS.invoke).toHaveBeenCalledWith("update_memory", {
			id: 7,
			content: "新内容",
			importance: undefined,
			tags: undefined,
			embedding: undefined,
		})
	})
})
