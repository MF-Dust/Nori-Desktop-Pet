<script setup lang="ts">
import {computed, defineAsyncComponent, onMounted, ref} from "vue"
import useLanguages from "../services/i18n/useLanguages.ts"
import {RUNTIME} from "../services/runtime"
import TitleBar from "../components/TitleBar.vue"
import Icon from "../components/Icon.vue"
import type {IconName} from "../services/icon"
import {showWindow, hideWindow} from "../services/window"
import {getWindowByLabel} from "../services/host/window"
import {MODEL_LIST} from "../services/live2d/models"

const HomePanel = defineAsyncComponent(() => import("../components/home/HomePanel.vue"))
const SettingsPanel = defineAsyncComponent(() => import("../components/settings/SettingsPanel.vue"))
const ModelManagement = defineAsyncComponent(() => import("../components/settings/ModelManagement.vue"))
const ChatView = defineAsyncComponent(() => import("../components/ChatView.vue"))

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
	void RUNTIME.exitApp()
}

// 桌宠当前是否显示
const petVisible = ref(false)
const selectedModelName = computed(() => {
	const MODEL_ID = RUNTIME.snapshot.value?.models.selected ?? "nori"
	return MODEL_LIST.find(model => model.id === MODEL_ID)?.name ?? MODEL_ID
})

const refreshPetState = async () => {
	try {
		petVisible.value = await getWindowByLabel("pet").isVisible()
		await RUNTIME.refresh()
	} catch {
		petVisible.value = false
	}
}

// 召唤 / 收起桌宠
const togglePet = async () => {
	if (petVisible.value) {
		await hideWindow("pet")
		await RUNTIME.writeLog("info", "主窗口收起 Nori")
	} else {
		await showWindow("pet")
		await RUNTIME.writeLog("info", "主窗口唤出 Nori")
	}
	await refreshPetState()
}

onMounted(async () => {
	await RUNTIME.init()
	await refreshPetState()
	void RUNTIME.writeLog("info", "主窗口 Main 挂载完成")
})
</script>

<template>
	<div class="main-window">
		<TitleBar>
			<div class="nav-title-chip">
				<Icon :name="currentNav?.icon || 'noriOS'" :size="13" class="nav-chip-icon"/>
				<span class="nav-chip-label">{{ currentNav?.label }}</span>
			</div>

			<div class="titlebar-right">
				<button class="win-btn" title="最小化" @click="minimizeMain">
					<Icon name="minus" :size="14"/>
				</button>
				<button class="win-btn close-btn" title="退出应用" @click="closeWindow">
					<Icon name="close" class="close-icon"/>
				</button>
			</div>
		</TitleBar>

		<div class="body">
			<aside class="sidebar">
				<div class="sidebar-nav">
					<button
						v-for="item in NAV_ITEMS"
						:key="item.key"
						class="nav-item"
						:class="{active: item.key === activeNav}"
						@click="activeNav = item.key"
					>
						<span class="active-glow-bar"/>
						<div class="nav-icon-wrap">
							<Icon :name="item.icon" :size="17"/>
						</div>
						<span class="nav-label">{{ item.label }}</span>
					</button>
				</div>
			</aside>

			<main class="content" :class="{compact: activeNav === 'home'}">
				<Transition name="tab-fade" mode="out-in">
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

					<!-- 全功能设置面板 -->
					<SettingsPanel v-else-if="activeNav === 'settings'"/>

					<!-- 声明 -->
					<section v-else-if="activeNav === 'about'" class="about-panel">
						<div class="about-card">
							<div class="about-icon-header">
								<Icon name="sparkles" :size="32" class="about-sparkle"/>
							</div>
							<h2 class="about-title glow-teal">{{ I18N.about.title }}</h2>
							<div class="about-badge-row">
								<span class="about-pill">{{ I18N.about.license }}</span>
								<span class="about-pill authors">{{ I18N.about.authors }}</span>
							</div>
							<p class="about-desc">{{ I18N.about.desc }}</p>
						</div>
					</section>
				</Transition>
			</main>
		</div>

		<!-- 底部状态与操作栏 -->
		<div class="footer">
			<div class="footer-left">
				<div class="pet-status-chip" :class="{online: petVisible}">
					<span class="status-pulse-dot"/>
					<span class="status-text">桌宠状态: {{ petVisible ? '桌面上已唤出' : '待命休眠中' }}</span>
					<span class="status-model-name">({{ selectedModelName }})</span>
				</div>
			</div>

			<div class="footer-right">
				<button class="btn-primary btn-summon" @click="togglePet">
					<Icon :name="petVisible ? 'close' : 'sparkles'" :size="15"/>
					<span>{{ petVisible ? I18N.hidePet : I18N.summonPet }}</span>
				</button>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.main-window {
	width: 100vw;
	height: 100vh;
	background: radial-gradient(80rem 50rem at 100% 0%, rgba(94, 234, 212, 0.08) 0%, transparent 60%),
		linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	border-radius: var(--radius-lg);
	display: flex;
	flex-direction: column;
	overflow: hidden;
	user-select: none;
	box-shadow: 0 1.2rem 3.6rem rgba(0, 0, 0, 0.65), inset 0 0 0 0.1rem var(--line-subtle);
}

