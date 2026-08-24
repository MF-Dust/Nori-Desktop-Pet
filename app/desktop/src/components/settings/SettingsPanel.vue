<script setup lang="ts">
/**
 * 设置面板
 *
 * 信息构架: 左侧二级列表 (分组) + 顶部面包屑 + 搜索过滤。
 * 键盘: `/` 聚焦搜索, ↑/↓ 在可见项间移动, Enter 打开。
 */
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import type {IconName} from "../../services/icon"
import AiSettings from "./AiSettings.vue"
import VoiceSettings from "./VoiceSettings.vue"
import ProactiveSettings from "./ProactiveSettings.vue"
import MemorySettings from "./MemorySettings.vue"
import SkillsSettings from "./SkillsSettings.vue"
import McpSettings from "./McpSettings.vue"
import GeneralSettings from "./GeneralSettings.vue"
import DebugSettings from "./DebugSettings.vue"
import AboutSettings from "./AboutSettings.vue"

type SettingsTabKey = "ai" | "memory" | "voice" | "proactive" | "skills" | "mcp" | "general" | "debug" | "about"

const props = withDefaults(defineProps<{
	/** 打开时直达的子页 (主页磁贴跳转用) */
	initialTab?: string
}>(), {
	initialTab: "",
})

const I18N = computed(() => useLanguages().views.main.settingsTabs)
const GROUP_I18N = computed(() => useLanguages().views.main.settingsGroups)
const SEARCH_I18N = computed(() => useLanguages().views.main.settingsSearch)

interface TabItem {
	key: SettingsTabKey
	label: string
	icon: IconName
	/** 搜索关键词 (中英混合, 命中即显示) */
	keywords: string
}

interface TabGroup {
	title: string
	tabs: TabItem[]
}

const TAB_GROUPS = computed<TabGroup[]>(() => [
	{
		title: GROUP_I18N.value.core,
		tabs: [
			{key: "ai", label: I18N.value.ai, icon: "cpu", keywords: "ai llm model apikey 大脑 模型 密钥 人格"},
			{key: "memory", label: I18N.value.memory, icon: "package", keywords: "memory embedding 记忆 向量 检索"},
		],
	},
	{
		title: GROUP_I18N.value.perception,
		tabs: [
			{key: "voice", label: I18N.value.voice, icon: "volume", keywords: "voice tts stt 语音 朗读 麦克风 音量"},
			{key: "proactive", label: I18N.value.proactive, icon: "sparkles", keywords: "proactive idle reminder 主动 提醒 问候 日程"},
		],
	},
	{
		title: GROUP_I18N.value.extend,
		tabs: [
			{key: "skills", label: I18N.value.skills, icon: "sparkles", keywords: "skill prompt 技能 指令 市场"},
			{key: "mcp", label: I18N.value.mcp, icon: "plug", keywords: "mcp tool server 工具 服务器 连接"},
		],
	},
	{
		title: GROUP_I18N.value.system,
		tabs: [
			{key: "general", label: I18N.value.general, icon: "settings", keywords: "general language startup telemetry privacy 常规 语言 启动 遥测 隐私 诊断"},
			{key: "debug", label: I18N.value.debug, icon: "terminal", keywords: "debug log diagnostic 调试 日志 诊断 崩溃"},
			{key: "about", label: I18N.value.about, icon: "info", keywords: "about license version 关于 声明 版本 协议"},
		],
	},
])

const currentTab = ref<SettingsTabKey>("ai")
const keyword = ref("")
const searchRef = ref<HTMLInputElement>()

// 过滤后的分组 (空搜索时全展示)
const VISIBLE_GROUPS = computed<TabGroup[]>(() => {
	const NEEDLE = keyword.value.trim().toLowerCase()
	if (!NEEDLE) return TAB_GROUPS.value
	return TAB_GROUPS.value
		.map(group => ({
			title: group.title,
			tabs: group.tabs.filter(tab =>
				tab.label.toLowerCase().includes(NEEDLE) || tab.keywords.toLowerCase().includes(NEEDLE)),
		}))
		.filter(group => group.tabs.length > 0)
})

const VISIBLE_TABS = computed<TabItem[]>(() => VISIBLE_GROUPS.value.flatMap(group => group.tabs))

// 当前项所属分组 (面包屑)
const currentGroupTitle = computed(() =>
	TAB_GROUPS.value.find(group => group.tabs.some(tab => tab.key === currentTab.value))?.title ?? "")
const currentTabLabel = computed(() =>
	TAB_GROUPS.value.flatMap(group => group.tabs).find(tab => tab.key === currentTab.value)?.label ?? "")

// 搜索把当前项过滤掉时, 自动跳到第一个可见项
watch(VISIBLE_TABS, (tabs) => {
	if (tabs.length === 0) return
	if (!tabs.some(tab => tab.key === currentTab.value)) currentTab.value = tabs[0].key
})

// 主页磁贴要求直达某个子页
watch(() => props.initialTab, (value) => {
	if (value && TAB_GROUPS.value.some(group => group.tabs.some(tab => tab.key === value))) {
		currentTab.value = value as SettingsTabKey
	}
}, {immediate: true})

// 上下键在可见项之间移动
const moveSelection = (delta: number) => {
	const TABS = VISIBLE_TABS.value
	if (TABS.length === 0) return
	const INDEX = TABS.findIndex(tab => tab.key === currentTab.value)
	const NEXT = (INDEX + delta + TABS.length) % TABS.length
	currentTab.value = TABS[NEXT].key
}

