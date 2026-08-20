<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import Icon from "../Icon.vue"
import {
	mcpService,
	type McpServerConfig,
	type McpServerStatusInfo,
	type McpToolDefinition,
	type McpMarketplaceItem,
} from "../../services/mcp"
import {toolManager, type AgentTool} from "../../services/agent/tools"

// 视图子标签: "servers" | "market" | "builtin"
const subTab = ref<"servers" | "market" | "builtin">("servers")

// 服务器列表与加载状态
const servers = ref<McpServerStatusInfo[]>([])
const loading = ref(false)
const testing = ref(false)
const testResult = ref<McpServerStatusInfo | null>(null)

// 市场搜索与分类
const marketSearch = ref("")
const selectedCategory = ref<string>("all")

// 弹窗状态
const isModalOpen = ref(false)
const isEditing = ref(false)

// 表单数据
const form = ref<McpServerConfig>({
	id: "",
	name: "",
	transport: "stdio",
	command: "npx",
	args: ["-y", "@modelcontextprotocol/server-filesystem", "C:/"],
	env: {},
	url: "http://localhost:3000/sse",
	enabled: true,
	autoConnect: true,
})
const argsInput = ref("")
const envInput = ref("")

// URL / JSON 导入弹窗
const isImportModalOpen = ref(false)
const importUrl = ref("")
const isImporting = ref(false)
const importError = ref("")

// 工具调用测试弹窗
const isTestToolModalOpen = ref(false)
const activeTestTool = ref<{serverId?: string; tool: McpToolDefinition | AgentTool} | null>(null)
const toolArgsInput = ref("{}")
const toolExecuting = ref(false)
const toolExecOutput = ref<string>("")

// 市场安装引导弹窗
const isMarketInstallModalOpen = ref(false)
const targetMarketItem = ref<McpMarketplaceItem | null>(null)
const marketCustomArgs = ref("")
const marketEnvForm = ref<Record<string, string>>({})

// 内置工具列表
const builtinTools = ref<AgentTool[]>([])

// 市场分类列表
const MARKET_CATEGORIES: {key: string; label: string}[] = [
	{key: "all", label: "全部扩展"},
	{key: "system", label: "系统与存储"},
	{key: "web", label: "网络与搜索"},
	{key: "developer", label: "开发与 Git"},
	{key: "database", label: "数据库"},
]

// 市场条目
const marketplaceList = computed(() => mcpService.getMarketplace())

// 过滤后的市场列表
const filteredMarketplace = computed(() => {
	return marketplaceList.value.filter(item => {
		const matchCat = selectedCategory.value === "all" || item.category === selectedCategory.value
		const matchSearch = !marketSearch.value.trim() ||
			item.name.toLowerCase().includes(marketSearch.value.toLowerCase()) ||
			item.description.toLowerCase().includes(marketSearch.value.toLowerCase()) ||
			item.author.toLowerCase().includes(marketSearch.value.toLowerCase())
		return matchCat && matchSearch
	})
})

// 已配置的服务器 ID 列表（用于市场中判断是否已安装）
const installedServerNames = computed(() => new Set(servers.value.map(s => s.name)))

// 刷新列表
const refresh = async () => {
	loading.value = true
	try {
		servers.value = await mcpService.getServers()
		builtinTools.value = toolManager.list().filter(t => !t.name.startsWith("mcp__"))
	} finally {
		loading.value = false
	}
}

onMounted(() => {
	void refresh()
})

// 打开添加模态框
const openAddModal = () => {
	form.value = {
		id: `mcp_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
		name: "新 MCP 服务",
		transport: "stdio",
		command: "npx",
		args: ["-y", "@modelcontextprotocol/server-filesystem", "C:/"],
		enabled: true,
		autoConnect: true,
	}
	argsInput.value = form.value.args?.join(" ") || ""
	envInput.value = ""
	testResult.value = null
	isEditing.value = false
	isModalOpen.value = true
}

// 打开市场安装引导
const openMarketInstallModal = (item: McpMarketplaceItem) => {
	targetMarketItem.value = item
	marketCustomArgs.value = item.defaultArgs.join(" ")
	marketEnvForm.value = {}
	if (item.requiredEnv) {
		for (const envReq of item.requiredEnv) {
			marketEnvForm.value[envReq.key] = ""
		}
	}
	isMarketInstallModalOpen.value = true
}

// 执行市场安装
const executeMarketInstall = async () => {
	if (!targetMarketItem.value) return
	loading.value = true
	try {
		const customArgs = marketCustomArgs.value.split(/\s+/).map(s => s.trim()).filter(Boolean)
		await mcpService.installFromMarketplace(targetMarketItem.value, customArgs, marketEnvForm.value)
		isMarketInstallModalOpen.value = false
		subTab.value = "servers"
		await refresh()
	} catch (error) {
		alert(`安装失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		loading.value = false
	}
}

