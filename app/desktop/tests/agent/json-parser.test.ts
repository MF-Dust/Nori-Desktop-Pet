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

	it("tool_call 的 arguments 为 JSON 字符串时二次解析", () => {
		const parser = new StreamingJsonParser()
		const results = parser.push("{\"type\": \"tool_call\", \"name\": \"getWeather\", \"arguments\": \"{\\\"city\\\": \\\"北京\\\"}\"}")
		expect(results).toHaveLength(1)
		expect(results[0]).toMatchObject({type: "tool_call", name: "getWeather"})
		expect((results[0] as {arguments?: Record<string, unknown>}).arguments).toEqual({city: "北京"})
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

	it("逐字符小 chunk 的对象只产出一次且结果正确", () => {
		const parser = new StreamingJsonParser()
		const FULL = "{\"type\": \"message\", \"text\": \"你好呀主人！\", \"emotion\": \"happy\", \"action\": \"smile\"}"

		let collected: ReturnType<StreamingJsonParser["push"]> = []
		for (const CH of FULL) {
			collected = collected.concat(parser.push(CH))
		}
		expect(collected).toHaveLength(1)
		expect(collected[0]).toMatchObject({type: "message", text: "你好呀主人！", emotion: "happy", action: "smile"})
	})

	it("转义引号跨 chunk 时字符串状态保持正确", () => {
		const parser = new StreamingJsonParser()
		const CHUNK1 = '{"type": "tool_call", "name": "run", "arguments": {"cmd": "echo \\"hello'
		const CHUNK2 = ' world\\""}}'
		expect(parser.push(CHUNK1)).toHaveLength(0)
		const RESULTS = parser.push(CHUNK2)
		expect(RESULTS).toHaveLength(1)
		expect((RESULTS[0] as {arguments?: Record<string, unknown>}).arguments).toEqual({cmd: 'echo "hello world"'})
	})

	it("连续对象在同一调用内全部输出", () => {
		const parser = new StreamingJsonParser()
		const RESULTS = parser.push(
			"{\"type\": \"message\", \"text\": \"a\"}{\"type\": \"event\", \"name\": \"e1\"}" +
			"{\"type\": \"tool_call\", \"id\": \"c9\", \"name\": \"t\", \"arguments\": {}}"
		)
		expect(RESULTS.map((item) => item.type)).toEqual(["message", "event", "tool_call"])
	})

	it("非法平衡对象后仍能继续解析后续对象", () => {
		const parser = new StreamingJsonParser()
		const FIRST = parser.push("{oops: 1}")
		expect(FIRST).toHaveLength(0)
		const SECOND = parser.push("{\"type\": \"message\", \"text\": \"ok\"}")
		expect(SECOND).toHaveLength(1)
		const FLUSHED = parser.flush()
		// 与旧实现一致: 后续成功解析会连同之前的非法平衡段一起消费
		expect(FLUSHED).toHaveLength(0)
	})

	it("超限的未闭合 payload 抛出可处理错误", () => {
		const parser = new StreamingJsonParser(32)
		parser.push("{\"type\": \"message\", \"text\": \"")
		expect(() => parser.push("x".repeat(64))).toThrowError(/上限/)
	})
})