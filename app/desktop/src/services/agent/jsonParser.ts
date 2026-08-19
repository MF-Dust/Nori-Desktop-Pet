import type {AgentProtocolItem, AgentTextMessage} from "./protocol"

/**
 * 流式 JSON / Markdown 解析器
 *
 * 处理 LLM 输出的 ```json 代码块包裹、分段 JSON 对象以及普通文本兜底
 */
export class StreamingJsonParser {
	private buffer = ""

	/**
	 * 追加流式文本分片并尝试解析出完整的 Agent 协议对象
	 */
	public push(chunk: string): AgentProtocolItem[] {
		this.buffer += chunk
		return this.extractAvailableObjects()
	}

	/**
	 * 流结束时刷新剩余内容
	 */
	public flush(): AgentProtocolItem[] {
		const OBJECTS = this.extractAvailableObjects()
		const REMAINING = this.buffer.trim()

		if (REMAINING.length > 0) {
			// 尝试最后一次完整解析
			const PARSED = this.tryParseObject(REMAINING)
			if (PARSED) {
				OBJECTS.push(PARSED)
			} else {
				// 若不是合法 JSON，清理掉代码块标记后作为纯文本消息兜底
				const CLEANED_TEXT = REMAINING
					.replace(/^```json\s*/i, "")
					.replace(/^```\s*/, "")
					.replace(/\s*```$/, "")
					.trim()

				if (CLEANED_TEXT.length > 0) {
					const FALLBACK_MSG: AgentTextMessage = {
						type: "message",
						text: CLEANED_TEXT,
					}
					OBJECTS.push(FALLBACK_MSG)
				}
			}
		}

		this.reset()
		return OBJECTS
	}

	/**
	 * 重置解析器状态
	 */
	public reset(): void {
		this.buffer = ""
	}

	/**
	 * 从当前缓冲区中提取所有闭合的 JSON 对象
	 */
	private extractAvailableObjects(): AgentProtocolItem[] {
		const RESULTS: AgentProtocolItem[] = []
		let i = 0
		let braceDepth = 0
		let inString = false
		let isEscaped = false
		let jsonStartIndex = -1

		while (i < this.buffer.length) {
			const CHAR = this.buffer[i]

			if (inString) {
				if (isEscaped) {
					isEscaped = false
				} else if (CHAR === "\\") {
					isEscaped = true
				} else if (CHAR === "\"") {
					inString = false
				}
				i++
				continue
			}

			if (CHAR === "\"") {
				inString = true
				i++
				continue
			}

			if (CHAR === "{") {
				if (braceDepth === 0) {
					jsonStartIndex = i
				}
				braceDepth++
			} else if (CHAR === "}") {
				braceDepth--
				if (braceDepth === 0 && jsonStartIndex !== -1) {
					const JSON_STR = this.buffer.substring(jsonStartIndex, i + 1)
					const PARSED = this.tryParseObject(JSON_STR)
					if (PARSED) {
						RESULTS.push(PARSED)
						// 消费掉已解析部分
						this.buffer = this.buffer.substring(i + 1)
						i = -1 // 下一轮循环从 0 开始
						braceDepth = 0
						jsonStartIndex = -1
					}
				} else if (braceDepth < 0) {
					// 括号失配纠正
					braceDepth = 0
					jsonStartIndex = -1
				}
			}

			i++
		}

		return RESULTS
	}

	/**
	 * 尝试将字符串解析为合法 Agent 协议对象
	 */
	private tryParseObject(raw: string): AgentProtocolItem | null {
		const TRIMMED = raw.trim()
		if (!TRIMMED.startsWith("{") || !TRIMMED.endsWith("}")) return null

		try {
			const PARSED = JSON.parse(TRIMMED) as Record<string, unknown>
			if (!PARSED || typeof PARSED !== "object") return null

			// 1. message 类型
			if (PARSED.type === "message" || typeof PARSED.text === "string") {
				return {
					type: "message",
					text: String(PARSED.text ?? ""),
					emotion: typeof PARSED.emotion === "string" ? PARSED.emotion : undefined,
					expression: typeof PARSED.expression === "string" ? PARSED.expression : undefined,
					action: typeof PARSED.action === "string" ? PARSED.action : (typeof PARSED.l2dAction === "string" ? PARSED.l2dAction : undefined),
				} as AgentTextMessage
			}

			// 2. tool_call 类型
			if (PARSED.type === "tool_call" && typeof PARSED.name === "string") {
				return {
					type: "tool_call",
					id: typeof PARSED.id === "string" ? PARSED.id : `call_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
					name: PARSED.name,
					arguments: (typeof PARSED.arguments === "object" && PARSED.arguments !== null ? PARSED.arguments : {}) as Record<string, unknown>,
				}
			}

			// 3. event 类型
			if (PARSED.type === "event" && typeof PARSED.name === "string") {
				return {
					type: "event",
					name: PARSED.name,
					payload: PARSED.payload,
				}
			}

			return null
		} catch {
			return null
		}
	}

	/**
	 * 静态辅助方法：直接解析一次完整的 LLM 输出
	 */
	public static parseComplete(raw: string): AgentProtocolItem[] {
		const PARSER = new StreamingJsonParser()
		PARSER.push(raw)
		return PARSER.flush()
	}
}