// 打开 URL 导入模态框
const openImportModal = () => {
	importUrl.value = ""
	importError.value = ""
	isImportModalOpen.value = true
}

// 执行 URL 导入
const executeImport = async () => {
	if (!importUrl.value.trim()) return
	isImporting.value = true
	importError.value = ""

	try {
		await mcpService.installFromUrl(importUrl.value.trim())
		isImportModalOpen.value = false
		subTab.value = "servers"
		await refresh()
	} catch (error) {
		importError.value = error instanceof Error ? error.message : String(error)
	} finally {
		isImporting.value = false
	}
}

// 保存服务器配置
const saveServer = async () => {
	if (!form.value.name.trim()) return

	if (form.value.transport === "stdio") {
		form.value.args = argsInput.value
			.split(/\s+/)
			.map(s => s.trim())
			.filter(Boolean)
	}

	if (envInput.value.trim()) {
		const envMap: Record<string, string> = {}
		const lines = envInput.value.split(/\r?\n/)
		for (const line of lines) {
			const idx = line.indexOf("=")
			if (idx > 0) {
				const k = line.substring(0, idx).trim()
				const v = line.substring(idx + 1).trim()
				if (k) envMap[k] = v
			}
		}
		form.value.env = envMap
	} else {
		form.value.env = undefined
	}

	loading.value = true
	try {
		await mcpService.saveServer(form.value)
		isModalOpen.value = false
		await refresh()
	} finally {
		loading.value = false
	}
}

// 测试连接
const testConnection = async () => {
	testing.value = true
	testResult.value = null

	const testConfig: McpServerConfig = {
		...form.value,
		args: argsInput.value.split(/\s+/).map(s => s.trim()).filter(Boolean),
	}

	try {
		testResult.value = await mcpService.testServer(testConfig)
	} catch (err) {
		testResult.value = {
			serverId: testConfig.id,
			name: testConfig.name,
			status: "error",
			errorMessage: String(err),
			tools: [],
			resources: [],
		}
	} finally {
		testing.value = false
	}
}

// 连接服务器
const connectServer = async (id: string) => {
	loading.value = true
	try {
		await mcpService.connectServer(id)
		await refresh()
	} finally {
		loading.value = false
	}
}

// 断开服务器
const disconnectServer = async (id: string) => {
	loading.value = true
	try {
		await mcpService.disconnectServer(id)
		await refresh()
	} finally {
		loading.value = false
	}
}

// 删除服务器
const deleteServer = async (id: string) => {
	if (!confirm("确定要删除该 MCP 服务器配置吗？")) return
	loading.value = true
	try {
		await mcpService.deleteServer(id)
		await refresh()
	} finally {
		loading.value = false
	}
}

// 打开工具执行测试
const openToolTester = (tool: McpToolDefinition | AgentTool, serverId?: string) => {
	activeTestTool.value = {serverId, tool}
	const schema = (tool as any).parameters || (tool as any).inputSchema
	const sampleArgs: Record<string, unknown> = {}
	if (schema?.properties) {
		for (const [key, val] of Object.entries(schema.properties as Record<string, any>)) {
			sampleArgs[key] = val.type === "number" ? 1 : (val.type === "boolean" ? true : "")
		}
	}
	toolArgsInput.value = JSON.stringify(sampleArgs, null, 2)
	toolExecOutput.value = ""
	isTestToolModalOpen.value = true
}

// 执行工具测试
const executeToolTest = async () => {
	if (!activeTestTool.value) return
	toolExecuting.value = true
	toolExecOutput.value = ""

	try {
		let parsedArgs = {}
		try {
			parsedArgs = JSON.parse(toolArgsInput.value)
		} catch {
			throw new Error("参数格式错误，必须为合法 JSON 对象")
		}

		if (activeTestTool.value.serverId) {
			const res = await mcpService.callTool(activeTestTool.value.serverId, activeTestTool.value.tool.name, parsedArgs)
			toolExecOutput.value = JSON.stringify(res, null, 2)
		} else {
			const res = await toolManager.execute(activeTestTool.value.tool.name, parsedArgs)
			toolExecOutput.value = JSON.stringify(res, null, 2)
		}
	} catch (error) {
		toolExecOutput.value = `执行出错: ${error instanceof Error ? error.message : String(error)}`
	} finally {
		toolExecuting.value = false
	}
}

// 切换内置工具启用状态
const toggleBuiltinTool = (tool: AgentTool) => {
	const nextState = tool.enabled === false
	toolManager.setEnabled(tool.name, nextState)
	builtinTools.value = toolManager.list().filter(t => !t.name.startsWith("mcp__"))
}
</script>

