import type {IconName} from "../icon"

/**
 * MCP 市场分类
 */
export type McpCategory = "system" | "web" | "developer" | "database" | "productivity"

/**
 * MCP 市场条目定义
 */
export interface McpMarketplaceItem {
	id: string
	name: string
	description: string
	author: string
	category: McpCategory
	icon: IconName
	transport: "stdio" | "sse"
	defaultCommand: string
	defaultArgs: string[]
	defaultUrl?: string
	requiredEnv?: {
		key: string
		description: string
		placeholder: string
	}[]
	npmPackage?: string
	readmeUrl?: string
}

/**
 * 精选官方与社区 MCP 扩展市场列表 (MCP Marketplace Hub)
 */
export const MCP_MARKETPLACE: McpMarketplaceItem[] = [
	{
		id: "mcp-anysearch",
		name: "AnySearch 智能搜索引擎",
		description: "调用 AnySearch 专属 API 执行精准网络、技术代码与专业文档搜索 (支持 tag 与 params)",
		author: "AnySearch Team",
		category: "web",
		icon: "sparkles",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "mcp-server-anysearch"],
		requiredEnv: [
			{
				key: "ANYSEARCH_API_KEY",
				description: "AnySearch API Key (可选)",
				placeholder: "as_live_...",
			},
		],
		readmeUrl: "https://api.anysearch.com/v1/search",
	},
	{
		id: "mcp-filesystem",
		name: "本地文件系统 (Filesystem)",
		description: "允许 Nori 安全读取、检索与操作指定本地目录下的文件与文件夹结构",
		author: "Anthropic / MCP Official",
		category: "system",
		icon: "package",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "@modelcontextprotocol/server-filesystem", "C:/Users"],
		npmPackage: "@modelcontextprotocol/server-filesystem",
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem",
	},
	{
		id: "mcp-fetch",
		name: "网页抓取与正文提取 (Fetch)",
		description: "抓取任意网络 URL 内容，并将 HTML 自动转换为干净的 Markdown 格式供 AI 阅读",
		author: "Anthropic / MCP Official",
		category: "web",
		icon: "sparkles",
		transport: "stdio",
		defaultCommand: "uvx",
		defaultArgs: ["mcp-server-fetch"],
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/fetch",
	},
	{
		id: "mcp-github",
		name: "GitHub 代码与仓库管理 (GitHub)",
		description: "检索 GitHub 仓库代码、搜索提交记录、查看并创建 Issue 与 Pull Request",
		author: "Anthropic / MCP Official",
		category: "developer",
		icon: "code",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "@modelcontextprotocol/server-github"],
		npmPackage: "@modelcontextprotocol/server-github",
		requiredEnv: [
			{
				key: "GITHUB_PERSONAL_ACCESS_TOKEN",
				description: "GitHub Personal Access Token (PAT)",
				placeholder: "ghp_xxxxxxxxxxxxxxxxxxxx",
			},
		],
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/github",
	},
	{
		id: "mcp-sqlite",
		name: "SQLite 数据库管理器 (SQLite)",
		description: "连接本地 SQLite 数据库，查看表 Schema 结构、执行 SELECT 只读查询或更新操作",
		author: "Anthropic / MCP Official",
		category: "database",
		icon: "server",
		transport: "stdio",
		defaultCommand: "uvx",
		defaultArgs: ["mcp-server-sqlite", "--db-path", "data.db"],
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/sqlite",
	},
	{
		id: "mcp-brave-search",
		name: "Brave 实时搜索引擎 (Brave Search)",
		description: "使用 Brave 独立搜索索引获取最新网络资讯、网页链接、新闻与知识百科",
		author: "Anthropic / MCP Official",
		category: "web",
		icon: "sparkles",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "@modelcontextprotocol/server-brave-search"],
		npmPackage: "@modelcontextprotocol/server-brave-search",
		requiredEnv: [
			{
				key: "BRAVE_API_KEY",
				description: "Brave Search API Key",
				placeholder: "BSA...",
			},
		],
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/brave-search",
	},
	{
		id: "mcp-git",
		name: "Git 本地版本控制 (Git)",
		description: "读取本地 Git 仓库的分支状态、Commit 日志、暂存区变更与 Diff",
		author: "MCP Community",
		category: "developer",
		icon: "terminal",
		transport: "stdio",
		defaultCommand: "uvx",
		defaultArgs: ["mcp-server-git", "--repository", "."],
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/git",
	},
	{
		id: "mcp-puppeteer",
		name: "Puppeteer 浏览器自动化",
		description: "驱动无头 Chrome 浏览器执行页面渲染、页面截图与动态 SPA 内容抓取",
		author: "Anthropic / MCP Official",
		category: "web",
		icon: "noriOS",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "@modelcontextprotocol/server-puppeteer"],
		npmPackage: "@modelcontextprotocol/server-puppeteer",
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/puppeteer",
	},
	{
		id: "mcp-memory",
		name: "知识图谱持久记忆 (Memory Graph)",
		description: "基于实体节点与关联关系的结构化知识图谱，长期沉淀用户背景与认知",
		author: "Anthropic / MCP Official",
		category: "system",
		icon: "package",
		transport: "stdio",
		defaultCommand: "npx",
		defaultArgs: ["-y", "@modelcontextprotocol/server-memory"],
		npmPackage: "@modelcontextprotocol/server-memory",
		readmeUrl: "https://github.com/modelcontextprotocol/servers/tree/main/src/memory",
	},
	{
		id: "mcp-docker",
		name: "Docker 容器与镜像管理 (Docker)",
		description: "检查本地 Docker 守护进程，列出容器、查看服务运行状态与容器日志",
		author: "MCP Community",
		category: "developer",
		icon: "server",
		transport: "stdio",
		defaultCommand: "uvx",
		defaultArgs: ["mcp-server-docker"],
		readmeUrl: "https://github.com/modelcontextprotocol/servers",
	},
]
