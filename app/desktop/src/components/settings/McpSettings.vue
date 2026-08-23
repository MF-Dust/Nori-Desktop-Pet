<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {feedback} from "../../services/feedback"
import {RUNTIME, type McpServerStatusInfo, type ToolDto} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppButton from "../ui/AppButton.vue"
import AppChip from "../ui/AppChip.vue"

const I18N = computed(() => useLanguages().views.main.mcp)
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
		feedback.error(I18N.value.error.load, error)
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
		name: I18N.value.server.defaultName,
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
		feedback.success(EDITING.value ? I18N.value.server.updated : I18N.value.server.added)
	} catch (error) {
		feedback.error(I18N.value.error.save, error)
	} finally {
		LOADING.value = false
	}
}

const testServer = async () => {
	TESTING.value = true
	TEST_RESULT.value = null
	try {
		TEST_RESULT.value = await RUNTIME.mcpTestServer(formPayload())
		if (TEST_RESULT.value.status === "connected") feedback.success(I18N.value.test.success)
		else feedback.warning(TEST_RESULT.value.errorMessage || I18N.value.test.notReady)
	} catch (error) {
		feedback.error(I18N.value.error.test, error)
	} finally {
		TESTING.value = false
	}
}

const connect = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpConnect(id)
		await refresh()
		feedback.success(I18N.value.server.connected)
	} catch (error) {
		feedback.error(I18N.value.error.connect, error)
	} finally {
		LOADING.value = false
	}
}

const disconnect = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpDisconnect(id)
		await refresh()
		feedback.info(I18N.value.server.disconnected)
	} catch (error) {
		feedback.error(I18N.value.error.disconnect, error)
	} finally {
		LOADING.value = false
	}
}

const remove = async (id: string) => {
	LOADING.value = true
	try {
		await RUNTIME.mcpDeleteServer(id)
		await refresh()
		feedback.success(I18N.value.server.deleted)
	} catch (error) {
		feedback.error(I18N.value.error.delete, error)
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
		TOOL_OUTPUT.value = `${I18N.value.tool.execFailed}: ${error instanceof Error ? error.message : String(error)}`
	} finally {
		TOOL_RUNNING.value = false
	}
}