<template>
	<div class="mcp-settings-view">
		<!-- 头部操作与分类切换 -->
		<div class="mcp-header">
			<div class="mcp-subtabs">
				<button
					class="subtab-btn"
					:class="{active: subTab === 'servers'}"
					@click="subTab = 'servers'"
				>
					<Icon name="server" :size="14"/>
					<span>已配置服务 ({{ servers.length }})</span>
				</button>
				<button
					class="subtab-btn"
					:class="{active: subTab === 'market'}"
					@click="subTab = 'market'"
				>
					<Icon name="package" :size="14"/>
					<span>MCP 扩展市场 (Hub)</span>
				</button>
				<button
					class="subtab-btn"
					:class="{active: subTab === 'builtin'}"
					@click="subTab = 'builtin'"
				>
					<Icon name="tool" :size="14"/>
					<span>内置工具 ({{ builtinTools.length }})</span>
				</button>
			</div>

			<div class="mcp-actions">
				<button class="btn-icon-action" title="刷新状态" :disabled="loading" @click="refresh">
					<Icon name="refresh" :class="{spin: loading}" :size="14"/>
				</button>
				<button class="btn-import-url" @click="openImportModal">
					<Icon name="external-link" :size="13"/>
					<span>从 URL 导入</span>
				</button>
				<button v-if="subTab !== 'market'" class="btn-add-server" @click="openAddModal">
					<Icon name="plus" :size="14"/>
					<span>添加服务</span>
				</button>
			</div>
		</div>

		<!-- 主内容滚动区 -->
		<div class="mcp-body">
			<!-- 1. MCP 服务器面板 -->
			<div v-if="subTab === 'servers'" class="servers-section">
				<!-- 空状态 -->
				<div v-if="servers.length === 0 && !loading" class="empty-state">
					<Icon name="plug" class="empty-icon" :size="36"/>
					<p class="empty-title">尚未添加任何 MCP 服务器</p>
					<p class="empty-desc">
						通过 Model Context Protocol，Nori 可以连接外部文件系统、网络检索、代码运行等专业工具服务器。
					</p>
					<div class="empty-actions">
						<button class="btn-primary" @click="subTab = 'market'">前往 MCP 市场浏览预设</button>
						<button class="btn-outline" @click="openAddModal">手动添加自定义服务</button>
					</div>
				</div>

				<!-- 服务器卡片列表 -->
				<div v-else class="servers-list">
					<div
						v-for="srv in servers"
						:key="srv.serverId"
						class="server-card"
						:class="srv.status"
					>
						<div class="server-card-header">
							<div class="server-info">
								<div class="server-status-dot" :class="srv.status" :title="srv.status"/>
								<h3 class="server-name">{{ srv.name }}</h3>
								<span class="server-id">#{{ srv.serverId }}</span>
								<span class="status-badge" :class="srv.status">
									{{ srv.status === 'connected' ? '已连接' : (srv.status === 'connecting' ? '连接中...' : (srv.status === 'error' ? '异常' : '未连接')) }}
								</span>
							</div>

							<div class="server-ops">
								<button
									v-if="srv.status === 'connected'"
									class="btn-sm btn-warn"
									:disabled="loading"
									@click="disconnectServer(srv.serverId)"
								>
									断开
								</button>
								<button
									v-else
									class="btn-sm btn-connect"
									:disabled="loading"
									@click="connectServer(srv.serverId)"
								>
									连接
								</button>
								<button class="btn-sm btn-danger" @click="deleteServer(srv.serverId)">
									<Icon name="trash" :size="12"/>
								</button>
							</div>
						</div>

						<!-- 错误提示 -->
						<div v-if="srv.status === 'error' && srv.errorMessage" class="server-error-box">
							<Icon name="alert" :size="13"/>
							<span>{{ srv.errorMessage }}</span>
						</div>

						<!-- 工具列表 -->
						<div v-if="srv.tools.length > 0" class="server-tools-box">
							<div class="tools-header">
								<span class="tools-title">提供 {{ srv.tools.length }} 个可用工具:</span>
							</div>
							<div class="tool-tags">
								<div
									v-for="tool in srv.tools"
									:key="tool.name"
									class="tool-tag"
									@click="openToolTester(tool, srv.serverId)"
								>
									<span class="tool-tag-name">{{ tool.name }}</span>
									<span class="tool-tag-desc">{{ tool.description || '无说明' }}</span>
									<button class="btn-test-tool" title="测试调用此工具">
										<Icon name="play" :size="10"/>
									</button>
								</div>
							</div>
						</div>
					</div>
				</div>
			</div>

			<!-- 2. MCP 扩展市场 -->
			<div v-else-if="subTab === 'market'" class="market-section">
				<!-- 搜索与分类条 -->
				<div class="filter-bar">
					<input
						v-model="marketSearch"
						class="search-input"
						placeholder="搜索 MCP 扩展名称、作者或功能说明..."
					/>
					<div class="category-chips">
						<button
							v-for="cat in MARKET_CATEGORIES"
							:key="cat.key"
							class="cat-chip"
							:class="{active: selectedCategory === cat.key}"
							@click="selectedCategory = cat.key"
						>
							{{ cat.label }}
						</button>
					</div>
				</div>

				<div class="market-grid">
					<div
						v-for="item in filteredMarketplace"
						:key="item.id"
						class="market-card"
					>
						<div class="market-card-top">
							<div class="market-meta">
								<div class="market-icon-wrap">
									<Icon :name="item.icon" :size="18"/>
								</div>
								<div>
									<h4 class="market-name">{{ item.name }}</h4>
									<span class="market-author">by {{ item.author }}</span>
								</div>
							</div>

							<div>
								<span v-if="installedServerNames.has(item.name)" class="badge-installed">
									<Icon name="check" :size="11"/>
									<span>已配置</span>
								</span>
								<button
									v-else
									class="btn-install"
									:disabled="loading"
									@click="openMarketInstallModal(item)"
								>
									<Icon name="plus" :size="12"/>
									<span>一键安装</span>
								</button>
							</div>
						</div>

						<p class="market-desc">{{ item.description }}</p>

						<div class="market-card-bottom">
							<span class="market-cmd-tag">
								<Icon name="terminal" :size="10"/>
								{{ item.defaultCommand }} {{ item.defaultArgs[0] }}
							</span>
							<span v-if="item.requiredEnv && item.requiredEnv.length > 0" class="market-env-tag">
								需填入密钥
							</span>
						</div>
					</div>
				</div>
			</div>

			<!-- 3. 内置工具面板 -->
			<div v-else class="builtin-section">
				<p class="section-intro">Nori 内置了时间查询、天气、动作控制与记忆交互等原生工具，你可以按需开启或停用特定工具：</p>

				<div class="builtin-grid">
					<div
						v-for="tool in builtinTools"
						:key="tool.name"
						class="builtin-card"
						:class="{disabled: tool.enabled === false}"
					>
						<div class="builtin-card-top">
							<div class="tool-meta">
								<span class="tool-name">{{ tool.name }}</span>
								<span class="tool-perm-badge">{{ tool.permissionLevel }}</span>
							</div>
							<div class="builtin-card-ops">
								<button
									class="btn-test-mini"
									title="测试此工具"
									@click="openToolTester(tool)"
								>
									<Icon name="play" :size="11"/>
									<span>测试</span>
								</button>
								<button
									class="btn-toggle"
									:class="{active: tool.enabled !== false}"
									@click="toggleBuiltinTool(tool)"
								>
									{{ tool.enabled !== false ? '已启用' : '已停用' }}
								</button>
							</div>
						</div>
						<p class="builtin-desc">{{ tool.description }}</p>
					</div>
				</div>
			</div>
		</div>

		<!-- 市场安装引导弹窗 -->
		<div v-if="isMarketInstallModalOpen && targetMarketItem" class="modal-overlay" @click.self="isMarketInstallModalOpen = false">
			<div class="modal-card">
				<div class="modal-header">
					<h3>配置并安装: {{ targetMarketItem.name }}</h3>
					<button class="btn-close" @click="isMarketInstallModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<p class="modal-hint">{{ targetMarketItem.description }}</p>

					<div class="field-row">
						<label class="field-label">启动命令行参数</label>
						<input v-model="marketCustomArgs" class="input"/>
					</div>

					<!-- 环境变量输入 (若有要求) -->
					<template v-if="targetMarketItem.requiredEnv && targetMarketItem.requiredEnv.length > 0">
						<div
							v-for="envItem in targetMarketItem.requiredEnv"
							:key="envItem.key"
							class="field-row"
						>
							<label class="field-label">{{ envItem.description }} ({{ envItem.key }})</label>
							<input
								v-model="marketEnvForm[envItem.key]"
								type="password"
								class="input"
								:placeholder="envItem.placeholder"
							/>
						</div>
					</template>
				</div>

				<div class="modal-footer">
					<button class="btn-ghost" @click="isMarketInstallModalOpen = false">取消</button>
					<button class="btn-primary" :disabled="loading" @click="executeMarketInstall">
						完成并连接
					</button>
				</div>
			</div>
		</div>

		<!-- 从 URL 导入 MCP 配置弹窗 -->
		<div v-if="isImportModalOpen" class="modal-overlay" @click.self="isImportModalOpen = false">
			<div class="modal-card">
				<div class="modal-header">
					<h3>从网络 URL 导入 MCP 配置 (JSON)</h3>
					<button class="btn-close" @click="isImportModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<p class="modal-hint">
						支持直接粘贴在线托管的 <code>mcp.json</code> 链接或 Claude Desktop 配置文件 URL。
					</p>

					<div class="field-row">
						<label class="field-label">MCP 配置文件 URL 地址</label>
						<input
							v-model="importUrl"
							class="input"
							placeholder="https://raw.githubusercontent.com/.../mcp.json"
						/>
					</div>

					<div v-if="importError" class="error-box">
						<Icon name="alert" :size="13"/>
						<span>{{ importError }}</span>
					</div>
				</div>

				<div class="modal-footer">
					<button class="btn-ghost" @click="isImportModalOpen = false">取消</button>
					<button
						class="btn-primary"
						:disabled="isImporting || !importUrl.trim()"
						@click="executeImport"
					>
						<Icon v-if="isImporting" name="loading" class="spin" :size="13"/>
						<span>{{ isImporting ? '导入中...' : '开始导入' }}</span>
					</button>
				</div>
			</div>
		</div>

		<!-- 手动添加 / 编辑服务器弹窗 -->
		<div v-if="isModalOpen" class="modal-overlay" @click.self="isModalOpen = false">
			<div class="modal-card">
				<div class="modal-header">
					<h3>{{ isEditing ? '编辑 MCP 服务器' : '添加自定义 MCP 服务器' }}</h3>
					<button class="btn-close" @click="isModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<div class="field-row">
						<label class="field-label">服务名称</label>
						<input v-model="form.name" class="input" placeholder="例如: 本地文件系统 / 网络搜索"/>
					</div>

					<div class="field-row">
						<label class="field-label">传输协议 (Transport)</label>
						<div class="radio-group">
							<label class="radio-label">
								<input v-model="form.transport" type="radio" value="stdio"/>
								<span>Stdio (子进程命令行)</span>
							</label>
							<label class="radio-label">
								<input v-model="form.transport" type="radio" value="sse"/>
								<span>SSE (HTTP Server-Sent Events)</span>
							</label>
						</div>
					</div>

					<!-- Stdio 字段 -->
					<template v-if="form.transport === 'stdio'">
						<div class="field-row">
							<label class="field-label">执行程序 / 命令 (Command)</label>
							<input v-model="form.command" class="input" placeholder="例如: npx, python, node, uvx"/>
						</div>

						<div class="field-row">
							<label class="field-label">启动参数 (Arguments, 空格分隔)</label>
							<input v-model="argsInput" class="input" placeholder="例如: -y @modelcontextprotocol/server-filesystem C:/"/>
						</div>

						<div class="field-row">
							<label class="field-label">环境变量 (Environment Variables, 每行一条 KEY=VALUE)</label>
							<textarea v-model="envInput" class="textarea" rows="2" placeholder="API_KEY=xxx&#10;DEBUG=true"/>
						</div>
					</template>

					<!-- SSE 字段 -->
					<template v-else>
						<div class="field-row">
							<label class="field-label">SSE 端点地址 (URL)</label>
							<input v-model="form.url" class="input" placeholder="例如: http://localhost:3000/sse"/>
						</div>
					</template>

					<div class="field-checkbox-row">
						<label class="checkbox-label">
							<input v-model="form.autoConnect" type="checkbox"/>
							<span>应用启动时自动连接</span>
						</label>
						<label class="checkbox-label">
							<input v-model="form.enabled" type="checkbox"/>
							<span>启用此服务</span>
						</label>
					</div>

					<!-- 测试连接反馈 -->
					<div v-if="testResult" class="test-feedback-box" :class="testResult.status">
						<div class="feedback-title">
							<Icon :name="testResult.status === 'connected' ? 'check' : 'alert'" :size="14"/>
							<span>{{ testResult.status === 'connected' ? '连接成功！已发现以下工具：' : `连接失败: ${testResult.errorMessage}` }}</span>
						</div>
						<div v-if="testResult.tools.length > 0" class="feedback-tools">
							<span v-for="t in testResult.tools" :key="t.name" class="feedback-tool-badge">
								{{ t.name }}
							</span>
						</div>
					</div>
				</div>

				<div class="modal-footer">
					<button class="btn-outline" :disabled="testing" @click="testConnection">
						<Icon v-if="testing" name="loading" class="spin" :size="13"/>
						<span>{{ testing ? '测试中...' : '测试连接' }}</span>
					</button>
					<div class="footer-right">
						<button class="btn-ghost" @click="isModalOpen = false">取消</button>
						<button class="btn-primary" :disabled="loading || !form.name.trim()" @click="saveServer">
							保存配置
						</button>
					</div>
				</div>
			</div>
		</div>

		<!-- 工具调用测试弹窗 -->
		<div v-if="isTestToolModalOpen && activeTestTool" class="modal-overlay" @click.self="isTestToolModalOpen = false">
			<div class="modal-card">
				<div class="modal-header">
					<h3>测试工具: {{ activeTestTool.tool.name }}</h3>
					<button class="btn-close" @click="isTestToolModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<p class="tool-test-desc">{{ activeTestTool.tool.description }}</p>

					<div class="field-row">
						<label class="field-label">输入参数 (JSON 格式)</label>
						<textarea v-model="toolArgsInput" class="textarea code-font" rows="5"/>
					</div>

					<div v-if="toolExecOutput" class="field-row">
						<label class="field-label">执行输出结果</label>
						<pre class="output-pre">{{ toolExecOutput }}</pre>
					</div>
				</div>

				<div class="modal-footer">
					<button class="btn-ghost" @click="isTestToolModalOpen = false">关闭</button>
					<button class="btn-primary" :disabled="toolExecuting" @click="executeToolTest">
						<Icon v-if="toolExecuting" name="loading" class="spin" :size="13"/>
						<Icon v-else name="play" :size="13"/>
						<span>{{ toolExecuting ? '执行中...' : '运行测试' }}</span>
					</button>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.mcp-settings-view {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	min-height: 0;
	padding: 1.6rem 2rem;
	overflow: hidden;
}

