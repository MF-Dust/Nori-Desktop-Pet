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

const TABS = computed<{key: SettingsTabKey; label: string; icon: IconName}[]>(() => [
	{key: "ai", label: I18N.value.ai, icon: "sparkles"},
	{key: "voice", label: I18N.value.voice, icon: "volume"},
	{key: "proactive", label: I18N.value.proactive, icon: "noriOS"},
	{key: "memory", label: I18N.value.memory, icon: "package"},
	{key: "skills", label: I18N.value.skills, icon: "sparkles"},
	{key: "mcp", label: I18N.value.mcp, icon: "plug"},
	{key: "general", label: I18N.value.general, icon: "settings"},
	{key: "debug", label: I18N.value.debug, icon: "terminal"},
])
</script>

<template>
	<div class="settings-panel">
		<!-- 顶部子标签切换栏 -->
		<nav class="settings-nav">
			<button
				v-for="tab in TABS"
				:key="tab.key"
				class="tab-btn"
				:class="{active: currentTab === tab.key}"
				@click="currentTab = tab.key"
			>
				<Icon :name="tab.icon" :size="14"/>
				<span>{{ tab.label }}</span>
			</button>
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
	gap: 0.6rem;
	padding: 0.8rem 1.4rem;
	border-bottom: 0.1rem solid var(--line-subtle);
	background: rgba(8, 22, 36, 0.6);
	backdrop-filter: blur(0.8rem);
	flex-shrink: 0;
	overflow-x: auto;
}

.tab-btn {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.65rem 1.3rem;
	border: 0.1rem solid transparent;
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-size: 1.2rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		color: var(--text-primary);
		background: rgba(125, 227, 255, 0.06);
	}

	&.active {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.12);
		border-color: rgba(125, 227, 255, 0.2);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
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

