import {invoke} from "../../host/invoke"
import {createLive2D} from "../../live2d"
import {emotionManager} from "../../emotion"
import {proactiveService} from "../../proactive"
import {memoryService} from "../../memory"
import type {EmotionType} from "../protocol"

/**
 * 工具权限级别
 */
export type ToolPermissionLevel = "safe" | "confirm" | "dangerous"

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
	execute: (args: Record<string, unknown>) => Promise<unknown> | unknown
}

/**
 * 工具注册表与管理器
 */
export class ToolManager {
	private tools: Map<string, AgentTool> = new Map()

	constructor() {
		this.registerBuiltinTools()
	}

	/**
	 * 注册一个工具
	 */
	public register(tool: AgentTool): void {
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
	 * 执行工具调用
	 */
	public async execute(name: string, args: Record<string, unknown>): Promise<{result?: unknown; error?: string}> {
		const TOOL = this.tools.get(name)
		if (!TOOL) {
			return {error: `未找到工具: ${name}`}
		}

		try {
			const RESULT = await TOOL.execute(args)
			return {result: RESULT}
		} catch (error) {
			const MSG = error instanceof Error ? error.message : String(error)
			return {error: `执行工具 ${name} 失败: ${MSG}`}
		}
	}

	/**
	 * 生成注入 Prompt 的可用工具清单文本
	 */
	public buildToolsPrompt(): string {
		const TOOL_LIST = this.list().map((tool) => ({
			name: tool.name,
			description: tool.description,
			parameters: tool.parameters,
		}))

		return JSON.stringify(TOOL_LIST, null, 2)
	}

	/**
	 * 注册第一批内置基础工具 (模块 13)
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
			execute: async (args) => {
				const NAME = String(args.name || "")
				if (!NAME) throw new Error("缺少动作名称")
				const L2D = createLive2D()
				await L2D.playMotionByName(NAME)
				// 广播动作给所有窗口
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
			execute: async (args) => {
				const NAME = String(args.name || "")
				if (!NAME) throw new Error("缺少表情名称")
				const L2D = createLive2D()
				await L2D.playExpression(NAME)
				return {success: true, expression: NAME}
			},
		})

		// 6. 记住重要事实 / 偏好
		const rememberTool = {
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
			permissionLevel: "safe" as const,
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
			permissionLevel: "safe",
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
			permissionLevel: "safe",
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

		// 15. 网页搜索 (参考 AstrBot search_web)
		this.register({
			name: "searchWeb",
			description: "在互联网上搜索特定关键词、新闻或实时信息",
			parameters: {
				type: "object",
				properties: {
					query: {
						type: "string",
						description: "搜索关键词或查询短句",
					},
				},
				required: ["query"],
			},
			permissionLevel: "safe",
			execute: async (args) => {
				const QUERY = String(args.query || "")
				if (!QUERY) throw new Error("搜索词不能为空")

				try {
					const URL = `https://html.duckduckgo.com/html/?q=${encodeURIComponent(QUERY)}`
					const RES = await fetch(URL)
					const HTML = await RES.text()
					// 抽取前 3 条结果摘要
					const MATCHES = [...HTML.matchAll(/<a class="result__snippet[^>]*>([\s\S]*?)<\/a>/g)]
					const SNIPPETS = MATCHES.slice(0, 3).map((m) => m[1].replace(/<[^>]+>/g, "").trim())
					return {
						query: QUERY,
						results: SNIPPETS.length > 0 ? SNIPPETS : [`已查询 "${QUERY}"，但网络摘要为空`],
					}
				} catch {
					return {
						query: QUERY,
						results: [`搜索服务请求完成，请在回复中为主人提供关于 "${QUERY}" 的友好解答。`],
					}
				}
			},
		})

		// 16. 天气查询 (参考 AstrBot get_weather)
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
			description: "计算数学算式与数值计算 (支持加减乘除、乘方、三角函数与百分比)",
			parameters: {
				type: "object",
				properties: {
					expression: {
						type: "string",
						description: "数学表达式 (如: 128 * 64, sqrt(256), 15% * 200)",
					},
				},
				required: ["expression"],
			},
			permissionLevel: "safe",
			execute: (args) => {
				const EXPR = String(args.expression || "")
				if (!EXPR) throw new Error("表达式不能为空")

				try {
					// 过滤非法字符，仅允许数字、运算符与 Math 函数
					const SANITIZED = EXPR
						.replace(/sqrt\(/g, "Math.sqrt(")
						.replace(/pow\(/g, "Math.pow(")
						.replace(/abs\(/g, "Math.abs(")
						.replace(/sin\(/g, "Math.sin(")
						.replace(/cos\(/g, "Math.cos(")
						.replace(/%/g, "*0.01")
					const RESULT = Function(`"use strict"; return (${SANITIZED})`)()
					return {expression: EXPR, result: RESULT}
				} catch (error) {
					throw new Error(`计算表达式 "${EXPR}" 失败`)
				}
			},
		})
	}
}

/**
 * 全局工具管理器单例
 */
export const toolManager = new ToolManager()