.mcp-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	margin-bottom: 1.4rem;
	flex-shrink: 0;
}

.mcp-subtabs {
	display: flex;
	gap: 0.8rem;
}

.subtab-btn {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	font-size: 1.2rem;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: var(--text-primary);
		background: rgba(255, 255, 255, 0.08);
	}

	&.active {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		font-weight: 600;
	}
}

.mcp-actions {
	display: flex;
	align-items: center;
	gap: 0.8rem;
}

.btn-icon-action {
	width: 3.2rem;
	height: 3.2rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}
}

.btn-import-url {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-body);
	font-size: 1.15rem;
	cursor: pointer;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}
}

.btn-add-server {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.4rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	font-size: 1.2rem;
	font-weight: 600;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.mcp-body {
	flex: 1;
	min-height: 0;
	overflow-y: auto;
	padding-right: 0.4rem;
}

// 市场网格
.filter-bar {
	display: flex;
	flex-direction: column;
	gap: 0.8rem;
	margin-bottom: 1.4rem;
	flex-shrink: 0;
}

.search-input {
	width: 100%;
	padding: 0.7rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-primary);
	font-size: 1.2rem;
	outline: none;

	&:focus {
		border-color: var(--nori-teal-soft);
	}
}

.category-chips {
	display: flex;
	flex-wrap: wrap;
	gap: 0.6rem;
}

