import {invoke} from "../host/invoke"
import {toolManager, type AgentTool} from "../agent/tools"
import {MCP_MARKETPLACE, type McpMarketplaceItem} from "./marketplace"

export * from "./marketplace"

/**
 * MCP 服务器配置
 */
export interface McpServerConfig {
	id: string
	name: string
	transport: "stdio" | "sse"
	command?: string
	args?: string[]
	env?: Record<string, string>
	url?: string
	enabled: boolean
	autoConnect: boolean
}

/**
 * MCP 工具定义
 */
export interface McpToolDefinition {
	name: string
	description?: string
	inputSchema?: {
		type: string
		properties?: Record<string, {
			type: string
			description?: string
			enum?: string[]
		}>
		required?: string[]
	}
}

/**
 * MCP 资源定义
 */
export interface McpResourceDefinition {
	uri: string
	name: string
	description?: string
	mimeType?: string
}

/**
 * MCP 服务器运行时状态
 */
export interface McpServerStatusInfo {
	serverId: string
	name: string
	status: "disconnected" | "connecting" | "connected" | "error"
	errorMessage?: string
	tools: McpToolDefinition[]
	resources: McpResourceDefinition[]
}

/**
 * MCP 工具执行结果
 */
export interface McpToolResult {
	content: {
		type: string
		text?: string
		data?: string
		mimeType?: string
	}[]
	isError: boolean
}

/**
 * MCP 客户端管理服务 (前端接口、ToolManager 动态同步与网络市场安装)
 */
export class McpService {
	/**
	 * 获取所有配置的 MCP 服务器列表及状态
	 */
	public async getServers(): Promise<McpServerStatusInfo[]> {
		try {
			return await invoke<McpServerStatusInfo[]>("mcp_get_servers")
		} catch (error) {
			console.error("获取 MCP 服务器列表失败:", error)
			return []
		}
	}

	/**
	 * 获取 MCP 市场官方与社区推荐列表
	 */
	public getMarketplace(): McpMarketplaceItem[] {
		return MCP_MARKETPLACE
	}

	/**
	 * 保存或更新服务器配置
	 */
	public async saveServer(config: McpServerConfig): Promise<McpServerStatusInfo> {
		const RES = await invoke<McpServerStatusInfo>("mcp_save_server", config as unknown as Record<string, unknown>)
		await this.syncToolsWithToolManager()
		return RES
	}

	/**
	 * 从市场一键安装 MCP 服务
	 */
	public async installFromMarketplace(
		item: McpMarketplaceItem,
		customArgs?: string[],
		env?: Record<string, string>
	): Promise<McpServerStatusInfo> {
		const CONFIG: McpServerConfig = {
			id: `mcp_${item.id.replace(/^mcp-/, "")}_${Date.now().toString(36).slice(-4)}`,
			name: item.name,
			transport: item.transport,
			command: item.defaultCommand,
			args: customArgs && customArgs.length > 0 ? customArgs : item.defaultArgs,
			env: env && Object.keys(env).length > 0 ? env : undefined,
			url: item.defaultUrl,
			enabled: true,
			autoConnect: true,
		}

		return this.saveServer(CONFIG)
	}

	/**
	 * 从远程网络 URL 或 JSON 清单导入 MCP 配置
	 */
	public async installFromUrl(url: string): Promise<McpServerStatusInfo[]> {
		let text = ""
		try {
			text = await invoke<string>("fetch_remote_text", {url})
		} catch {
			const RES = await fetch(url)
			if (!RES.ok) throw new Error(`HTTP ${RES.status}: ${RES.statusText}`)
			text = await RES.text()
		}

		const PARSED = JSON.parse(text)
		const RESULTS: McpServerStatusInfo[] = []

		// 支持单项 McpServerConfig 或 claude_desktop_config 的 { mcpServers: { [key]: ... } } 格式
		if (PARSED.mcpServers && typeof PARSED.mcpServers === "object") {
			for (const [key, val] of Object.entries(PARSED.mcpServers as Record<string, any>)) {
				const S_CONF: McpServerConfig = {
					id: `mcp_${key.toLowerCase()}_${Date.now().toString(36).slice(-4)}`,
					name: key,
					transport: val.url ? "sse" : "stdio",
					command: val.command || "npx",
					args: Array.isArray(val.args) ? val.args : [],
					env: val.env,
					url: val.url,
					enabled: true,
					autoConnect: true,
				}
				const RES = await this.saveServer(S_CONF)
				RESULTS.push(RES)
			}
		} else if (PARSED.name || PARSED.command || PARSED.url) {
			const S_CONF: McpServerConfig = {
				id: PARSED.id || `mcp_import_${Date.now().toString(36).slice(-4)}`,
				name: PARSED.name || "导入的 MCP 服务",
				transport: PARSED.transport || (PARSED.url ? "sse" : "stdio"),
				command: PARSED.command || "npx",
				args: Array.isArray(PARSED.args) ? PARSED.args : [],
				env: PARSED.env,
				url: PARSED.url,
				enabled: true,
				autoConnect: true,
			}
			const RES = await this.saveServer(S_CONF)
			RESULTS.push(RES)
		} else {
			throw new Error("未识别的 MCP 配置文件结构")
		}

		await this.syncToolsWithToolManager()
		return RESULTS
	}

