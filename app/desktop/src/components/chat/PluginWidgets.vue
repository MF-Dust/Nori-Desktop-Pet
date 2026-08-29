<script setup lang="ts">
/**
 * 插件聊天卡片槽 (通用机制, 不含任何具体插件逻辑)
 *
 * 约定: 活跃插件包内存在 web/card.html 时, 宿主在此以同源 iframe 挂载,
 * 卡片通过 window.postMessage 请求宿主代理调用插件动作 (plugin_action):
 *   卡片 → 宿主: {source: "nori-plugin-widget", requestId, pluginId, actionId, args}
 *   宿主 → 卡片: {source: "nori-plugin-widget-host", requestId, result, error}
 */
import {onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import Icon from "../Icon.vue"

interface WidgetInfo {
	pluginId: string
	title: string
	entry: string
}

interface WidgetCall {
	source: string
	requestId: number
	pluginId: string
	actionId: string
	args?: Record<string, unknown>
}

const widgets = ref<WidgetInfo[]>([])
const expanded = ref(new Set<string>())
const knownIds = new Set<string>()

async function refresh(): Promise<void> {
	try {
		const result = await invoke("plugin_widgets")
		const list = (result as unknown as {widgets: WidgetInfo[]}).widgets ?? []
		widgets.value = list
		// 新出现的卡片默认展开: 扫码登录这类流程不应要求用户先找到折叠入口
		let expandedChanged = false
		const next = new Set(expanded.value)
		for (const widget of list) {
			if (!knownIds.has(widget.pluginId)) {
				knownIds.add(widget.pluginId)
				next.add(widget.pluginId)
				expandedChanged = true
			}
		}
		if (expandedChanged) expanded.value = next
	} catch {
		widgets.value = []
	}
}

function toggle(pluginId: string): void {
	const next = new Set(expanded.value)
	if (next.has(pluginId)) next.delete(pluginId)
	else next.add(pluginId)
	expanded.value = next
}

async function proxyCall(event: MessageEvent): Promise<void> {
	const data = event.data as WidgetCall | undefined
	if (data?.source !== "nori-plugin-widget" || typeof data.requestId !== "number") return
	const reply = (result: unknown, error?: string): void => {
		;(event.source as Window | null)?.postMessage(
			{source: "nori-plugin-widget-host", requestId: data.requestId, result, error},
			"*",
		)
	}
	try {
		const result = await invoke("plugin_action", {
			pluginId: data.pluginId,
			actionId: data.actionId,
			args: data.args,
		})
		reply(result)
	} catch (error) {
		reply(null, error instanceof Error ? error.message : String(error))
	}
}

let refreshTimer: ReturnType<typeof setInterval> | null = null

onMounted(async () => {
	await refresh()
	// 插件激活可能晚于聊天挂载 (宿主启动后异步激活), 轮询保证卡片能及时出现
	refreshTimer = setInterval(() => {
		if (document.hidden) return
		void refresh()
	}, 10_000)
	window.addEventListener("message", proxyCall)
})

onBeforeUnmount(() => {
	if (refreshTimer) clearInterval(refreshTimer)
	window.removeEventListener("message", proxyCall)
})
</script>

<template>
	<div v-if="widgets.length" class="flex flex-col gap-2">
		<div
			v-for="widget in widgets"
			:key="widget.pluginId"
			class="mx-4.5 mt-3 rounded-xl border border-line-subtle bg-bg-deep/60 backdrop-blur-[1rem] text-sm overflow-hidden"
		>
			<button
				class="w-full flex items-center gap-2 px-3.5 py-2 text-left hover:bg-bg-hover/40"
				@click="toggle(widget.pluginId)"
			>
				<span class="text-nori-teal-bright">◆</span>
				<span class="font-medium">{{ widget.title }}</span>
				<span class="flex-1" />
				<Icon :name="expanded.has(widget.pluginId) ? 'arrow-up' : 'arrow-down'" class="w-4 h-4 text-text-muted" />
			</button>
			<iframe
				v-if="expanded.has(widget.pluginId)"
				:src="widget.entry"
				class="w-full border-0 bg-transparent"
				style="height: 22rem"
				:title="widget.title"
			/>
		</div>
	</div>
</template>