.cat-chip {
	padding: 0.3rem 0.9rem;
	border-radius: 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-muted);
	font-size: 1.1rem;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: var(--text-primary);
		background: rgba(255, 255, 255, 0.06);
	}

	&.active {
		border-color: var(--nori-teal-soft);
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.1);
		font-weight: 500;
	}
}

.market-grid {
	display: grid;
	grid-template-columns: repeat(auto-fill, minmax(32rem, 1fr));
	gap: 1.2rem;
}

.market-card {
	padding: 1.4rem 1.6rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	display: flex;
	flex-direction: column;
	gap: 1rem;
	transition: all 0.2s ease;

	&:hover {
		border-color: rgba(125, 227, 255, 0.3);
	}
}

.market-card-top {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
}

.market-meta {
	display: flex;
	gap: 1rem;
	align-items: center;
}

.market-icon-wrap {
	width: 3.6rem;
	height: 3.6rem;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal-bright);
	display: flex;
	align-items: center;
	justify-content: center;
	flex-shrink: 0;
}

.market-name {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.market-author {
	font-size: 1.1rem;
	color: var(--text-faint);
	display: block;
	margin-top: 0.2rem;
}

.badge-installed {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	font-size: 1.1rem;
	padding: 0.3rem 0.8rem;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.1);
	color: var(--nori-teal-bright);
}

