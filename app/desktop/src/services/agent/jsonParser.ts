import type {AgentProtocolItem, AgentTextMessage} from "./protocol"

/**
 * 流式 JSON / Markdown 解析器
 *
 * 处理 LLM 输出的 ```json 代码块包裹、分段 JSON 对象以及普通文本兜底。
 *
 * 扫描状态 (游标、括号深度、字符串/转义态) 在多次 push 之间保持,
 * 每个 chunk 只扫描新增文本, 未闭合对象不会被反复从头扫描;
 * 单个未闭合 payload 超过上限时抛出可处理错误而不是无限占用内存。
 */
export class StreamingJsonParser {
	/** 默认的未完成缓冲区上限 (字符数) */
	public static readonly DEFAULT_MAX_PENDING_BUFFER = 1_000_000

	private buffer = ""
	/** 下一次扫描的起点: 已确认为垃圾前缀或已消费的部分不再重复扫描 */
	private scanIndex = 0
	private jsonStartIndex = -1
	private braceDepth = 0
	private inString = false
	private isEscaped = false

	/** 单个未闭合 payload 的上限, 测试可用小值覆盖 */
	public constructor(private readonly maxPendingBuffer: number = StreamingJsonParser.DEFAULT_MAX_PENDING_BUFFER) {
		if (maxPendingBuffer <= 0) throw new RangeError("maxPendingBuffer 必须为正数")
	}

	/**
	 * 追加流式文本分片并尝试解析出完整的 Agent 协议对象
	 */
	public push(chunk: string): AgentProtocolItem[] {
		this.buffer += chunk
		const RESULTS = this.extractAvailableObjects()
		// 上限针对“未完成”的输入: 未闭合对象从起点计, 普通垃圾前缀按全量计
		const PENDING_SIZE = this.hasOpenObject()
			? this.buffer.length - Math.max(this.jsonStartIndex, 0)
			: this.buffer.length
		if (PENDING_SIZE > this.maxPendingBuffer) {
			this.reset()
			throw new Error(`流式解析缓冲区超过上限 (${this.maxPendingBuffer} 字符)，已终止当前输入`)
		}
		return RESULTS
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
		this.scanIndex = 0
		this.jsonStartIndex = -1
		this.braceDepth = 0
		this.inString = false
		this.isEscaped = false
	}

	/** 是否存在尚未闭合的对象 (用于超限判定) */
	private hasOpenObject(): boolean {
		return this.jsonStartIndex !== -1 || this.braceDepth > 0
	}

	/**
	 * 从上次扫描位置继续提取所有闭合的 JSON 对象.
	 *
	 * 只扫描新增区间; 提取完整对象后消费已确认前缀并按需复位状态,
	 * 同一调用内可以连续输出多个对象。
	 */
	private extractAvailableObjects(): AgentProtocolItem[] {
		const RESULTS: AgentProtocolItem[] = []
		let i = this.scanIndex

		while (i < this.buffer.length) {
			const CHAR = this.buffer[i]

			if (this.inString) {
				if (this.isEscaped) {
					this.isEscaped = false
				} else if (CHAR === "\\") {
					this.isEscaped = true
				} else if (CHAR === "\"") {
					this.inString = false
				}
				i++
				continue
			}

			if (CHAR === "\"") {
				this.inString = true
				i++
				continue
			}

			if (CHAR === "{") {
				if (this.braceDepth === 0) {
					this.jsonStartIndex = i
				}
				this.braceDepth++
			} else if (CHAR === "}") {
				this.braceDepth--
				if (this.braceDepth === 0 && this.jsonStartIndex !== -1) {
					const JSON_STR = this.buffer.substring(this.jsonStartIndex, i + 1)
					const PARSED = this.tryParseObject(JSON_STR)
					if (PARSED) {
						RESULTS.push(PARSED)
						// 消费掉已解析部分与之前的垃圾前缀
						this.buffer = this.buffer.substring(i + 1)
						i = -1
						this.scanIndex = 0
					}
					// 解析失败时保留原文交给 flush 兜底, 只复位状态继续扫描;
					// 该区间位于 scanIndex 之前, 后续 push 不会重复扫描
					this.braceDepth = 0
					this.jsonStartIndex = -1
				} else if (this.braceDepth < 0) {
					// 括号失配纠正: 该字符视为普通文本
					this.braceDepth = 0
					this.jsonStartIndex = -1
				}
			}

			i++
		}

		// 记录已扫描到的位置: 未闭合对象的中间部分在后续 push 中不会重扫
		this.scanIndex = i
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
				let args: Record<string, unknown> = {}
				if (typeof PARSED.arguments === "object" && PARSED.arguments !== null) {
					args = PARSED.arguments as Record<string, unknown>
				} else if (typeof PARSED.arguments === "string" && PARSED.arguments.trim()) {
					// 部分模型会把 arguments 输出为 JSON 字符串, 尝试二次解析
					try {
						const PARSED_ARGS = JSON.parse(PARSED.arguments)
						if (typeof PARSED_ARGS === "object" && PARSED_ARGS !== null) {
							args = PARSED_ARGS as Record<string, unknown>
						}
					} catch {
						/* 忽略无效参数 JSON, 保持空对象 */
					}
				}

				return {
					type: "tool_call",
					id: typeof PARSED.id === "string" ? PARSED.id : `call_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
					name: PARSED.name,
					arguments: args,
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
		const FROM_PUSH = PARSER.push(raw)
		const FROM_FLUSH = PARSER.flush()
		return [...FROM_PUSH, ...FROM_FLUSH]
	}
}
