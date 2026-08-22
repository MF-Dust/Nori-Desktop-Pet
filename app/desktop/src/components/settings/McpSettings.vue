<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {useMessage} from "naive-ui"
import {RUNTIME, type McpServerStatusInfo, type ToolDto} from "../../services/runtime"
import Icon from "../Icon.vue"

const MESSAGE = useMessage()
const SUB_TAB = ref<"servers" | "builtin">("servers")
const SERVERS = ref<McpServerStatusInfo[]>([])
const LOADING = ref(false)
const TESTING = ref(false)
const TEST_RESULT = ref<McpServerStatusInfo | null>(null)
const SEARCH = ref("")

const MODAL_OPEN = ref(false)
const EDITING = ref(false)
const FORM = ref({
	id: "",
	name: "",
	transport: "stdio" as "stdio" | "sse",
	command: "npx",
	args: [] as string[],
	env: {} as Record<string, string>,
	url: "",
	enabled: true,
	autoConnect: true,
})
const ARGS_INPUT = ref("")
const ENV_INPUT = ref("")

const IMPORT_OPEN = ref(false)
const IMPORT_URL = ref("")
const IMPORTING = ref(false)
const IMPORT_ERROR = ref("")

const TOOL_MODAL_OPEN = ref(false)
const ACTIVE_TOOL = ref<{serverId?: string; name: string; description: string; inputSchema?: Record<string, unknown>} | null>(null)
const TOOL_ARGS = ref("{}")
const TOOL_RUNNING = ref(false)
const TOOL_OUTPUT = ref("")

const BUILTIN_TOOLS = computed<ToolDto[]>(() =>
	(RUNTIME.snapshot.value?.tools ?? []).filter(tool => tool.category === "builtin")
)
const FILTERED_TOOLS = computed(() => {
	const QUERY = SEARCH.value.trim().toLowerCase()
	return !QUERY ? BUILTIN_TOOLS.value : BUILTIN_TOOLS.value.filter(tool =>
		tool.name.toLowerCase().includes(QUERY) || tool.description.toLowerCase().includes(QUERY)
	)
})

