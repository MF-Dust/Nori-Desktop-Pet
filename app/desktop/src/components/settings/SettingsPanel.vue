<script setup lang="ts">
import {computed, ref} from "vue"
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

type SettingsTabKey = "ai" | "voice" | "proactive" | "memory" | "skills" | "mcp" | "general" | "debug"

const I18N = computed(() => useLanguages().views.main.settingsTabs)

const currentTab = ref<SettingsTabKey>("ai")

interface TabGroup {
	title: string
	tabs: {key: SettingsTabKey; label: string; icon: IconName}[]
}

const TAB_GROUPS = computed<TabGroup[]>(() => [
	{
		title: "智能核心",
		tabs: [
			{key: "ai", label: I18N.value.ai, icon: "cpu"},
			{key: "memory", label: I18N.value.memory, icon: "package"},
		],
	},
	{
		title: "感知交互",
		tabs: [
			{key: "voice", label: I18N.value.voice, icon: "volume"},
			{key: "proactive", label: I18N.value.proactive, icon: "sparkles"},
		],
	},
	{
		title: "能力扩展",
		tabs: [
			{key: "skills", label: I18N.value.skills, icon: "sparkles"},
			{key: "mcp", label: I18N.value.mcp, icon: "plug"},
		],
	},
	{
		title: "系统与诊断",
		tabs: [
			{key: "general", label: I18N.value.general, icon: "settings"},
			{key: "debug", label: I18N.value.debug, icon: "terminal"},
		],
	},
])
</script>

<template>
	<div class="settings-panel">
		<!-- 顶部子标签切换栏 (分组分段式设计) -->
		<nav class="settings-nav">
			<div v-for="group in TAB_GROUPS" :key="group.title" class="nav-group">
				<button
					v-for="tab in group.tabs"
					:key="tab.key"
					class="tab-btn"
					:class="{active: currentTab === tab.key}"
					@click="currentTab = tab.key"
				>
					<Icon :name="tab.icon" :size="13"/>
					<span>{{ tab.label }}</span>
				</button>
			</div>
		</nav>

		<!-- 设置主视图区 -->
		<div class="settings-view-body">
			<Transition name="tab-fade" mode="out-in">
				<AiSettings v-if="currentTab === 'ai'"/>
				<VoiceSettings v-else-if="currentTab === 'voice'"/>
				<ProactiveSettings v-else-if="currentTab === 'proactive'"/>
				<MemorySettings v-else-if="currentTab === 'memory'"/>
				<SkillsSettings v-else-if="currentTab === 'skills'"/>
				<McpSettings v-else-if="currentTab === 'mcp'"/>
				<GeneralSettings v-else-if="currentTab === 'general'"/>
				<DebugSettings v-else-if="currentTab === 'debug'"/>
			</Transition>
		</div>
	</div>
</template>

<style scoped lang="less">
.settings-panel {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	min-height: 0;
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-lg);
	overflow: hidden;
	box-shadow: 0 0.8rem 2.8rem rgba(0, 0, 0, 0.35);
	backdrop-filter: blur(1.2rem);
}

.settings-nav {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	padding: 0.8rem 1.4rem;
	border-bottom: 0.1rem solid var(--line-subtle);
	background: rgba(8, 22, 36, 0.6);
	backdrop-filter: blur(0.8rem);
	flex-shrink: 0;
	overflow-x: auto;
}

.nav-group {
	display: inline-flex;
	align-items: center;
	background: rgba(0, 0, 0, 0.25);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	padding: 0.25rem 0.3rem;
	gap: 0.2rem;
}

.tab-btn {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.45rem 1rem;
	border: none;
	border-radius: var(--radius-pill);
	background: transparent;
	color: var(--text-muted);
	font-size: 1.15rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		color: var(--text-primary);
		background: rgba(125, 227, 255, 0.08);
	}

	&.active {
		color: #03101c;
		background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		box-shadow: 0 0.2rem 1rem var(--glow-teal-soft);
		font-weight: 600;
	}
}

.settings-view-body {
	flex: 1;
	min-height: 0;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
}

// 标签过渡
.tab-fade-enter-active,
.tab-fade-leave-active {
	transition: opacity 0.18s ease, transform 0.18s ease;
}

.tab-fade-enter-from {
	opacity: 0;
	transform: translateY(0.6rem);
}

.tab-fade-leave-to {
	opacity: 0;
	transform: translateY(-0.6rem);
}
</style>