.btn-install {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	padding: 0.4rem 1.1rem;
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.08);
	color: var(--nori-teal-bright);
	font-size: 1.15rem;
	cursor: pointer;

	&:hover {
		background: rgba(125, 227, 255, 0.18);
	}
}

.market-desc {
	font-size: 1.18rem;
	color: var(--text-muted);
	line-height: 1.5;
}

.market-card-bottom {
	display: flex;
	align-items: center;
	justify-content: space-between;
	border-top: 0.1rem solid var(--line-subtle);
	padding-top: 0.8rem;
}

.market-cmd-tag {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	font-size: 1.05rem;
	color: var(--text-faint);
	font-family: monospace;
}

.market-env-tag {
	font-size: 1rem;
	padding: 0.1rem 0.5rem;
	border-radius: 0.3rem;
	background: rgba(255, 184, 48, 0.15);
	color: #ffb830;
}

// 空状态
.empty-state {
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	padding: 4rem 2rem;
	text-align: center;
	gap: 1.2rem;
}

.empty-icon {
	color: var(--text-faint);
	opacity: 0.5;
}

.empty-title {
	font-size: 1.6rem;
	color: var(--text-primary);
	font-weight: 600;
}

.empty-desc {
	font-size: 1.2rem;
	color: var(--text-muted);
	max-width: 44rem;
	line-height: 1.6;
}

.empty-actions {
	display: flex;
	gap: 1rem;
	margin-top: 0.6rem;
}