const toggleTool = async (tool: ToolDto) => {
	try {
		await RUNTIME.toolsSetEnabled(tool.name, !tool.enabled)
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.error.toolToggle, error)
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
		feedback.success(I18N.value.import.success)
	} catch (error) {
		IMPORT_ERROR.value = error instanceof Error ? error.message : String(error)
	} finally {
		IMPORTING.value = false
	}
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-3 px-6 pt-4 pb-5 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.subtitle">
			<template #actions>
				<AppButton variant="ghost" size="sm" icon="refresh" :loading="LOADING" :disabled="LOADING" @click="refresh">{{ I18N.refresh }}</AppButton>
				<AppButton variant="ghost" size="sm" icon="external-link" @click="openImport">{{ I18N.importUrl }}</AppButton>
				<AppButton variant="primary" size="sm" icon="plus" @click="newForm">{{ I18N.server.add }}</AppButton>
			</template>
		</AppSectionHeader>

		<nav class="flex gap-1.5 border-b border-line-subtle shrink-0">
			<button
				type="button"
				class="px-3 py-[0.7rem] bg-transparent border-b-2 text-sm font-inherit cursor-pointer transition-colors duration-200 focus-ring"
				:class="SUB_TAB === 'servers'
					? 'text-nori-teal-bright border-b-nori-teal'
					: 'text-text-muted border-b-transparent hover:text-text-primary'"
				:aria-pressed="SUB_TAB === 'servers'"
				@click="SUB_TAB = 'servers'"
			>
				{{ I18N.tabs.servers }} ({{ SERVERS.length }})
			</button>
			<button
				type="button"
				class="px-3 py-[0.7rem] bg-transparent border-b-2 text-sm font-inherit cursor-pointer transition-colors duration-200 focus-ring"
				:class="SUB_TAB === 'builtin'
					? 'text-nori-teal-bright border-b-nori-teal'
					: 'text-text-muted border-b-transparent hover:text-text-primary'"
				:aria-pressed="SUB_TAB === 'builtin'"
				@click="SUB_TAB = 'builtin'"
			>
				{{ I18N.tabs.builtin }} ({{ BUILTIN_TOOLS.length }})
			</button>
		</nav>

		<section v-if="SUB_TAB === 'servers'" class="flex-1 min-h-0 flex flex-col gap-2 scroll-area">
			<p v-if="SERVERS.length === 0" class="m-0 text-hint">{{ I18N.server.empty }}</p>
			<div
				v-for="server in SERVERS"
				:key="server.serverId"
				class="surface-card flex justify-between gap-2.5 px-3.5 py-3 transition-all duration-200
					hover:(border-nori-teal-soft bg-nori-teal-bright/6)"
			>
				<div class="flex-1 min-w-0">
					<div class="flex items-center gap-[0.7rem] text-base text-text-primary">
						<Icon name="server" :size="17" class="text-nori-teal-bright shrink-0"/>
						<strong>{{ server.name }}</strong>
						<AppChip :tone="server.status === 'connected' ? 'success' : (server.status === 'error' ? 'danger' : 'neutral')">
							{{ server.status }}
						</AppChip>
					</div>
					<p v-if="server.errorMessage" class="mt-1.5 mb-0 text-xs text-danger-text">{{ server.errorMessage }}</p>
					<div v-if="server.tools?.length" class="flex flex-wrap gap-1.5 mt-2">
						<button
							v-for="tool in server.tools"
							:key="tool.name"
							type="button"
							class="chip-teal cursor-pointer transition-colors duration-200 focus-ring
								hover:(border-nori-teal-soft bg-nori-teal-bright/16)"
							@click="openTool(tool, server.serverId)"
						>
							{{ tool.name }}
						</button>
					</div>
					<span v-else class="text-hint">{{ I18N.tool.noneOnline }}</span>
				</div>
				<div class="flex items-center gap-1.5 shrink-0">
					<AppButton v-if="server.status !== 'connected'" variant="ghost" size="sm" @click="connect(server.serverId)">{{ I18N.server.connect }}</AppButton>
					<AppButton v-else variant="ghost" size="sm" @click="disconnect(server.serverId)">{{ I18N.server.disconnect }}</AppButton>
					<n-popconfirm
						:positive-text="I18N.common.delete"
						:negative-text="I18N.common.cancel"
						@positive-click="remove(server.serverId)"
					>
						<template #trigger>
							<button type="button" class="btn-danger px-3 py-1.5 text-sm">{{ I18N.common.delete }}</button>
						</template>
						<span class="flex flex-col gap-1">
							<span>{{ I18N.server.deleteConfirm }}</span>
							<strong>{{ server.name }}</strong>
						</span>
					</n-popconfirm>
				</div>
			</div>
		</section>

		<section v-else class="flex-1 min-h-0 flex flex-col">
			<div class="mb-2 shrink-0">
				<input v-model="SEARCH" class="input-base text-sm" :placeholder="I18N.tool.searchPlaceholder"/>
			</div>
			<div class="grid grid-cols-[repeat(auto-fill,minmax(25rem,1fr))] gap-2 scroll-area flex-1">
				<div
					v-for="tool in FILTERED_TOOLS"
					:key="tool.name"
					class="surface-card flex flex-col gap-2.5 px-3.5 py-3 transition-all duration-200 hover:border-line-strong"
					:class="tool.enabled ? '' : 'opacity-55'"
				>
					<div class="flex items-center justify-between gap-2">
						<strong class="text-base text-text-primary">{{ tool.name }}</strong>
						<AppChip tone="teal">{{ tool.permissionLevel }}</AppChip>
					</div>
					<p class="flex-1 m-0 text-xs text-text-muted leading-relaxed">{{ tool.description }}</p>
					<div class="flex items-center justify-between gap-2">
						<AppButton
							variant="ghost"
							size="sm"
							:disabled="!tool.enabled || tool.permissionLevel !== 'safe'"
							@click="openTool(tool)"
						>
							{{ I18N.tool.test }}
						</AppButton>
						<n-switch :value="tool.enabled" @update:value="toggleTool(tool)"/>
					</div>
				</div>
			</div>
		</section>

		<!-- 添加 MCP 服务器弹窗 -->
		<div
			v-if="MODAL_OPEN"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/72 backdrop-blur-[0.4rem]"
			@click.self="MODAL_OPEN = false"
		>
			<div class="w-[min(48rem,92vw)] max-h-[86vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg">
				<header class="flex items-center justify-between gap-2 px-[1.5rem] py-[1.1rem] border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ I18N.server.modalTitle }}</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="MODAL_OPEN = false">
						<Icon name="close" :size="16"/>
					</button>
				</header>
				<div class="flex flex-col gap-2.5 px-[1.5rem] py-3.5 scroll-area">
					<label class="field field-label">{{ I18N.server.name }}<input v-model="FORM.name" class="input-base text-sm"/></label>
					<label class="field field-label">
						{{ I18N.server.transport }}
						<select v-model="FORM.transport" class="input-base text-sm">
							<option value="stdio">Stdio</option>
							<option value="sse">SSE</option>
						</select>
					</label>
					<template v-if="FORM.transport === 'stdio'">
						<label class="field field-label">{{ I18N.server.command }}<input v-model="FORM.command" class="input-base text-sm" placeholder="npx / python / node"/></label>
						<label class="field field-label">{{ I18N.server.args }}<input v-model="ARGS_INPUT" class="input-base text-sm"/></label>
						<label class="field field-label">
							{{ I18N.server.env }}
							<textarea v-model="ENV_INPUT" class="input-base text-sm resize-y leading-relaxed" rows="3"/>
						</label>
					</template>
					<label v-else class="field field-label">{{ I18N.server.sseUrl }}<input v-model="FORM.url" class="input-base text-sm" placeholder="http://localhost:3000/sse"/></label>
					<div class="flex gap-4 text-xs text-text-muted">
						<label class="inline-flex items-center gap-1 cursor-pointer"><input v-model="FORM.enabled" type="checkbox"/>{{ I18N.server.enabled }}</label>
						<label class="inline-flex items-center gap-1 cursor-pointer"><input v-model="FORM.autoConnect" type="checkbox"/>{{ I18N.server.autoConnect }}</label>
					</div>
					<div
						v-if="TEST_RESULT"
						class="px-2.5 py-[0.7rem] rounded-sm text-xs"
						:class="TEST_RESULT.status === 'connected' ? 'text-success bg-success/10' : 'text-danger-text bg-danger/10'"
					>
						<template v-if="TEST_RESULT.status === 'connected'">
							{{ I18N.test.foundPrefix }} {{ TEST_RESULT.tools?.length || 0 }} {{ I18N.test.foundUnit }}
						</template>
						<template v-else>{{ TEST_RESULT.errorMessage }}</template>
					</div>
				</div>
				<footer class="flex items-center justify-end gap-[0.7rem] px-[1.5rem] py-[1.1rem] border-t border-line-subtle">
					<AppButton variant="ghost" size="sm" :loading="TESTING" @click="testServer">{{ TESTING ? I18N.test.testing : I18N.test.run }}</AppButton>
					<AppButton variant="primary" size="sm" :disabled="LOADING" @click="saveServer">{{ I18N.common.save }}</AppButton>
				</footer>
			</div>
		</div>

		<!-- 导入 MCP JSON 弹窗 -->
		<div
			v-if="IMPORT_OPEN"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/72 backdrop-blur-[0.4rem]"
			@click.self="IMPORT_OPEN = false"
		>
			<div class="w-[min(48rem,92vw)] max-h-[86vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg">
				<header class="flex items-center justify-between gap-2 px-[1.5rem] py-[1.1rem] border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ I18N.import.title }}</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="IMPORT_OPEN = false">
						<Icon name="close" :size="16"/>
					</button>
				</header>
				<div class="flex flex-col gap-2.5 px-[1.5rem] py-3.5 scroll-area">
					<p class="m-0 text-hint">{{ I18N.import.hint }}</p>
					<input v-model="IMPORT_URL" class="input-base text-sm" placeholder="https://.../mcp.json"/>
					<p v-if="IMPORT_ERROR" class="m-0 text-xs text-danger-text" role="alert">{{ IMPORT_ERROR }}</p>
				</div>
				<footer class="flex items-center justify-end gap-[0.7rem] px-[1.5rem] py-[1.1rem] border-t border-line-subtle">
					<AppButton variant="ghost" size="sm" @click="IMPORT_OPEN = false">{{ I18N.common.cancel }}</AppButton>
					<AppButton variant="primary" size="sm" :loading="IMPORTING" :disabled="IMPORTING || !IMPORT_URL.trim()" @click="importConfig">{{ I18N.import.confirm }}</AppButton>
				</footer>
			</div>
		</div>

		<!-- 测试工具弹窗 -->
		<div
			v-if="TOOL_MODAL_OPEN && ACTIVE_TOOL"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/72 backdrop-blur-[0.4rem]"
			@click.self="TOOL_MODAL_OPEN = false"
		>
			<div class="w-[min(48rem,92vw)] max-h-[86vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg">
				<header class="flex items-center justify-between gap-2 px-[1.5rem] py-[1.1rem] border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ I18N.tool.testTitle }}: {{ ACTIVE_TOOL.name }}</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="TOOL_MODAL_OPEN = false">
						<Icon name="close" :size="16"/>
					</button>
				</header>
				<div class="flex flex-col gap-2.5 px-[1.5rem] py-3.5 scroll-area">
					<p class="m-0 text-hint">{{ ACTIVE_TOOL.description }}</p>
					<textarea v-model="TOOL_ARGS" class="input-base text-sm resize-y leading-relaxed" rows="7"/>
					<pre v-if="TOOL_OUTPUT" class="m-0 p-2 max-h-[18rem] overflow-auto rounded-sm bg-black/25 text-xs text-text-body whitespace-pre-wrap">{{ TOOL_OUTPUT }}</pre>
				</div>
				<footer class="flex items-center justify-end gap-[0.7rem] px-[1.5rem] py-[1.1rem] border-t border-line-subtle">
					<AppButton variant="ghost" size="sm" @click="TOOL_MODAL_OPEN = false">{{ I18N.common.close }}</AppButton>
					<AppButton variant="primary" size="sm" :loading="TOOL_RUNNING" :disabled="TOOL_RUNNING" @click="executeTool">{{ TOOL_RUNNING ? I18N.tool.running : I18N.tool.execute }}</AppButton>
				</footer>
			</div>
		</div>
	</div>
</template>