const onListKeydown = (event: KeyboardEvent) => {
	if (event.key === "ArrowDown") {
		event.preventDefault()
		moveSelection(1)
	} else if (event.key === "ArrowUp") {
		event.preventDefault()
		moveSelection(-1)
	}
}

// `/` 聚焦搜索框 (输入状态下不抢焦点)
const onGlobalKeydown = (event: KeyboardEvent) => {
	if (event.key !== "/" || event.ctrlKey || event.metaKey || event.altKey) return
	const TARGET = event.target as HTMLElement | null
	if (TARGET && /^(INPUT|TEXTAREA)$/.test(TARGET.tagName)) return
	event.preventDefault()
	void nextTick(() => searchRef.value?.focus())
}

onMounted(() => window.addEventListener("keydown", onGlobalKeydown))
onBeforeUnmount(() => window.removeEventListener("keydown", onGlobalKeydown))
</script>

<template>
	<div class="w-full h-full min-h-0 flex overflow-hidden glass-panel rounded-lg shadow-[0_0.8rem_3.2rem_rgba(0,0,0,0.45)]">
		<!-- 左侧二级列表导航 -->
		<nav
			class="w-[19rem] shrink-0 flex flex-col min-h-0 border-r border-line-subtle bg-bg-abyss/60 backdrop-blur-[1.4rem]"
			:aria-label="currentTabLabel"
			@keydown="onListKeydown"
		>
			<!-- 顶部快捷搜索框 -->
			<div class="p-3.5 border-b border-line-subtle bg-bg-deep/40">
				<div class="relative">
					<span class="absolute left-3 top-1/2 -translate-y-1/2 text-text-faint pointer-events-none">
						<Icon name="search" :size="14"/>
					</span>
					<input
						ref="searchRef"
						v-model="keyword"
						class="input-base pl-9 pr-8 text-sm"
						type="search"
						:placeholder="SEARCH_I18N.placeholder"
						spellcheck="false"
						:aria-label="SEARCH_I18N.placeholder"
					/>
					<button
						v-if="keyword"
						type="button"
						class="absolute right-2 top-1/2 -translate-y-1/2 btn-icon w-5.5 h-5.5"
						:aria-label="SEARCH_I18N.clear"
						:title="SEARCH_I18N.clear"
						@click="keyword = ''"
					>
						<Icon name="close" :size="11"/>
					</button>
					<span
						v-else
						class="absolute right-2.5 top-1/2 -translate-y-1/2 px-1.2 py-0.2 rounded-[0.3rem] text-xs mono text-text-faint bg-white/6 border border-line-subtle pointer-events-none"
					>/</span>
				</div>
			</div>

			<!-- 分组列表项 -->
			<div class="flex-1 scroll-area p-3 flex flex-col gap-4">
				<div v-for="group in VISIBLE_GROUPS" :key="group.title" class="flex flex-col gap-1.2">
					<span class="px-2 text-hint font-600 uppercase tracking-[0.08rem] text-text-faint/90">{{ group.title }}</span>
					<button
						v-for="tab in group.tabs"
						:key="tab.key"
						type="button"
						class="nav-item group py-2.2 px-3"
						:class="currentTab === tab.key ? 'nav-item-active' : ''"
						:aria-current="currentTab === tab.key ? 'page' : undefined"
						@click="currentTab = tab.key"
					>
						<Icon :name="tab.icon" :size="15" class="shrink-0 transition-transform duration-200 group-hover:scale-110"/>
						<span class="truncate font-500">{{ tab.label }}</span>
					</button>
				</div>

				<p v-if="VISIBLE_GROUPS.length === 0" class="px-2 py-6 text-sub text-center leading-relaxed">{{ SEARCH_I18N.empty }}</p>
			</div>
		</nav>

		<!-- 右侧内容面板 -->
		<div class="flex-1 min-w-0 flex flex-col min-h-0 bg-bg-deep/30">
			<!-- 顶部面包屑导航条 -->
			<div class="relative shrink-0 flex items-center gap-2.5 px-6 py-3.5 border-b border-line-subtle bg-bg-deep/75 backdrop-blur-[1.2rem]">
				<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/20 to-transparent pointer-events-none"/>
				<span class="text-sm text-text-faint font-500">{{ currentGroupTitle }}</span>
				<Icon name="arrow-right" :size="11" class="text-text-faint"/>
				<span class="text-sm font-600 tracking-[0.02rem] text-nori-teal-bright [text-shadow:0_0_1rem_var(--glow-teal-soft)]">{{ currentTabLabel }}</span>
			</div>

			<!-- 内容滚动区 -->
			<div
				class="flex-1 min-h-0 flex flex-col scroll-area p-5"
				:data-settings-panel="currentTab"
			>
				<AiSettings v-if="currentTab === 'ai'"/>
				<MemorySettings v-else-if="currentTab === 'memory'"/>
				<VoiceSettings v-else-if="currentTab === 'voice'"/>
				<ProactiveSettings v-else-if="currentTab === 'proactive'"/>
				<SkillsSettings v-else-if="currentTab === 'skills'"/>
				<McpSettings v-else-if="currentTab === 'mcp'"/>
				<GeneralSettings v-else-if="currentTab === 'general'"/>
				<DebugSettings v-else-if="currentTab === 'debug'"/>
				<AboutSettings v-else/>
			</div>
		</div>
	</div>
</template>