	/**
	 * 从 npm 包名一键生成并添加 MCP 服务
	 */
	public async installFromNpm(packageName: string, name?: string, args: string[] = []): Promise<McpServerStatusInfo> {
		const CLEAN_NAME = packageName.replace(/^@modelcontextprotocol\/server-/, "").replace(/^mcp-server-/, "")
		const CONFIG: McpServerConfig = {
			id: `mcp_npm_${CLEAN_NAME}_${Date.now().toString(36).slice(-4)}`,
			name: name || `NPM: ${packageName}`,
			transport: "stdio",
			command: "npx",
			args: ["-y", packageName, ...args],
			enabled: true,
			autoConnect: true,
		}

		return this.saveServer(CONFIG)
	}

	/**
	 * 删除服务器配置并断开连接
	 */
	public async deleteServer(id: string): Promise<boolean> {
		const RES = await invoke<boolean>("mcp_delete_server", {id})
		await this.syncToolsWithToolManager()
		return RES
	}

	/**
	 * 连接指定 MCP 服务器
	 */
	public async connectServer(id: string): Promise<McpServerStatusInfo> {
		const RES = await invoke<McpServerStatusInfo>("mcp_connect_server", {id})
		await this.syncToolsWithToolManager()
		return RES
	}

	/**
	 * 断开指定 MCP 服务器
	 */
	public async disconnectServer(id: string): Promise<McpServerStatusInfo> {
		const RES = await invoke<McpServerStatusInfo>("mcp_disconnect_server", {id})
		await this.syncToolsWithToolManager()
		return RES
	}

	/**
	 * 测试服务器连接 (不持久化)
	 */
	public async testServer(config: McpServerConfig): Promise<McpServerStatusInfo> {
		return invoke<McpServerStatusInfo>("mcp_test_server", config as unknown as Record<string, unknown>)
	}

	/**
	 * 调用 MCP 工具
	 *
	 * 带 sessionId 时宿主会登记可取消操作, cancel_agent_session 可中止本次调用
	 */
	public async callTool(serverId: string, toolName: string, args: Record<string, unknown>, sessionId?: string): Promise<McpToolResult> {
		return invoke<McpToolResult>("mcp_call_tool", {
			serverId,
			toolName,
			arguments: args,
			sessionId,
		})
	}

	/**
	 * 将所有已连接的 MCP 服务器暴露的工具同步注册到全局 ToolManager
	 */
	public async syncToolsWithToolManager(): Promise<void> {
		try {
			const SERVERS = await this.getServers()

			// 1. 清理已有 MCP 动态工具
			const ALL_REGISTERED = toolManager.list()
			for (const tool of ALL_REGISTERED) {
				if (tool.name.startsWith("mcp__")) {
					toolManager.unregister(tool.name)
				}
			}

			// 2. 注册当前在线 MCP 服务的全部工具
			for (const server of SERVERS) {
				if (server.status !== "connected") continue

				for (const mcpTool of server.tools) {
					const FULL_NAME = `mcp__${server.serverId}__${mcpTool.name}`
					const WRAPPED_TOOL: AgentTool = {
						name: FULL_NAME,
						description: `[MCP 扩展工具 - 来自 ${server.name}]: ${mcpTool.description || mcpTool.name}`,
						parameters: mcpTool.inputSchema || {
							type: "object",
							properties: {},
							required: [],
						},
						// 未分类的动态外部工具默认需要逐次用户确认, 防止模型直接触发副作用
						permissionLevel: "confirm",
						category: "mcp",
						execute: async (toolArgs, context) => {
							const RES = await this.callTool(server.serverId, mcpTool.name, toolArgs, context?.sessionId)
							if (RES.isError) {
								throw new Error(RES.content.map(c => c.text).filter(Boolean).join("\n") || "MCP 工具执行失败")
							}
							return {
								success: true,
								output: RES.content.map(c => c.text).filter(Boolean).join("\n"),
								raw: RES.content,
							}
						},
					}

					toolManager.register(WRAPPED_TOOL)
				}
			}
		} catch (error) {
			console.error("同步 MCP 工具到 ToolManager 失败:", error)
		}
	}
}

/**
 * 全局 MCP 服务单例
 */
export const mcpService = new McpService()
