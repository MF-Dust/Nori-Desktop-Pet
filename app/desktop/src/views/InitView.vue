<script setup lang="ts">
import {computed, defineAsyncComponent, onMounted, ref, type Component} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWebviewWindow} from "@tauri-apps/api/webviewWindow"
import logo from "../assets/images/logo.png"
import useLanguages from "../services/i18n/useLanguages.ts"
import {i18n} from "../services/i18n"
import Icon from "../components/Icon.vue"
import {getModules} from "../services/modules"
import {loadSelectedModel} from "../services/store/selectedModel"

const I18N = computed(() => useLanguages().views.main)

const MODULES = getModules()

const COMPONENTS = MODULES.reduce((acc, m) => {
	acc[m.id] = defineAsyncComponent(m.loader)
	return acc
}, {} as Record<string, Component>)

const active = ref<string | null>(null)

const collapsed = ref(false)

const NAV = computed(() => I18N.value.nav)

const activeComponent = computed<Component | null>(() => {
	return active.value ? COMPONENTS[active.value] ?? null : null
})

const select = (id: string) => {
	active.value = id
	void invoke("write_log", {level: "info", message: i18n.global.t("log.main.moduleSwitch", {id})}).catch(() => {})
}

const toggleCollapse = () => {
	collapsed.value = !collapsed.value
	void invoke("write_log", {
		level: "info",
		message: i18n.global.t(collapsed.value ? "log.main.navCollapse" : "log.main.navExpand"),
	}).catch(() => {})
}

const minimizeWindow = async () => {
	try {
		await getCurrentWebviewWindow().minimize()
	} catch {
	}
}

const closeWindow = async () => {
	try {
		await getCurrentWebviewWindow().close()
	} catch {
	}
}

onMounted(async () => {
	await loadSelectedModel()
	try {
		const LABEL = getCurrentWebviewWindow().label
		await invoke("write_log", {level: "info", message: i18n.global.t("log.main.mounted", {label: LABEL})})
	} catch {
	}
})
</script>

<template>
	<div class="main-window">
		<aside class="nav" :class="{collapsed}">
			<div class="nav-top" data-tauri-drag-region>
				<button class="logo-btn" :title="collapsed ? I18N.expand : I18N.collapse" @click="toggleCollapse">
					<img class="logo" :src="logo" alt="Nori"/>
				</button>
			</div>
			<nav class="nav-list">
				<button
					v-for="m in MODULES"
					:key="m.id"
					class="nav-item"
					:class="{active: active === m.id, collapsed}"
					:title="NAV[m.label]"
					@click="select(m.id)"
				>
					<icon class="nav-icon" :name="m.icon"/>
					<span v-if="!collapsed" class="nav-label">{{ NAV[m.label] }}</span>
				</button>
			</nav>
			<div class="nav-bottom" data-tauri-drag-region/>
		</aside>

		<main class="content">
			<div class="content-top" data-tauri-drag-region>
				<div class="window-controls">
					<button class="win-btn" :title="I18N.minimize" @click="minimizeWindow">
						<icon name="minus" :size="16"/>
					</button>
					<button class="win-btn win-close" :title="I18N.close" @click="closeWindow">
						<icon name="close" :size="16"/>
					</button>
				</div>
			</div>
			<div class="content-body">
				<div v-if="!active" class="empty">
					<icon class="empty-icon" name="panel" :size="48"/>
					<p class="empty-text">{{ I18N.empty }}</p>
				</div>
				<Suspense v-else>
					<component :is="activeComponent"/>
					<template #fallback>
						<div class="loading">
							<icon name="loading" :size="28"/>
						</div>
					</template>
				</Suspense>
			</div>
		</main>
	</div>
</template>

<style scoped lang="less">
.main-window {
	width: 100%;
	height: 100%;
	display: flex;
	border-radius: var(--radius-lg);
	overflow: hidden;
	user-select: none;
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	color: var(--text-body);
}

.nav {
	width: 22rem;
	flex-shrink: 0;
	display: flex;
	flex-direction: column;
	border-right: 0.1rem solid var(--line-subtle);
	background-color: rgba(5, 14, 26, 0.5);
	transition: width 0.28s cubic-bezier(0.4, 0, 0.2, 1);

	&.collapsed {
		width: 6.4rem;
	}
}

.nav-top {
	height: 5.2rem;
	display: flex;
	align-items: center;
	padding: 0 1.2rem;
	flex-shrink: 0;
}

.logo-btn {
	width: 4rem;
	height: 4rem;
	border: none;
	border-radius: 50%;
	background-color: transparent;
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s ease;

	&:hover {
		background-color: rgba(125, 227, 255, 0.08);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.logo {
	width: 3.2rem;
	height: 3.2rem;
	object-fit: contain;
	animation: breathe 4s ease-in-out infinite;
}

.nav-list {
	flex: 1;
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
	padding: 0.8rem;
	min-height: 0;
	overflow-y: auto;
}

.nav-item {
	height: 4.4rem;
	display: flex;
	align-items: center;
	gap: 1.2rem;
	padding: 0 1.2rem;
	border: 0.1rem solid transparent;
	border-radius: var(--radius-sm);
	background-color: transparent;
	color: var(--text-muted);
	font-size: 1.4rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;

	:deep(.nav-icon) {
		width: 2.2rem;
		height: 2.2rem;
		flex-shrink: 0;
	}

	&.collapsed {
		justify-content: center;
		padding: 0;
	}

	&:hover {
		background-color: rgba(125, 227, 255, 0.06);
		color: var(--text-primary);
	}

	&.active {
		background-color: rgba(125, 227, 255, 0.12);
		border-color: var(--nori-teal-soft);
		color: var(--nori-teal-bright);
		box-shadow: 0 0 1rem var(--glow-teal-soft);
	}
}

.nav-label {
	white-space: nowrap;
	overflow: hidden;
	text-overflow: ellipsis;
}

.nav-bottom {
	height: 1.2rem;
	flex-shrink: 0;
}

.content {
	flex: 1;
	min-width: 0;
	display: flex;
	flex-direction: column;
	min-height: 0;
}

.content-top {
	height: 5.2rem;
	flex-shrink: 0;
	display: flex;
	justify-content: flex-end;
	align-items: center;
	padding: 0 0.8rem;
}

.window-controls {
	display: flex;
	gap: 0.4rem;
}

.win-btn {
	width: 3.2rem;
	height: 3.2rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: 0.1rem solid transparent;
	border-radius: var(--radius-sm);
	background-color: transparent;
	color: var(--text-muted);
	cursor: pointer;
	transition: all 0.15s ease;

	&:hover {
		background-color: rgba(255, 255, 255, 0.08);
		color: var(--text-primary);
	}

	&:active {
		transform: scale(0.92);
	}
}

.win-close {
	&:hover {
		background-color: rgba(255, 80, 80, 0.15);
		border-color: rgba(255, 80, 80, 0.3);
		color: #ff6b6b;
	}
}

.content-body {
	flex: 1;
	min-height: 0;
	padding: 0 2.4rem 2.4rem;
	display: flex;
	flex-direction: column;
}

.empty {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.6rem;
	color: var(--text-faint);

	.empty-icon {
		opacity: 0.4;
	}

	.empty-text {
		font-size: 1.3rem;
		letter-spacing: 0.04rem;
	}
}

.loading {
	flex: 1;
	display: flex;
	align-items: center;
	justify-content: center;
	color: var(--nori-teal-soft);
}
</style>
