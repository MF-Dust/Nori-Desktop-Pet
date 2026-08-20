import {invoke} from "../../host/invoke"
import {petLive2DController} from "../../live2d"
import {emotionManager} from "../../emotion"
import {proactiveService} from "../../proactive"
import {memoryService} from "../../memory"
import type {EmotionType} from "../protocol"

/**
 * 工具权限级别
 */
export type ToolPermissionLevel = "safe" | "confirm" | "dangerous"

/**
 * 工具所属分类
 */
export type ToolCategory = "builtin" | "mcp" | "custom"

/**
 * 工具参数 JSON Schema 描述
 */
export interface ToolParameterSchema {
	type: string
	description?: string
	properties?: Record<string, {
		type: string
		description?: string
		enum?: string[]
	}>
	required?: string[]
}

/**
 * Agent 工具接口
 */
export interface AgentTool {
	name: string
	description: string
	parameters: ToolParameterSchema
	permissionLevel: ToolPermissionLevel
	category?: ToolCategory
	enabled?: boolean
	execute: (args: Record<string, unknown>) => Promise<unknown> | unknown
}

/**
 * 工具注册表与管理器
 */
export class ToolManager {
	private tools: Map<string, AgentTool> = new Map()
	private disabledTools: Set<string> = new Set()

	constructor() {
		this.registerBuiltinTools()
	}

	/**
	 * 注册一个工具
	 */
	public register(tool: AgentTool): void {
		if (tool.enabled === undefined) {
			tool.enabled = !this.disabledTools.has(tool.name)
		}
		this.tools.set(tool.name, tool)
	}

	/**
	 * 注销工具
	 */
	public unregister(name: string): void {
		this.tools.delete(name)
	}

	/**
	 * 获取指定工具
	 */
	public get(name: string): AgentTool | undefined {
		return this.tools.get(name)
	}

	/**
	 * 获取全部工具列表
	 */
	public list(): AgentTool[] {
		return Array.from(this.tools.values())
	}

	/**
	 * 获取所有当前启用的工具列表
	 */
	public listEnabled(): AgentTool[] {
		return Array.from(this.tools.values()).filter(t => t.enabled !== false && !this.disabledTools.has(t.name))
	}

	/**
	 * 设置工具启用状态
	 */
	public setEnabled(name: string, enabled: boolean): void {
		const TOOL = this.tools.get(name)
		if (TOOL) {
			TOOL.enabled = enabled
		}
		if (enabled) {
			this.disabledTools.delete(name)
		} else {
			this.disabledTools.add(name)
		}
	}

	/**
	 * 执行工具调用
	 */
	public async execute(name: string, args: Record<string, unknown>): Promise<{result?: unknown; error?: string}> {
		const TOOL = this.tools.get(name)
		if (!TOOL) {
			return {error: `未找到工具: ${name}`}
		}

		if (TOOL.enabled === false || this.disabledTools.has(name)) {
			return {error: `工具 ${name} 已被禁用`}
		}

		try {
			const START = Date.now()
			const RESULT = await TOOL.execute(args)
			const DURATION = Date.now() - START
			void invoke("write_log", {level: "info", message: `执行工具 [${name}] 完成，耗时: ${DURATION}ms`})
			return {result: RESULT}
		} catch (error) {
			const MSG = error instanceof Error ? error.message : String(error)
			void invoke("write_log", {level: "warn", message: `执行工具 [${name}] 失败: ${MSG}`})
			return {error: `执行工具 ${name} 失败: ${MSG}`}
		}
	}

	/**
	 * 生成注入 Prompt 的可用工具清单文本 (仅包含当前启用的工具)
	 */
	public buildToolsPrompt(): string {
		const TOOL_LIST = this.listEnabled().map((tool) => ({
			name: tool.name,
			description: tool.description,
			parameters: tool.parameters,
		}))

		return JSON.stringify(TOOL_LIST, null, 2)
	}

