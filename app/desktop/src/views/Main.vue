<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import TitleBar from "../components/TitleBar.vue"
import Icon from "../components/Icon.vue"
import type {IconName} from "../services/icon"
import {hideWindow, showWindow} from "../services/window"

// ---- 侧边导航项 ----
type NavKey = "home" | "talk" | "model" | "settings"

const NAV_ITEMS: {key: NavKey; label: string; icon: IconName}[] = [
	{key: "home", label: "主页", icon: "noriOS"},
	{key: "talk", label: "对话", icon: "send"},
	{key: "model", label: "模型", icon: "arrow-up"},
	{key: "settings", label: "设置", icon: "loading"},
]

const activeNav = ref<NavKey>("home")
const currentNav = computed(() => NAV_ITEMS.find((item) => item.key === activeNav.value))

// 窗口操作
const closeWindow = () => {
	invoke("exit_app")
}

// 最小化: 收进桌宠, 关闭主窗口面板 (桌宠窗口仍可见)
const minimizeToPet = async () => {
	await hideWindow("main")
}

// 唤出桌宠: 记录日志 (桌宠窗口由 Rust 侧管理置顶)
const summonPet = async () => {
	await showWindow("pet")
	await invoke("write_log", {level: "info", message: "主窗口唤出桌宠"})
}

onMounted(() => {
	invoke("write_log", {level: "info", message: "主窗口 Main 挂载完成"})
})
</script>

<template>
	<div class="main-window">
		<TitleBar>
			<span class="nav-title">{{ currentNav?.label }}</span>
			<div class="titlebar-right">
				<button class="icon-btn" title="最小化到桌宠" @click="minimizeToPet">
					<Icon name="arrow-down" :size="16"/>
				</button>
				<button class="close-btn" title="退出" @click="closeWindow">
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

			<main class="content">
				<h1 class="content-title glow-teal">{{ currentNav?.label }}</h1>
				<p class="content-desc">此区域为 {{ currentNav?.label }} 页面占位, 功能将在此处实现。</p>
			</main>
		</div>

		<div class="footer">
			<button class="btn-primary" @click="summonPet">
				<Icon name="send" :size="16"/>
				唤出桌宠
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

.nav-title {
	font-size: 1.3rem;
	color: var(--text-muted);
	letter-spacing: 0.04rem;
}

.icon-btn {
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

	&:hover {
		background-color: rgba(255, 255, 255, 0.08);
		color: var(--nori-teal-bright);
	}
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

.footer {
	padding: 1rem 1.6rem;
	display: flex;
	justify-content: flex-end;
	border-top: 0.1rem solid var(--line-subtle);
}
</style>
