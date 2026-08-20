import {describe, expect, it} from "vitest"
import {StreamingJsonParser} from "../../src/services/agent/jsonParser"

describe("StreamingJsonParser 流式协议解析", () => {
	it("分片流式提取完整 JSON", () => {
		const parser = new StreamingJsonParser()
		const chunk1 = "```json\n{\"type\": \"message\", \"text\": \"你好呀"
		const chunk2 = "主人！\", \"emotion\": \"happy\", \"action\": \"smile\"}\n```"

		expect(parser.push(chunk1)).toHaveLength(0)
		const results = parser.push(chunk2)
		expect(results).toHaveLength(1)
		expect(results[0]).toMatchObject({
			type: "message",
			text: "你好呀主人！",
			emotion: "happy",
			action: "smile",
		})
	})

	it("解析工具调用 tool_call", () => {
		const parser = new StreamingJsonParser()
		const results = parser.push("{\"type\": \"tool_call\", \"id\": \"c1\", \"name\": \"getTime\", \"arguments\": {}}")
		expect(results).toHaveLength(1)
		expect(results[0]).toMatchObject({
			type: "tool_call",
			name: "getTime",
		})
		expect((results[0] as {arguments?: unknown}).arguments).toEqual({})
	})

	it("解析事件类型 event", () => {
		const parser = new StreamingJsonParser()
		const results = parser.push("{\"type\": \"event\", \"name\": \"pet-motion\", \"payload\": {\"name\": \"wave\"}}")
		expect(results).toHaveLength(1)
		expect(results[0]).toMatchObject({
			type: "event",
			name: "pet-motion",
		})
	})

	it("非 JSON 格式普通文本兜底", () => {
		const parser = new StreamingJsonParser()
		parser.push("这是一条未格式化的纯文本助手回复。")
		const flushed = parser.flush()
		expect(flushed).toHaveLength(1)
		expect(flushed[0]).toMatchObject({
			type: "message",
			text: "这是一条未格式化的纯文本助手回复。",
		})
	})

	it("l2dAction 兼容别名映射到 action", () => {
		const parser = new StreamingJsonParser()
		const results = parser.push("{\"type\": \"message\", \"text\": \"hi\", \"l2dAction\": \"wave\"}")
		expect(results[0]).toMatchObject({action: "wave"})
	})

	it("静态 parseComplete 直接解析完整输出", () => {
		const results = StreamingJsonParser.parseComplete("```json\n{\"type\": \"message\", \"text\": \"ok\"}\n```")
		expect(results).toHaveLength(1)
		expect(results[0]).toMatchObject({type: "message", text: "ok"})
	})
})