.nav-title-chip {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.3rem 0.9rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	font-size: 1.2rem;
	color: var(--text-primary);

	.nav-chip-icon {
		color: var(--nori-teal-bright);
	}
}

.titlebar-right {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.win-btn {
	width: 2.8rem;
	height: 2.8rem;
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
		background-color: rgba(251, 60, 68, 0.18);
		color: var(--danger);
	}

	&:active {
		transform: scale(0.92);
	}
}

.body {
	flex: 1;
	display: flex;
	min-height: 0;
}

.sidebar {
	width: 15rem;
	padding: 1.2rem 0.8rem;
	display: flex;
	flex-direction: column;
	justify-content: space-between;
	border-right: 0.1rem solid var(--line-subtle);
	background: rgba(5, 14, 26, 0.4);
	backdrop-filter: blur(1rem);
}

.sidebar-nav {
	display: flex;
	flex-direction: column;
	gap: 0.5rem;
}

.nav-item {
	position: relative;
	display: flex;
	align-items: center;
	gap: 1rem;
	padding: 0.95rem 1.2rem;
	border: 0.1rem solid transparent;
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-family: inherit;
	font-size: 1.3rem;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	overflow: hidden;

	.active-glow-bar {
		position: absolute;
		left: 0;
		top: 0.6rem;
		bottom: 0.6rem;
		width: 0.35rem;
		border-radius: 0.2rem;
		background: var(--nori-teal-bright);
		opacity: 0;
		transform: scaleY(0.4);
		transition: all 0.2s ease;
		box-shadow: 0 0 0.8rem var(--glow-teal);
	}

	.nav-icon-wrap {
		display: flex;
		align-items: center;
		justify-content: center;
		transition: transform 0.2s ease;
	}

	&:hover {
		background: rgba(125, 227, 255, 0.06);
		color: var(--text-primary);

		.nav-icon-wrap {
			transform: scale(1.1);
			color: var(--nori-teal-soft);
		}
	}

	&.active {
		background: rgba(125, 227, 255, 0.12);
		border-color: rgba(125, 227, 255, 0.2);
		color: var(--nori-teal-bright);
		font-weight: 600;

		.active-glow-bar {
			opacity: 1;
			transform: scaleY(1);
		}

		.nav-icon-wrap {
			color: var(--nori-teal-bright);
		}
	}
}

.content {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: stretch;
	justify-content: flex-start;
	padding: 1.6rem 2rem;
	overflow: hidden;
	min-height: 0;
}

// 标签过渡
.tab-fade-enter-active,
.tab-fade-leave-active {
	transition: opacity 0.2s ease, transform 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.tab-fade-enter-from {
	opacity: 0;
	transform: translateY(0.8rem);
}

.tab-fade-leave-to {
	opacity: 0;
	transform: translateY(-0.8rem);
}

// 声明面板
.about-panel {
	width: 100%;
	height: 100%;
	display: flex;
	align-items: center;
	justify-content: center;
}

.about-card {
	width: 100%;
	max-width: 48rem;
	padding: 2.8rem 2.4rem;
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-lg);
	backdrop-filter: blur(1.2rem);
	box-shadow: 0 0.8rem 2.8rem rgba(0, 0, 0, 0.35);
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 1.4rem;
	text-align: center;
}

.about-sparkle {
	color: var(--nori-teal-bright);
	filter: drop-shadow(0 0 1.2rem var(--glow-teal));
}

.about-title {
	font-size: 2.4rem;
	font-weight: 700;
}

.about-badge-row {
	display: flex;
	gap: 0.8rem;
	flex-wrap: wrap;
	justify-content: center;
}

.about-pill {
	padding: 0.35rem 1rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	font-size: 1.15rem;
	color: var(--nori-teal-soft);

	&.authors {
		background: rgba(255, 255, 255, 0.05);
		color: var(--text-body);
	}
}

.about-desc {
	font-size: 1.25rem;
	color: var(--text-muted);
	line-height: 1.6;
	max-width: 36rem;
}

// 底部控制栏
.footer {
	padding: 0.8rem 1.6rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	border-top: 0.1rem solid var(--line-subtle);
	background: rgba(5, 14, 26, 0.6);
	backdrop-filter: blur(1rem);
	flex-shrink: 0;
}

.footer-left {
	display: flex;
	align-items: center;
}

.pet-status-chip {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.35rem 0.9rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	font-size: 1.15rem;
	color: var(--text-muted);

	.status-pulse-dot {
		width: 0.6rem;
		height: 0.6rem;
		border-radius: 50%;
		background: #7a8c9e;
		transition: all 0.3s ease;
	}

	.status-model-name {
		color: var(--text-faint);
		font-family: monospace;
	}

	&.online {
		background: rgba(32, 224, 144, 0.08);
		border-color: rgba(32, 224, 144, 0.25);
		color: #20e090;

		.status-pulse-dot {
			background: #20e090;
			box-shadow: 0 0 0.8rem #20e090;
		}

		.status-model-name {
			color: var(--nori-teal-soft);
		}
	}
}

.btn-summon {
	padding: 0.75rem 1.8rem;
	font-size: 1.25rem;
}
</style>