const refresh = async () => {
	LOADING.value = true
	try {
		SERVERS.value = await RUNTIME.mcpGetServers()
		await RUNTIME.refresh()
	} catch (error) {
		MESSAGE.error(`读取 MCP 状态失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		LOADING.value = false
	}
}

onMounted(async () => {
	await RUNTIME.init()
	await refresh()
})

const newForm = () => {
	FORM.value = {
		id: `mcp_${Date.now().toString(36)}`,
		name: "新 MCP 服务",
		transport: "stdio",
		command: "npx",
		args: ["-y", "@modelcontextprotocol/server-filesystem", "C:/"],
		env: {},
		url: "",
		enabled: true,
		autoConnect: true,
	}
	ARGS_INPUT.value = FORM.value.args.join(" ")
	ENV_INPUT.value = ""
	TEST_RESULT.value = null
	EDITING.value = false
	MODAL_OPEN.value = true
}

const openImport = () => {
	IMPORT_URL.value = ""
	IMPORT_ERROR.value = ""
	IMPORT_OPEN.value = true
}

const parseEnv = (raw: string): Record<string, string> => {
	const RESULT: Record<string, string> = {}
	for (const LINE of raw.split(/\r?\n/)) {
		const INDEX = LINE.indexOf("=")
		if (INDEX > 0) RESULT[LINE.slice(0, INDEX).trim()] = LINE.slice(INDEX + 1).trim()
	}
	return RESULT
}

const formPayload = () => ({
	...FORM.value,
	args: FORM.value.transport === "stdio" ? ARGS_INPUT.value.split(/\s+/).filter(Boolean) : [],
	env: parseEnv(ENV_INPUT.value),
	url: FORM.value.transport === "sse" ? FORM.value.url.trim() : undefined,
})

const saveServer = async () => {
	if (!FORM.value.name.trim()) return
	LOADING.value = true
	try {
		await RUNTIME.mcpSaveServer(formPayload())
		MODAL_OPEN.value = false
		await refresh()
		MESSAGE.success(EDITING.value ? "MCP 服务器已更新" : "已添加 MCP 服务器")
	} catch (error) {
		MESSAGE.error(`保存失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		LOADING.value = false
	}
}

const testServer = async () => {
	TESTING.value = true
	TEST_RESULT.value = null
	try {
		TEST_RESULT.value = await RUNTIME.mcpTestServer(formPayload())
		if (TEST_RESULT.value.status === "connected") MESSAGE.success("MCP 服务连接测试成功")
		else MESSAGE.warning(TEST_RESULT.value.errorMessage || "MCP 服务未就绪")
	} catch (error) {
		MESSAGE.error(`连接测试失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		TESTING.value = false
	}
}

const connect = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpConnect(id)
		await refresh()
		MESSAGE.success("服务器已连接")
	} catch (error) {
		MESSAGE.error(`连接失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		LOADING.value = false
	}
}

const disconnect = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpDisconnect(id)
		await refresh()
		MESSAGE.info("服务器连接已断开")
	} catch (error) {
		MESSAGE.error(`断开失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		LOADING.value = false
	}
}

const remove = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpDeleteServer(id)
		await refresh()
		MESSAGE.success("已删除 MCP 服务器配置")
	} catch (error) {
		MESSAGE.error(`删除失败: ${error instanceof Error ? error.message : String(error)}`)
	} finally {
		LOADING.value = false
	}
}

const openTool = (tool: ToolDto | {name: string; description?: string; inputSchema?: Record<string, unknown>}, serverId?: string) => {
	ACTIVE_TOOL.value = {
		serverId,
		name: tool.name,
		description: tool.description || "",
		inputSchema: "inputSchema" in tool ? tool.inputSchema : undefined,
	}
	const SAMPLE: Record<string, unknown> = {}
	for (const [KEY, VALUE] of Object.entries(ACTIVE_TOOL.value.inputSchema?.properties as Record<string, {type?: string}> || {})) {
		SAMPLE[KEY] = VALUE.type === "number" ? 1 : VALUE.type === "boolean" ? true : ""
	}
	TOOL_ARGS.value = JSON.stringify(SAMPLE, null, 2)
	TOOL_OUTPUT.value = ""
	TOOL_MODAL_OPEN.value = true
}

const executeTool = async () => {
	if (!ACTIVE_TOOL.value) return
	TOOL_RUNNING.value = true
	TOOL_OUTPUT.value = ""
	try {
		const ARGS = JSON.parse(TOOL_ARGS.value) as Record<string, unknown>
		const RESULT = ACTIVE_TOOL.value.serverId
			? await RUNTIME.mcpCallTool(ACTIVE_TOOL.value.serverId, ACTIVE_TOOL.value.name, ARGS)
			: await RUNTIME.toolsExecuteManual(ACTIVE_TOOL.value.name, ARGS)
		TOOL_OUTPUT.value = JSON.stringify(RESULT, null, 2)
	} catch (error) {
		TOOL_OUTPUT.value = `执行出错: ${error instanceof Error ? error.message : String(error)}`
	} finally {
		TOOL_RUNNING.value = false
	}
}

const toggleTool = async (tool: ToolDto) => {
	try {
		await RUNTIME.toolsSetEnabled(tool.name, !tool.enabled)
		await RUNTIME.refresh()
	} catch (error) {
		MESSAGE.error(`更新工具失败: ${error instanceof Error ? error.message : String(error)}`)
	}
}

const importConfig = async () => {
	if (!IMPORT_URL.value.trim()) return
	IMPORTING.value = true
	IMPORT_ERROR.value = ""
	try {
		await RUNTIME.mcpImportUrl(IMPORT_URL.value.trim())
		IMPORT_OPEN.value = false
		await refresh()
		MESSAGE.success("MCP 配置导入成功")
	} catch (error) {
		IMPORT_ERROR.value = error instanceof Error ? error.message : String(error)
	} finally {
		IMPORTING.value = false
	}
}
</script>

<template>
	<div class="mcp-settings">
		<header class="header">
			<div>
				<h2 class="title glow-teal">MCP 与工具</h2>
				<p class="subtitle">管理后端 MCP 服务器与内置工具。工具调用始终由 C# 执行。</p>
			</div>
			<div class="actions">
				<button class="btn-ghost" :disabled="LOADING" @click="refresh"><Icon name="refresh" :class="{spin: LOADING}" :size="14"/>刷新</button>
				<button class="btn-ghost" @click="openImport"><Icon name="external-link" :size="14"/>导入 URL</button>
				<button class="btn-primary" @click="newForm"><Icon name="plus" :size="14"/>添加服务器</button>
			</div>
		</header>

		<nav class="tabs">
			<button :class="{active: SUB_TAB === 'servers'}" @click="SUB_TAB = 'servers'">MCP 服务器 ({{ SERVERS.length }})</button>
			<button :class="{active: SUB_TAB === 'builtin'}" @click="SUB_TAB = 'builtin'">内置工具 ({{ BUILTIN_TOOLS.length }})</button>
		</nav>

		<section v-if="SUB_TAB === 'servers'" class="server-list">
			<div v-if="SERVERS.length === 0" class="empty">尚未配置 MCP 服务器。可添加本地 stdio 服务或 SSE 服务。</div>
			<div v-for="server in SERVERS" :key="server.serverId" class="server-card">
				<div class="server-main">
					<div class="server-title-row">
						<Icon name="server" :size="17" class="server-icon"/>
						<strong>{{ server.name }}</strong>
						<span class="status" :class="server.status">{{ server.status }}</span>
					</div>
					<p v-if="server.errorMessage" class="error-text">{{ server.errorMessage }}</p>
					<div v-if="server.tools?.length" class="tool-chips">
						<button v-for="tool in server.tools" :key="tool.name" class="tool-chip" @click="openTool(tool, server.serverId)">
							{{ tool.name }}
						</button>
					</div>
					<span v-else class="hint">未发现在线工具</span>
				</div>
				<div class="server-actions">
					<button v-if="server.status !== 'connected'" class="btn-ghost" @click="connect(server.serverId)">连接</button>
					<button v-else class="btn-ghost" @click="disconnect(server.serverId)">断开</button>
					<n-popconfirm positive-text="删除" negative-text="取消" @positive-click="remove(server.serverId)">
						<template #trigger><button class="btn-danger">删除</button></template>
						确定删除「{{ server.name }}」的配置吗？
					</n-popconfirm>
				</div>
			</div>
		</section>

		<section v-else class="tool-section">
			<div class="search-row"><input v-model="SEARCH" class="input" placeholder="搜索内置工具..."/></div>
			<div class="tool-grid">
				<div v-for="tool in FILTERED_TOOLS" :key="tool.name" class="builtin-card" :class="{disabled: !tool.enabled}">
					<div class="tool-header"><strong>{{ tool.name }}</strong><span class="permission">{{ tool.permissionLevel }}</span></div>
					<p>{{ tool.description }}</p>
					<div class="tool-actions">
						<button class="btn-ghost" :disabled="!tool.enabled || tool.permissionLevel !== 'safe'" @click="openTool(tool)">测试</button>
						<n-switch :value="tool.enabled" @update:value="toggleTool(tool)"/>
					</div>
				</div>
			</div>
		</section>

		<div v-if="MODAL_OPEN" class="modal-overlay" @click.self="MODAL_OPEN = false">
			<div class="modal-card">
				<header class="modal-header"><h3>添加 MCP 服务器</h3><button class="close" @click="MODAL_OPEN = false"><Icon name="close" :size="16"/></button></header>
				<div class="modal-body">
					<label class="field">名称<input v-model="FORM.name" class="input"/></label>
					<label class="field">传输协议<select v-model="FORM.transport" class="input"><option value="stdio">Stdio</option><option value="sse">SSE</option></select></label>
					<template v-if="FORM.transport === 'stdio'">
						<label class="field">命令<input v-model="FORM.command" class="input" placeholder="npx / python / node"/></label>
						<label class="field">参数 (空格分隔)<input v-model="ARGS_INPUT" class="input"/></label>
						<label class="field">环境变量 (每行 KEY=VALUE)<textarea v-model="ENV_INPUT" class="input textarea" rows="3"/></label>
					</template>
					<label v-else class="field">SSE 地址<input v-model="FORM.url" class="input" placeholder="http://localhost:3000/sse"/></label>
					<div class="checks"><label><input v-model="FORM.enabled" type="checkbox"/>启用</label><label><input v-model="FORM.autoConnect" type="checkbox"/>启动时自动连接</label></div>
					<div v-if="TEST_RESULT" class="test-result" :class="TEST_RESULT.status">{{ TEST_RESULT.status === 'connected' ? `连接成功，发现 ${TEST_RESULT.tools?.length || 0} 个工具` : TEST_RESULT.errorMessage }}</div>
				</div>
				<footer class="modal-footer"><button class="btn-ghost" @click="testServer">{{ TESTING ? "测试中..." : "测试连接" }}</button><button class="btn-primary" :disabled="LOADING" @click="saveServer">保存</button></footer>
			</div>
		</div>

		<div v-if="IMPORT_OPEN" class="modal-overlay" @click.self="IMPORT_OPEN = false">
			<div class="modal-card"><header class="modal-header"><h3>导入 MCP JSON</h3><button class="close" @click="IMPORT_OPEN = false"><Icon name="close" :size="16"/></button></header><div class="modal-body"><p class="hint">后端会使用安全 URL 策略读取并解析 mcp.json。</p><input v-model="IMPORT_URL" class="input" placeholder="https://.../mcp.json"/><p v-if="IMPORT_ERROR" class="error-text">{{ IMPORT_ERROR }}</p></div><footer class="modal-footer"><button class="btn-ghost" @click="IMPORT_OPEN = false">取消</button><button class="btn-primary" :disabled="IMPORTING || !IMPORT_URL.trim()" @click="importConfig">导入</button></footer></div>
		</div>

		<div v-if="TOOL_MODAL_OPEN && ACTIVE_TOOL" class="modal-overlay" @click.self="TOOL_MODAL_OPEN = false">
			<div class="modal-card"><header class="modal-header"><h3>测试工具: {{ ACTIVE_TOOL.name }}</h3><button class="close" @click="TOOL_MODAL_OPEN = false"><Icon name="close" :size="16"/></button></header><div class="modal-body"><p class="hint">{{ ACTIVE_TOOL.description }}</p><textarea v-model="TOOL_ARGS" class="input textarea" rows="7"/><pre v-if="TOOL_OUTPUT" class="output">{{ TOOL_OUTPUT }}</pre></div><footer class="modal-footer"><button class="btn-ghost" @click="TOOL_MODAL_OPEN = false">关闭</button><button class="btn-primary" :disabled="TOOL_RUNNING" @click="executeTool">{{ TOOL_RUNNING ? "执行中..." : "执行" }}</button></footer></div>
		</div>
	</div>
</template>

<style scoped lang="less">
.mcp-settings { width: 100%; height: 100%; display: flex; flex-direction: column; gap: 1.2rem; overflow-y: auto; padding: 1.6rem 2.4rem 2rem; }
.header, .server-title-row, .server-actions, .tool-header, .tool-actions, .modal-header, .modal-footer, .actions { display: flex; align-items: center; }
.header, .server-card, .modal-header, .modal-footer { justify-content: space-between; }
.title { margin: 0; font-size: 1.8rem; color: var(--text-primary); }
.subtitle, .hint, .empty { margin: 0; color: var(--text-faint); font-size: 1.15rem; line-height: 1.5; }
.actions, .server-actions { gap: 0.6rem; }
.tabs { display: flex; gap: 0.6rem; border-bottom: 0.1rem solid var(--line-subtle); }
.tabs button { padding: 0.7rem 1.2rem; border: none; border-bottom: 0.2rem solid transparent; background: transparent; color: var(--text-muted); font: inherit; font-size: 1.2rem; cursor: pointer; }
.tabs button.active { color: var(--nori-teal-bright); border-color: var(--nori-teal); }
.btn-ghost, .btn-primary, .btn-danger { display: inline-flex; align-items: center; gap: 0.4rem; padding: 0.55rem 1rem; border-radius: var(--radius-sm); font: inherit; font-size: 1.15rem; cursor: pointer; }
.btn-ghost { color: var(--text-muted); border: 0.1rem solid var(--line-subtle); background: rgba(255,255,255,0.03); }
.btn-primary { color: #03101c; border: none; background: linear-gradient(135deg, var(--nori-teal-bright), var(--nori-teal)); font-weight: 600; }
.btn-danger { color: var(--danger); border: 0.1rem solid rgba(251,60,68,0.3); background: rgba(251,60,68,0.08); }
button:disabled { opacity: 0.5; cursor: not-allowed; }
.server-list, .tool-grid { display: flex; flex-direction: column; gap: 0.9rem; }
.server-card, .builtin-card { display: flex; gap: 1rem; padding: 1.3rem 1.5rem; border: 0.1rem solid var(--line-subtle); border-radius: var(--radius-md); background: var(--bg-card); }
.server-main { flex: 1; min-width: 0; }
.server-title-row { gap: 0.7rem; font-size: 1.3rem; color: var(--text-primary); }
.server-icon { color: var(--nori-teal-bright); }
.status, .permission { padding: 0.2rem 0.6rem; border-radius: var(--radius-pill); font-size: 1rem; background: rgba(255,255,255,0.08); color: var(--text-muted); }
.status.connected { color: #20e090; background: rgba(32,224,144,0.12); }
.status.error { color: var(--danger); background: rgba(251,60,68,0.12); }
.error-text { margin: 0.6rem 0 0; color: var(--danger); font-size: 1.1rem; }
.tool-chips { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.8rem; }
.tool-chip { padding: 0.35rem 0.7rem; border: 0.1rem solid var(--line-subtle); border-radius: var(--radius-pill); background: rgba(125,227,255,0.06); color: var(--nori-teal-bright); font: inherit; font-size: 1.05rem; cursor: pointer; }
.tool-section, .server-list { min-height: 0; }
.search-row { margin-bottom: 0.8rem; }
.input { width: 100%; box-sizing: border-box; padding: 0.75rem 1rem; border: 0.1rem solid var(--line-subtle); border-radius: var(--radius-sm); background: rgba(255,255,255,0.04); color: var(--text-primary); font: inherit; font-size: 1.2rem; outline: none; }
.input:focus { border-color: var(--nori-teal); }
.tool-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(25rem, 1fr)); }
.builtin-card { flex-direction: column; }
.builtin-card.disabled { opacity: 0.55; }
.tool-header, .tool-actions { justify-content: space-between; gap: 0.8rem; }
.builtin-card p { flex: 1; margin: 0; color: var(--text-muted); font-size: 1.1rem; line-height: 1.45; }
.permission { color: var(--nori-teal-soft); }
.modal-overlay { position: fixed; inset: 0; z-index: 100; display: flex; align-items: center; justify-content: center; background: rgba(2,8,16,0.72); backdrop-filter: blur(0.4rem); }
.modal-card { width: min(48rem, 92vw); max-height: 86vh; display: flex; flex-direction: column; background: #0a1a2c; border: 0.1rem solid var(--line-strong); border-radius: var(--radius-lg); }
.modal-header, .modal-footer { padding: 1.1rem 1.5rem; border-color: var(--line-subtle); }
.modal-header { border-bottom: 0.1rem solid var(--line-subtle); }
.modal-header h3 { margin: 0; color: var(--text-primary); font-size: 1.4rem; }
.modal-footer { justify-content: flex-end; gap: 0.7rem; border-top: 0.1rem solid var(--line-subtle); }
.close { border: none; background: transparent; color: var(--text-muted); cursor: pointer; }
.modal-body { display: flex; flex-direction: column; gap: 1rem; padding: 1.4rem 1.5rem; overflow-y: auto; }
.field { display: flex; flex-direction: column; gap: 0.45rem; color: var(--text-muted); font-size: 1.15rem; }
.textarea { resize: vertical; line-height: 1.5; }
.checks { display: flex; gap: 1.5rem; color: var(--text-muted); font-size: 1.15rem; }
.checks label { display: inline-flex; align-items: center; gap: 0.4rem; }
.test-result { padding: 0.7rem 1rem; border-radius: var(--radius-sm); font-size: 1.1rem; }
.test-result.connected { color: #20e090; background: rgba(32,224,144,0.1); }
.test-result.error { color: var(--danger); background: rgba(251,60,68,0.1); }
.output { max-height: 18rem; overflow: auto; margin: 0; padding: 0.9rem; white-space: pre-wrap; color: var(--text-body); background: rgba(0,0,0,0.25); border-radius: var(--radius-sm); font-size: 1.1rem; }
.spin { animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
</style>
