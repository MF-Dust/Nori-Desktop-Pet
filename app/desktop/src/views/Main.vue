<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import useLanguages from "../services/i18n/useLanguages.ts"
import TitleBar from "../components/TitleBar.vue"
import Icon from "../components/Icon.vue"
import type {IconName} from "../services/icon"
import {showWindow, hideWindow} from "../services/window"
import HomePanel from "../components/home/HomePanel.vue"
import AiSettings from "../components/settings/AiSettings.vue"
import ModelManagement from "../components/settings/ModelManagement.vue"
import ChatView from "../components/ChatView.vue"

const I18N = computed(() => useLanguages().views.main)

// ---- 侧边导航项 ----
type NavKey = "home" | "talk" | "model" | "settings" | "about"

const NAV_ITEMS = computed<{key: NavKey; label: string; icon: IconName}[]>(() => [
	{key: "home", label: I18N.value.nav.home, icon: "noriOS"},
	{key: "talk", label: I18N.value.nav.talk, icon: "send"},
	{key: "model", label: I18N.value.nav.model, icon: "package"},
	{key: "settings", label: I18N.value.nav.settings, icon: "settings"},
	{key: "about", label: I18N.value.nav.about, icon: "info"},
])

const activeNav = ref<NavKey>("home")
const currentNav = computed(() => NAV_ITEMS.value.find((item) => item.key === activeNav.value))

// 窗口操作: 最小化主窗口 / 退出应用
const minimizeMain = async () => {
	await hideWindow("main")
}

const closeWindow = () => {
	invoke("exit_app")
}

// 桌宠当前是否显示
const petVisible = ref(false)

const refreshPetVisible = async () => {
	try {
		petVisible.value = await invoke<boolean>("window_is_visible", {label: "pet"})
	} catch {
		petVisible.value = false
	}
}

// 召唤 / 收起桌宠
const togglePet = async () => {
	if (petVisible.value) {
		await hideWindow("pet")
		await invoke("write_log", {level: "info", message: "主窗口收起 Nori"})
	} else {
		await showWindow("pet")
		await invoke("write_log", {level: "info", message: "主窗口唤出 Nori"})
	}
	await refreshPetVisible()
}

onMounted(async () => {
	await refreshPetVisible()
	invoke("write_log", {level: "info", message: "主窗口 Main 挂载完成"})
})
</script>

<template>
	<div class="main-window">
		<TitleBar>
			<span class="nav-title">{{ currentNav?.label }}</span>
			<div class="titlebar-right">
				<button class="win-btn" title="最小化" @click="minimizeMain">
					<Icon name="minus" :size="15"/>
				</button>
				<button class="win-btn close-btn" title="退出应用" @click="closeWindow">
					<Icon name="close" class="close-icon"/>
				</button>
			</div>
		</TitleBar>

		<div class="body">
			<aside class="sidebar">
				<button
					v-for="item in NAV_ITEMS"
					:key="item.key"
					class="nav-item"
					:class="{active: item.key === activeNav}"
					@click="activeNav = item.key"
				>
					<Icon :name="item.icon" :size="18"/>
					<span>{{ item.label }}</span>
				</button>
			</aside>

			<main class="content" :class="{compact: activeNav === 'home'}">
				<!-- 主页看板 -->
				<HomePanel
					v-if="activeNav === 'home'"
					:pet-visible="petVisible"
					@toggle-pet="togglePet"
					@navigate="(tab) => activeNav = tab"
				/>

				<!-- 对话 -->
				<ChatView v-else-if="activeNav === 'talk'" @go-settings="activeNav = 'settings'"/>

				<!-- 模型管理 -->
				<ModelManagement v-else-if="activeNav === 'model'"/>

				<!-- 设置: AI 接入 -->
				<AiSettings v-else-if="activeNav === 'settings'"/>

				<!-- 声明 -->
				<section v-else-if="activeNav === 'about'" class="about-panel">
					<h2 class="about-title glow-teal">{{ I18N.about.title }}</h2>
					<p class="about-line">{{ I18N.about.license }}</p>
					<p class="about-line">{{ I18N.about.authors }}</p>
					<p class="about-desc">{{ I18N.about.desc }}</p>
				</section>
			</main>
		</div>

		<div class="footer">
			<button class="btn-primary" @click="togglePet">
				<Icon :name="petVisible ? 'close' : 'sparkles'" :size="16"/>
				{{ petVisible ? I18N.hidePet : I18N.summonPet }}
			</button>
		</div>
	</div>
</template>

<style scoped lang="less">
.main-window {
	width: 100vw;
	height: 100vh;
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	border-radius: var(--radius-lg);
	display: flex;
	flex-direction: column;
	overflow: hidden;
	user-select: none;
}

.titlebar-right {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.win-btn {
	width: 2.6rem;
	height: 2.6rem;
	border: none;
	border-radius: 50%;
	background-color: transparent;
	color: var(--text-muted);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.15s ease;

	&:hover {
		background-color: rgba(255, 255, 255, 0.08);
		color: var(--nori-teal-bright);
	}

	&.close-btn:hover {
		background-color: rgba(255, 80, 80, 0.15);
		color: #ff6b6b;
	}
}

.nav-title {
	font-size: 1.3rem;
	color: var(--text-muted);
	letter-spacing: 0.04rem;
}

.body {
	flex: 1;
	display: flex;
	min-height: 0;
}

.sidebar {
	width: 14rem;
	padding: 1.2rem 0.8rem;
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
	border-right: 0.1rem solid var(--line-subtle);
}

.nav-item {
	display: flex;
	align-items: center;
	gap: 0.9rem;
	padding: 0.9rem 1.1rem;
	border: none;
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-family: inherit;
	font-size: 1.3rem;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		color: var(--text-primary);
	}

	&.active {
		background: rgba(125, 227, 255, 0.14);
		color: var(--nori-teal-bright);
		box-shadow: inset 0 0 0 0.1rem var(--line-strong);
	}
}

.content {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1rem;
	padding: 2rem;
	overflow: auto;

	&.compact {
		align-items: stretch;
		justify-content: flex-start;
		padding: 1.6rem 2rem;
	}
}

.content-title {
	font-size: 2.6rem;
	font-weight: 700;
	color: var(--text-primary);
}

.content-desc {
	font-size: 1.3rem;
	color: var(--text-faint);
}

// 声明页
.about-panel {
	width: 100%;
	max-width: 52rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 1.2rem;
	padding: 2rem;
}

.about-title {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--text-primary);
}

.about-line {
	font-size: 1.3rem;
	color: var(--text-body);
	line-height: 1.6;
}

.about-desc {
	font-size: 1.2rem;
	color: var(--text-faint);
	line-height: 1.6;
	text-align: center;
}

.footer {
	padding: 1rem 1.6rem;
	display: flex;
	justify-content: flex-end;
	border-top: 0.1rem solid var(--line-subtle);
}
</style>