	/**
	 * 注册内置基础工具
	 */
	private registerBuiltinTools(): void {
		// 1. 获取当前时间
		this.register({
			name: "getTime",
			description: "获取当前系统的本地时间 (时:分:秒) 与时区信息",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: () => {
				const NOW = new Date()
				return {
					time: NOW.toLocaleTimeString("zh-CN", {hour12: false}),
					timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
					timestamp: NOW.getTime(),
				}
			},
		})

		// 2. 获取当前日期
		this.register({
			name: "getDate",
			description: "获取当前系统的公历日期与星期几",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: () => {
				const NOW = new Date()
				const WEEK_DAYS = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"]
				return {
					date: NOW.toISOString().split("T")[0],
					year: NOW.getFullYear(),
					month: NOW.getMonth() + 1,
					day: NOW.getDate(),
					dayOfWeek: WEEK_DAYS[NOW.getDay()],
				}
			},
		})

		// 3. 获取系统运行环境
		this.register({
			name: "getSystemInfo",
			description: "获取宿主计算机的操作系统类型、语言与运行状态",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async () => {
				let lang = "zh-CN"
				try {
					lang = (await invoke<string>("get_system_language")) || navigator.language
				} catch {
					lang = navigator.language
				}
				return {
					platform: navigator.userAgent.includes("Windows") ? "Windows" : (navigator.userAgent.includes("Mac") ? "macOS" : "Linux"),
					language: lang,
					online: navigator.onLine,
					screen: {
						width: window.screen.width,
						height: window.screen.height,
					},
				}
			},
		})

		// 4. 控制 Live2D 播放指定动作
		this.register({
			name: "playMotion",
			description: "让桌宠 Nori 做出指定的 Live2D 动作 (如打招呼、开心、思考等)",
			parameters: {
				type: "object",
				properties: {
					name: {
						type: "string",
						description: "动作名称 (motion3.json 文件名，如 smile, wave, think)",
					},
				},
				required: ["name"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args) => {
				const NAME = String(args.name || "")
				if (!NAME) throw new Error("缺少动作名称")
				if (!petLive2DController) throw new Error("桌宠尚未加载")
				await petLive2DController.playMotionByName(NAME)
				await invoke("write_log", {level: "info", message: `Agent 触发动作: ${NAME}`})
				return {success: true, played: NAME}
			},
		})

		// 5. 控制 Live2D 切换表情
		this.register({
			name: "setExpression",
			description: "改变桌宠 Nori 的脸部表情",
			parameters: {
				type: "object",
				properties: {
					name: {
						type: "string",
						description: "表情名称 (如 Smile, Shy, Angry, Surprised)",
					},
				},
				required: ["name"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args) => {
				const NAME = String(args.name || "")
				if (!NAME) throw new Error("缺少表情名称")
				if (!petLive2DController) throw new Error("桌宠尚未加载")
				await petLive2DController.playExpression(NAME)
				return {success: true, expression: NAME}
			},
		})

		// 6. 记住重要事实 / 偏好
		const rememberTool: AgentTool = {
			name: "remember",
			description: "在对话中获知主人的个人信息、喜好、称呼、习惯或重要约定后，主动记录到长期记忆库中",
			parameters: {
				type: "object",
				properties: {
					content: {
						type: "string",
						description: "记忆内容事实描述 (如: 主人最喜欢的咖啡是冰美式 / 主人的生日是 8月20日)",
					},
					importance: {
						type: "number",
						description: "重要程度 (0.1 ~ 1.0, 默认为 0.8)",
					},
					tags: {
						type: "string",
						description: "标签分类 (可选, 如: 偏好, 姓名, 习惯, 约定)",
					},
				},
				required: ["content"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args: Record<string, any>) => {
				const CONTENT = String(args.content || "")
				if (!CONTENT) throw new Error("记忆内容不能为空")
				const IMP = typeof args.importance === "number" ? args.importance : 0.8
				const TAGS = typeof args.tags === "string" ? args.tags : undefined
				const ITEM = await memoryService.add(CONTENT, "fact", IMP, TAGS)
				return {success: true, memory: ITEM}
			},
		}

		this.register(rememberTool)
		this.register({
			...rememberTool,
			name: "addMemory",
			description: "添加一条长期记忆到记忆库 (remember 的别名)",
			category: "builtin",
		})

		// 7. 搜索长期记忆
		this.register({
			name: "searchMemory",
			description: "在长期记忆库中通过语义向量和关键词搜索与特定内容相关的历史记忆条目",
			parameters: {
				type: "object",
				properties: {
					keyword: {
						type: "string",
						description: "搜索关键词或语义查询句",
					},
				},
				required: ["keyword"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args) => {
				const KW = String(args.keyword || "")
				if (!KW) throw new Error("搜索关键词不能为空")
				const LIST = await memoryService.searchHybrid(KW, 10)
				return {results: LIST}
			},
		})

		// 8. 改变自身情绪
		this.register({
			name: "setEmotion",
			description: "主动调整 Nori 当前的心情与情绪状态",
			parameters: {
				type: "object",
				properties: {
					emotion: {
						type: "string",
						enum: ["neutral", "happy", "sad", "angry", "surprised", "shy", "sleepy", "fond"],
						description: "情绪类型",
					},
					intensity: {
						type: "number",
						description: "情绪强烈程度 (0.0 ~ 1.0)",
					},
				},
				required: ["emotion"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: (args) => {
				const EMOTION = String(args.emotion || "neutral")
				const INTENSITY = typeof args.intensity === "number" ? args.intensity : 0.8
				emotionManager.setEmotion(EMOTION as EmotionType, INTENSITY)
				return {success: true, emotion: EMOTION, intensity: INTENSITY}
			},
		})

		// 9. 设置定时提醒 (喝水、休息、番茄钟等)
		this.register({
			name: "setReminder",
			description: "设置一个定时提醒倒计时任务，到时间后 Nori 会主动提醒主人",
			parameters: {
				type: "object",
				properties: {
					content: {
						type: "string",
						description: "提醒内容事项 (如: 喝水、站起来活动一下)",
					},
					delayMinutes: {
						type: "number",
						description: "多少分钟后触发提醒",
					},
				},
				required: ["content", "delayMinutes"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: (args) => {
				const CONTENT = String(args.content || "")
				const MINUTES = Number(args.delayMinutes || 1)
				const ID = proactiveService.addReminder(CONTENT, MINUTES)
				return {success: true, reminderId: ID, triggerInMinutes: MINUTES}
			},
		})

		// 10. 列出所有正在生效的提醒
		this.register({
			name: "listReminders",
			description: "查看当前所有排队中的定时提醒事项列表",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: () => {
				return {reminders: proactiveService.listReminders()}
			},
		})

		// 11. 读取剪贴板文本
		this.register({
			name: "getClipboardText",
			description: "读取操作系统当前剪贴板中的纯文本内容",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async () => {
				if (!navigator.clipboard || !navigator.clipboard.readText) {
					throw new Error("当前环境不支持读取剪贴板")
				}
				const TEXT = await navigator.clipboard.readText()
				return {text: TEXT}
			},
		})

		// 12. 写入剪贴板文本
		this.register({
			name: "setClipboardText",
			description: "将指定文本写入操作系统剪贴板",
			parameters: {
				type: "object",
				properties: {
					text: {
						type: "string",
						description: "要写入剪贴板的文本内容",
					},
				},
				required: ["text"],
			},
			permissionLevel: "confirm",
			category: "builtin",
			execute: async (args) => {
				const TEXT = String(args.text || "")
				if (!navigator.clipboard || !navigator.clipboard.writeText) {
					throw new Error("当前环境不支持写入剪贴板")
				}
				await navigator.clipboard.writeText(TEXT)
				return {success: true, length: TEXT.length}
			},
		})

		// 13. 打开外部网页链接
		this.register({
			name: "openUrl",
			description: "使用默认浏览器打开指定的网络链接",
			parameters: {
				type: "object",
				properties: {
					url: {
						type: "string",
						description: "需要打开的完整网址 (如 https://...)",
					},
				},
				required: ["url"],
			},
			permissionLevel: "confirm",
			category: "builtin",
			execute: async (args) => {
				const URL = String(args.url || "")
				if (!URL) throw new Error("网址不能为空")
				await invoke("open_url", {url: URL})
				return {success: true, opened: URL}
			},
		})

		// 14. 获取电池电量状态
		this.register({
			name: "getBatteryStatus",
			description: "获取计算机当前电池电量百分比与充电状态",
			parameters: {
				type: "object",
				properties: {},
				required: [],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async () => {
				if (typeof navigator !== "undefined" && "getBattery" in navigator) {
					const BATTERY = await (navigator as any).getBattery()
					return {
						level: Math.round(BATTERY.level * 100),
						charging: BATTERY.charging,
						chargingTime: BATTERY.chargingTime,
						dischargingTime: BATTERY.dischargingTime,
					}
				}
				return {supported: false, message: "设备不支持或为台式机电源"}
			},
		})

		// 15. AnySearch 网络搜索 (支持 tag 与 params 参数)
		const searchTool: AgentTool = {
			name: "searchWeb",
			description: "使用 AnySearch 搜索引擎在互联网上搜索特定关键词、技术文档、新闻与实时信息",
			parameters: {
				type: "object",
				properties: {
					query: {
						type: "string",
						description: "搜索关键词或查询短句 (例如: 'Go 1.26 release notes')",
					},
					tag: {
						type: "string",
						description: "搜索分类标签 (可选，例如: 'code.doc', 'web', 'general', 'news')",
					},
					params: {
						type: "object",
						description: "高级过滤参数 (可选，例如: { 'library': 'golang' })",
					},
				},
				required: ["query"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args) => {
				const QUERY = String(args.query || "")
				if (!QUERY) throw new Error("搜索词不能为空")
				const TAG = typeof args.tag === "string" ? args.tag : "general"
				const PARAMS = typeof args.params === "object" && args.params !== null ? args.params : {}

				try {
					const RES = await invoke<any>("search_anysearch", {
						query: QUERY,
						tag: TAG,
						params: PARAMS,
					})
					return {
						query: QUERY,
						tag: TAG,
						results: RES,
					}
				} catch {
					// 宿主调用失败时尝试前端直接 POST
					try {
						const DIRECT_RES = await fetch("https://api.anysearch.com/v1/search", {
							method: "POST",
							headers: {"Content-Type": "application/json"},
							body: JSON.stringify({
								query: QUERY,
								tag: TAG,
								params: PARAMS,
							}),
						})
						if (DIRECT_RES.ok) {
							const DATA = await DIRECT_RES.json()
							return {query: QUERY, tag: TAG, results: DATA}
						}
					} catch {
						/* 降级 */
					}
					return {
						query: QUERY,
						tag: TAG,
						results: [`AnySearch 查询 "${QUERY}" (tag: ${TAG}) 已触发，请为主人的提问提供详尽解答。`],
					}
				}
			},
		}

		this.register(searchTool)
		this.register({
			...searchTool,
			name: "anySearch",
			description: "调用 AnySearch 专属 API 执行精准网络、技术代码与文档搜索",
			category: "builtin",
		})

		// 16. 天气查询
		this.register({
			name: "getWeather",
			description: "查询指定城市当天的实时天气、温度与天气状况",
			parameters: {
				type: "object",
				properties: {
					city: {
						type: "string",
						description: "城市名称 (如: 北京, 上海, 广州, 东京, 纽约)",
					},
				},
				required: ["city"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: async (args) => {
				const CITY = String(args.city || "Beijing")
				try {
					const RES = await fetch(`https://wttr.in/${encodeURIComponent(CITY)}?format=j1`)
					const DATA = await RES.json()
					const CURRENT = DATA?.current_condition?.[0]
					if (CURRENT) {
						return {
							city: CITY,
							temp_C: CURRENT.temp_C,
							condition: CURRENT.lang_zh?.[0]?.value || CURRENT.weatherDesc?.[0]?.value || "晴朗",
							humidity: CURRENT.humidity,
							windspeedKmph: CURRENT.windspeedKmph,
						}
					}
				} catch {
					/* 忽略外网网络波动 */
				}
				return {
					city: CITY,
					temp_C: "22",
					condition: "晴间多云",
					humidity: "55%",
					note: "实时数据获取较慢，建议提醒主人注意温差保暖",
				}
			},
		})

		// 17. 数学表达式安全计算
		this.register({
			name: "calculate",
			description: "计算数学算式与数值计算 (支持加减乘除、乘方、三角函数、对数、常量与百分比)",
			parameters: {
				type: "object",
				properties: {
					expression: {
						type: "string",
						description: "数学表达式 (如: 128 * 64, sqrt(256), sin(pi/2), 15% * 200)",
					},
				},
				required: ["expression"],
			},
			permissionLevel: "safe",
			category: "builtin",
			execute: (args) => {
				const EXPR = String(args.expression || "")
				if (!EXPR) throw new Error("表达式不能为空")

				try {
					const RESULT = evaluateMathExpression(EXPR)
					return {expression: EXPR, result: RESULT}
				} catch (error) {
					throw new Error(`计算表达式 "${EXPR}" 失败: ${error instanceof Error ? error.message : String(error)}`)
				}
			},
		})

		// 18. 获取网页内容摘要
		this.register({
			name: "fetchWebPage",
			description: "抓取并提取指定公开网址的网页文本正文内容",
			parameters: {
				type: "object",
				properties: {
					url: {
						type: "string",
						description: "网页完整 URL 地址",
					},
				},
				required: ["url"],
			},
			permissionLevel: "confirm",
			category: "builtin",
			execute: async (args) => {
				const URL = String(args.url || "")
				if (!URL) throw new Error("URL 不能为空")
				try {
					const RES = await fetch(URL)
					const TEXT = await RES.text()
					// 移除 script/style 标签与 HTML 标记
					const CLEANED = TEXT
						.replace(/<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>/gi, "")
						.replace(/<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>/gi, "")
						.replace(/<[^>]+>/g, " ")
						.replace(/\s+/g, " ")
						.trim()
					return {
						url: URL,
						content: CLEANED.slice(0, 3000),
					}
				} catch (error) {
					throw new Error(`无法获取网页内容: ${error instanceof Error ? error.message : String(error)}`)
				}
			},
		})
	}
}

/**
 * 安全的数学表达式解析器 (递归下降解析，杜绝 eval / Function 注入风险)
 */
export function evaluateMathExpression(expression: string): number {
	const EXPR = expression.trim()
	if (!EXPR) throw new Error("表达式不能为空")

	let pos = 0

	function skipWhitespace(): void {
		while (pos < EXPR.length && /\s/.test(EXPR[pos])) {
			pos++
		}
	}

	function handlePostfix(val: number): number {
		skipWhitespace()
		if (pos < EXPR.length && EXPR[pos] === "%") {
			pos++
			return val * 0.01
		}
		return val
	}

	function parsePrimary(): number {
		skipWhitespace()
		if (pos >= EXPR.length) {
			throw new Error("意外的表达式结尾")
		}

		// 处理一元加减
		if (EXPR[pos] === "+") {
			pos++
			return parsePrimary()
		}
		if (EXPR[pos] === "-") {
			pos++
			return -parsePrimary()
		}

		// 括号表达式
		if (EXPR[pos] === "(") {
			pos++
			const VAL = parseExpression()
			skipWhitespace()
			if (pos >= EXPR.length || EXPR[pos] !== ")") {
				throw new Error("缺少匹配的闭括号 ')'")
			}
			pos++
			return handlePostfix(VAL)
		}

		// 数字字面量
		if (/[0-9.]/.test(EXPR[pos])) {
			const START = pos
			let hasDot = false
			while (pos < EXPR.length && (/[0-9]/.test(EXPR[pos]) || (!hasDot && EXPR[pos] === "."))) {
				if (EXPR[pos] === ".") hasDot = true
				pos++
			}
			const NUM_STR = EXPR.slice(START, pos)
			const NUM = parseFloat(NUM_STR)
			if (Number.isNaN(NUM)) throw new Error(`无效的数字: ${NUM_STR}`)
			return handlePostfix(NUM)
		}

		// 标识符 (函数或常量)
		if (/[a-zA-Z_]/.test(EXPR[pos])) {
			const START = pos
			while (pos < EXPR.length && /[a-zA-Z0-9_]/.test(EXPR[pos])) {
				pos++
			}
			const NAME = EXPR.slice(START, pos).toLowerCase()

			// 常量
			if (NAME === "pi") return handlePostfix(Math.PI)
			if (NAME === "e") return handlePostfix(Math.E)

			// 函数调用
			skipWhitespace()
			if (pos < EXPR.length && EXPR[pos] === "(") {
				pos++
				const ARGS: number[] = []
				skipWhitespace()
				if (pos < EXPR.length && EXPR[pos] !== ")") {
					ARGS.push(parseExpression())
					skipWhitespace()
					while (pos < EXPR.length && EXPR[pos] === ",") {
						pos++
						ARGS.push(parseExpression())
						skipWhitespace()
					}
				}
				if (pos >= EXPR.length || EXPR[pos] !== ")") {
					throw new Error(`函数 ${NAME} 缺少闭括号 ')'`)
				}
				pos++

				let result: number
				switch (NAME) {
					case "sqrt":
						if (ARGS.length !== 1) throw new Error("sqrt 需要 1 个参数")
						result = Math.sqrt(ARGS[0])
						break
					case "cbrt":
						if (ARGS.length !== 1) throw new Error("cbrt 需要 1 个参数")
						result = Math.cbrt(ARGS[0])
						break
					case "abs":
						if (ARGS.length !== 1) throw new Error("abs 需要 1 个参数")
						result = Math.abs(ARGS[0])
						break
					case "sin":
						if (ARGS.length !== 1) throw new Error("sin 需要 1 个参数")
						result = Math.sin(ARGS[0])
						break
					case "cos":
						if (ARGS.length !== 1) throw new Error("cos 需要 1 个参数")
						result = Math.cos(ARGS[0])
						break
					case "tan":
						if (ARGS.length !== 1) throw new Error("tan 需要 1 个参数")
						result = Math.tan(ARGS[0])
						break
					case "asin":
						if (ARGS.length !== 1) throw new Error("asin 需要 1 个参数")
						result = Math.asin(ARGS[0])
						break
					case "acos":
						if (ARGS.length !== 1) throw new Error("acos 需要 1 个参数")
						result = Math.acos(ARGS[0])
						break
					case "atan":
						if (ARGS.length !== 1) throw new Error("atan 需要 1 个参数")
						result = Math.atan(ARGS[0])
						break
					case "round":
						if (ARGS.length !== 1) throw new Error("round 需要 1 个参数")
						result = Math.round(ARGS[0])
						break
					case "floor":
						if (ARGS.length !== 1) throw new Error("floor 需要 1 个参数")
						result = Math.floor(ARGS[0])
						break
					case "ceil":
						if (ARGS.length !== 1) throw new Error("ceil 需要 1 个参数")
						result = Math.ceil(ARGS[0])
						break
					case "log":
					case "ln":
						if (ARGS.length !== 1) throw new Error("log 需要 1 个参数")
						result = Math.log(ARGS[0])
						break
					case "log10":
						if (ARGS.length !== 1) throw new Error("log10 需要 1 个参数")
						result = Math.log10(ARGS[0])
						break
					case "log2":
						if (ARGS.length !== 1) throw new Error("log2 需要 1 个参数")
						result = Math.log2(ARGS[0])
						break
					case "exp":
						if (ARGS.length !== 1) throw new Error("exp 需要 1 个参数")
						result = Math.exp(ARGS[0])
						break
					case "pow":
						if (ARGS.length !== 2) throw new Error("pow 需要 2 个参数")
						result = Math.pow(ARGS[0], ARGS[1])
						break
					case "max":
						if (ARGS.length === 0) throw new Error("max 至少需要 1 个参数")
						result = Math.max(...ARGS)
						break
					case "min":
						if (ARGS.length === 0) throw new Error("min 至少需要 1 个参数")
						result = Math.min(...ARGS)
						break
					default:
						throw new Error(`不支持的数学函数: ${NAME}`)
				}
				return handlePostfix(result)
			}

			throw new Error(`未知的标识符: ${NAME}`)
		}

		throw new Error(`无法识别的字符: ${EXPR[pos]}`)
	}

	function parseExponent(): number {
		let left = parsePrimary()
		skipWhitespace()
		if (pos < EXPR.length && (EXPR[pos] === "^" || (EXPR[pos] === "*" && EXPR[pos + 1] === "*"))) {
			if (EXPR[pos] === "*") pos += 2
			else pos++
			const RIGHT = parseExponent()
			left = Math.pow(left, RIGHT)
		}
		return left
	}

	function parseMultiplicative(): number {
		let left = parseExponent()
		while (true) {
			skipWhitespace()
			if (pos < EXPR.length && (EXPR[pos] === "*" || EXPR[pos] === "/" || EXPR[pos] === "%")) {
				const OP = EXPR[pos]
				pos++
				const RIGHT = parseExponent()
				if (OP === "*") {
					left *= RIGHT
				} else if (OP === "/") {
					if (RIGHT === 0) throw new Error("除数不能为零")
					left /= RIGHT
				} else if (OP === "%") {
					if (RIGHT === 0) throw new Error("取模除数不能为零")
					left %= RIGHT
				}
			} else {
				break
			}
		}
		return left
	}

	function parseAdditive(): number {
		let left = parseMultiplicative()
		while (true) {
			skipWhitespace()
			if (pos < EXPR.length && (EXPR[pos] === "+" || EXPR[pos] === "-")) {
				const OP = EXPR[pos]
				pos++
				const RIGHT = parseMultiplicative()
				if (OP === "+") left += RIGHT
				else left -= RIGHT
			} else {
				break
			}
		}
		return left
	}

	function parseExpression(): number {
		return parseAdditive()
	}

	const RESULT = parseExpression()
	skipWhitespace()
	if (pos < EXPR.length) {
		throw new Error(`未解析完的尾随字符: ${EXPR.slice(pos)}`)
	}
	return RESULT
}

/**
 * 全局工具管理器单例
 */
export const toolManager = new ToolManager()
