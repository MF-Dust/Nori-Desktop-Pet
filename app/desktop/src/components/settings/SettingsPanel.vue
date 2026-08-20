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

type SettingsTabKey = "ai" | "voice" | "proactive" | "memory" | "skills" | "mcp" | "general"

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
				<Icon :name="tab.icon" :size="15"/>
				<span>{{ tab.label }}</span>
			</button>
		</nav>

		<!-- 设置主视图区 -->
		<div class="settings-view-body">
			<AiSettings v-if="currentTab === 'ai'"/>
			<VoiceSettings v-else-if="currentTab === 'voice'"/>
			<ProactiveSettings v-else-if="currentTab === 'proactive'"/>
			<MemorySettings v-else-if="currentTab === 'memory'"/>
			<SkillsSettings v-else-if="currentTab === 'skills'"/>
			<McpSettings v-else-if="currentTab === 'mcp'"/>
			<GeneralSettings v-else-if="currentTab === 'general'"/>
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
}

.settings-nav {
	display: flex;
	gap: 0.6rem;
	padding: 1.2rem 2rem 0.8rem;
	border-bottom: 0.1rem solid var(--line-subtle);
	background: rgba(10, 26, 36, 0.4);
	flex-shrink: 0;
	overflow-x: auto;
}

.tab-btn {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.4rem;
	border: 0.1rem solid transparent;
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-size: 1.2rem;
	font-weight: 500;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s ease;

	&:hover {
		color: var(--text-primary);
		background: rgba(255, 255, 255, 0.04);
	}

	&.active {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--line-subtle);
		font-weight: 600;
	}
}

.settings-view-body {
	flex: 1;
	min-height: 0;
	display: flex;
	flex-direction: column;
	overflow: hidden;
}
</style>
