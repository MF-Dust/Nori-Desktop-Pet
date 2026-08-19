import {invoke} from "../../host/invoke"
import {createLive2D} from "../../live2d"

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
	}
}

/**
 * 全局工具管理器单例
 */
export const toolManager = new ToolManager()