// 服务器卡片
.servers-list {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.server-card {
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	padding: 1.4rem 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1rem;
	transition: all 0.2s ease;

	&.connected {
		border-color: rgba(125, 227, 255, 0.3);
		background: rgba(125, 227, 255, 0.02);
	}

	&.error {
		border-color: rgba(255, 75, 75, 0.3);
	}
}

.server-card-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
}

.server-info {
	display: flex;
	align-items: center;
	gap: 0.8rem;
}

.server-status-dot {
	width: 0.8rem;
	height: 0.8rem;
	border-radius: 50%;
	background: var(--text-faint);

	&.connected {
		background: var(--nori-teal-bright);
		box-shadow: 0 0 0.8rem var(--nori-teal-bright);
	}

	&.connecting {
		background: #ffb830;
	}

	&.error {
		background: var(--danger);
	}
}

.server-name {
	font-size: 1.4rem;
	font-weight: 600;
	color: var(--text-primary);
}

.server-id {
	font-size: 1.1rem;
	color: var(--text-faint);
	font-family: monospace;
}

.status-badge {
	font-size: 1rem;
	padding: 0.2rem 0.6rem;
	border-radius: 0.4rem;
	background: rgba(255, 255, 255, 0.06);
	color: var(--text-muted);

	&.connected {
		background: rgba(125, 227, 255, 0.15);
		color: var(--nori-teal-bright);
	}

	&.connecting {
		background: rgba(255, 184, 48, 0.15);
		color: #ffb830;
	}

	&.error {
		background: rgba(255, 75, 75, 0.15);
		color: var(--danger);
	}
}

.server-ops {
	display: flex;
	gap: 0.6rem;
}

.btn-sm {
	padding: 0.4rem 1rem;
	border-radius: var(--radius-sm);
	font-size: 1.15rem;
	cursor: pointer;
	border: 0.1rem solid var(--line-subtle);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	transition: all 0.2s ease;

	&.btn-connect {
		border-color: var(--nori-teal-soft);
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.08);

		&:hover {
			background: rgba(125, 227, 255, 0.18);
		}
	}

	&.btn-warn:hover {
		color: #ffb830;
		border-color: #ffb830;
	}

	&.btn-danger:hover {
		color: var(--danger);
		border-color: var(--danger);
	}
}

.server-error-box {
	display: flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1rem;
	background: rgba(255, 75, 75, 0.08);
	border: 0.1rem solid rgba(255, 75, 75, 0.2);
	border-radius: var(--radius-sm);
	color: var(--danger);
	font-size: 1.15rem;
}

.server-tools-box {
	border-top: 0.1rem solid var(--line-subtle);
	padding-top: 0.8rem;
}

.tools-title {
	font-size: 1.15rem;
	color: var(--text-faint);
	margin-bottom: 0.6rem;
	display: block;
}

.tool-tags {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
}

.tool-tag {
	display: flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.5rem 0.9rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.06);

		.btn-test-tool {
			color: var(--nori-teal-bright);
		}
	}
}

.tool-tag-name {
	font-size: 1.15rem;
	color: var(--nori-teal-bright);
	font-weight: 500;
}

.tool-tag-desc {
	font-size: 1.05rem;
	color: var(--text-faint);
	max-width: 18rem;
	overflow: hidden;
	text-overflow: ellipsis;
	white-space: nowrap;
}

.btn-test-tool {
	border: none;
	background: transparent;
	color: var(--text-faint);
	cursor: pointer;
	padding: 0.2rem;
	display: flex;
	align-items: center;
}

// 内置工具面板
.builtin-section {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.section-intro {
	font-size: 1.2rem;
	color: var(--text-muted);
	line-height: 1.6;
}

.builtin-grid {
	display: grid;
	grid-template-columns: repeat(auto-fill, minmax(28rem, 1fr));
	gap: 1rem;
}

.builtin-card {
	padding: 1.2rem 1.4rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	display: flex;
	flex-direction: column;
	gap: 0.8rem;
	transition: all 0.2s ease;

	&.disabled {
		opacity: 0.6;
		background: rgba(0, 0, 0, 0.2);
	}
}

.builtin-card-top {
	display: flex;
	align-items: center;
	justify-content: space-between;
}

.tool-meta {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.tool-name {
	font-size: 1.3rem;
	font-weight: 600;
	color: var(--text-primary);
}

.tool-perm-badge {
	font-size: 0.95rem;
	padding: 0.1rem 0.5rem;
	border-radius: 0.3rem;
	background: rgba(125, 227, 255, 0.08);
	color: var(--nori-teal-bright);
}

.builtin-card-ops {
	display: flex;
	gap: 0.6rem;
}

.btn-test-mini {
	display: inline-flex;
	align-items: center;
	gap: 0.3rem;
	padding: 0.3rem 0.6rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-size: 1.05rem;
	cursor: pointer;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}
}

.btn-toggle {
	padding: 0.3rem 0.8rem;
	border-radius: var(--radius-sm);
	font-size: 1.05rem;
	cursor: pointer;
	border: 0.1rem solid var(--line-subtle);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-faint);

	&.active {
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.1);
		color: var(--nori-teal-bright);
	}
}

