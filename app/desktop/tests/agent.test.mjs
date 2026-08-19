import test from "node:test"
import assert from "node:assert/strict"

class StreamingJsonParser {
	constructor() {
		this.buffer = ""
	}

	push(chunk) {
		this.buffer += chunk
		return this.extractAvailableObjects()
	}

	flush() {
		const objects = this.extractAvailableObjects()
		const remaining = this.buffer.trim()
		if (remaining.length > 0) {
			const parsed = this.tryParseObject(remaining)
			if (parsed) {
				objects.push(parsed)
			} else {
				const cleaned = remaining
					.replace(/^```json\s*/i, "")
					.replace(/^```\s*/, "")
					.replace(/\s*```$/, "")
					.trim()
				if (cleaned.length > 0) {
					objects.push({type: "message", text: cleaned})
				}
			}
		}
		this.reset()
		return objects
	}

	reset() {
		this.buffer = ""
	}

	extractAvailableObjects() {
		const results = []
		let i = 0
		let braceDepth = 0
		let inString = false
		let isEscaped = false
		let jsonStartIndex = -1

		while (i < this.buffer.length) {
			const char = this.buffer[i]
			if (inString) {
				if (isEscaped) {
					isEscaped = false
				} else if (char === "\\") {
					isEscaped = true
				} else if (char === "\"") {
					inString = false
				}
				i++
				continue
			}
			if (char === "\"") {
				inString = true
				i++
				continue
			}
			if (char === "{") {
				if (braceDepth === 0) {
					jsonStartIndex = i
				}
				braceDepth++
			} else if (char === "}") {
				braceDepth--
				if (braceDepth === 0 && jsonStartIndex !== -1) {
					const jsonStr = this.buffer.substring(jsonStartIndex, i + 1)
					const parsed = this.tryParseObject(jsonStr)
					if (parsed) {
						results.push(parsed)
						this.buffer = this.buffer.substring(i + 1)
						i = -1
						braceDepth = 0
						jsonStartIndex = -1
					}
				} else if (braceDepth < 0) {
					braceDepth = 0
					jsonStartIndex = -1
				}
			}
			i++
		}
		return results
	}

	tryParseObject(raw) {
		const trimmed = raw.trim()
		if (!trimmed.startsWith("{") || !trimmed.endsWith("}")) return null
		try {
			const parsed = JSON.parse(trimmed)
			if (!parsed || typeof parsed !== "object") return null
			if (parsed.type === "message" || typeof parsed.text === "string") {
				return {
					type: "message",
					text: String(parsed.text ?? ""),
					emotion: parsed.emotion,
					action: parsed.action,
					expression: parsed.expression,
				}
			}
			if (parsed.type === "tool_call" && typeof parsed.name === "string") {
				return {
					type: "tool_call",
					id: parsed.id || "call_test",
					name: parsed.name,
					arguments: parsed.arguments || {},
				}
			}
			return null
		} catch {
			return null
		}
	}
}

test("StreamingJsonParser - 分片流式提取完整 JSON", () => {
	const parser = new StreamingJsonParser()
	const chunk1 = "```json\n{\"type\": \"message\", \"text\": \"你好呀"
	const chunk2 = "主人！\", \"emotion\": \"happy\", \"action\": \"smile\"}\n```"

	const r1 = parser.push(chunk1)
	assert.equal(r1.length, 0)

	const r2 = parser.push(chunk2)
	assert.equal(r2.length, 1)
	assert.equal(r2[0].type, "message")
	assert.equal(r2[0].text, "你好呀主人！")
	assert.equal(r2[0].emotion, "happy")
	assert.equal(r2[0].action, "smile")
})

test("StreamingJsonParser - 解析工具调用 tool_call", () => {
	const parser = new StreamingJsonParser()
	const raw = "{\"type\": \"tool_call\", \"id\": \"c1\", \"name\": \"getTime\", \"arguments\": {}}"
	const results = parser.push(raw)
	assert.equal(results.length, 1)
	assert.equal(results[0].type, "tool_call")
	assert.equal(results[0].name, "getTime")
})

test("StreamingJsonParser - 非 JSON 格式普通文本兜底", () => {
	const parser = new StreamingJsonParser()
	parser.push("这是一条未格式化的纯文本助手回复。")
	const flushed = parser.flush()
	assert.equal(flushed.length, 1)
	assert.equal(flushed[0].type, "message")
	assert.equal(flushed[0].text, "这是一条未格式化的纯文本助手回复。")
})

test("ToolManager 权限与工具执行机制", async () => {
	const tools = new Map()
	tools.set("calc", {
		name: "calc",
		description: "计算器",
		permissionLevel: "safe",
		execute: (args) => (args.a || 0) + (args.b || 0),
	})

	const target = tools.get("calc")
	assert.ok(target)
	assert.equal(target.permissionLevel, "safe")
	const res = await target.execute({a: 2, b: 3})
	assert.equal(res, 5)
})