.builtin-desc {
	font-size: 1.15rem;
	color: var(--text-muted);
	line-height: 1.5;
}

// 弹窗
.modal-overlay {
	position: fixed;
	inset: 0;
	background: rgba(0, 0, 0, 0.65);
	backdrop-filter: blur(0.4rem);
	display: flex;
	align-items: center;
	justify-content: center;
	z-index: 100;
	padding: 2rem;
}

.modal-card {
	width: 100%;
	max-width: 52rem;
	max-height: 90vh;
	background: #091a26;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	display: flex;
	flex-direction: column;
	box-shadow: 0 1rem 3rem rgba(0, 0, 0, 0.5);
	overflow: hidden;
}

.modal-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1.6rem 2rem;
	border-bottom: 0.1rem solid var(--line-subtle);

	h3 {
		font-size: 1.5rem;
		color: var(--text-primary);
		font-weight: 600;
	}
}

.btn-close {
	background: transparent;
	border: none;
	color: var(--text-faint);
	cursor: pointer;

	&:hover {
		color: var(--text-primary);
	}
}

.modal-body {
	padding: 1.6rem 2rem;
	overflow-y: auto;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.modal-hint {
	font-size: 1.18rem;
	color: var(--text-muted);
	line-height: 1.5;

	code {
		background: rgba(0, 0, 0, 0.3);
		padding: 0.2rem 0.5rem;
		border-radius: 0.3rem;
		color: var(--nori-teal-bright);
	}
}

.field-row {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.field-label {
	font-size: 1.2rem;
	color: var(--text-muted);
	font-weight: 500;
}

.input, .textarea {
	padding: 0.8rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 1.2rem;
	font-family: inherit;
	outline: none;

	&:focus {
		border-color: var(--nori-teal-soft);
	}
}

.code-font {
	font-family: monospace;
	line-height: 1.4;
}

.radio-group {
	display: flex;
	gap: 1.6rem;
}

.radio-label, .checkbox-label {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	font-size: 1.2rem;
	color: var(--text-body);
	cursor: pointer;
}

.field-checkbox-row {
	display: flex;
	gap: 2rem;
	margin-top: 0.4rem;
}

.test-feedback-box {
	padding: 1rem 1.2rem;
	border-radius: var(--radius-sm);
	border: 0.1rem solid var(--line-subtle);
	font-size: 1.15rem;

	&.connected {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		color: var(--nori-teal-bright);
	}

	&.error {
		background: rgba(255, 75, 75, 0.08);
		border-color: var(--danger);
		color: var(--danger);
	}
}

.feedback-title {
	display: flex;
	align-items: center;
	gap: 0.6rem;
	font-weight: 500;
}

.feedback-tools {
	display: flex;
	flex-wrap: wrap;
	gap: 0.4rem;
	margin-top: 0.6rem;
}

.feedback-tool-badge {
	padding: 0.2rem 0.6rem;
	background: rgba(0, 0, 0, 0.25);
	border-radius: 0.3rem;
	font-size: 1.05rem;
}

.error-box {
	display: flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1rem;
	background: rgba(255, 75, 75, 0.08);
	border: 0.1rem solid rgba(255, 75, 75, 0.2);
	border-radius: var(--radius-sm);
	color: var(--danger);
	font-size: 1.15rem;
}

.output-pre {
	padding: 1rem;
	background: rgba(0, 0, 0, 0.3);
	border-radius: var(--radius-sm);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal-bright);
	font-size: 1.1rem;
	font-family: monospace;
	max-height: 16rem;
	overflow-y: auto;
	white-space: pre-wrap;
	word-break: break-all;
}

.tool-test-desc {
	font-size: 1.2rem;
	color: var(--text-muted);
	line-height: 1.5;
}

.modal-footer {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1.4rem 2rem;
	border-top: 0.1rem solid var(--line-subtle);
	background: rgba(0, 0, 0, 0.15);
}

.footer-right {
	display: flex;
	gap: 0.8rem;
}

.btn-primary {
	padding: 0.7rem 1.6rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	font-size: 1.2rem;
	font-weight: 600;
	cursor: pointer;

	&:disabled {
		opacity: 0.5;
	}
}

.btn-outline {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-body);
	font-size: 1.2rem;
	cursor: pointer;

	&:hover {
		border-color: var(--nori-teal-soft);
		color: var(--nori-teal-bright);
	}
}

.btn-ghost {
	padding: 0.6rem 1.2rem;
	border: none;
	background: transparent;
	color: var(--text-muted);
	font-size: 1.2rem;
	cursor: pointer;

	&:hover {
		color: var(--text-primary);
	}
}

.spin {
	animation: spin 1s linear infinite;
}

@keyframes spin {
	from { transform: rotate(0deg); }
	to { transform: rotate(360deg); }
}
</style>
