<script setup lang="ts">
import {computed, defineAsyncComponent, h, onBeforeUnmount, onErrorCaptured, onMounted, ref, watch} from "vue"
import useLanguages from "../services/i18n/useLanguages.ts"
import {RUNTIME} from "../services/runtime"
import TitleBar from "../components/TitleBar.vue"
import Icon from "../components/Icon.vue"
import AppChip from "../components/ui/AppChip.vue"
import AppButton from "../components/ui/AppButton.vue"
import AppSkeleton from "../components/ui/AppSkeleton.vue"
import type {IconName} from "../services/icon"
import {showWindow, hideWindow} from "../services/window"
import {MODEL_LIST} from "../services/live2d/models"
import {feedback} from "../services/feedback"
import {installAudioHost, uninstallAudioHost} from "../services/audio"

const I18N = computed(() => useLanguages().views.main)
const UI_I18N = computed(() => useLanguages().components.ui.state)
const PLATFORM_I18N = computed(() => useLanguages().views.main.platform)

// 平台能力: 托盘不可用时要在主窗内补一个常驻入口 (部分 Linux 桌面环境没有 StatusNotifier)
const PLATFORM = computed(() => RUNTIME.platform())
const DEGRADED_HINTS = computed(() => {
	const HINTS: string[] = []
	if (!PLATFORM.value.supportsTray) HINTS.push(PLATFORM_I18N.value.trayUnavailable)
	if (!PLATFORM.value.supportsHitThrough) HINTS.push(PLATFORM_I18N.value.hitThroughDegraded)
	if (!PLATFORM.value.supportsGlobalCursor) HINTS.push(PLATFORM_I18N.value.cursorDegraded)
	return HINTS
})

// 异步面板: 卡住/加载失败时给出可见状态, 而不是一片空白
const PANEL_LOADING = () => h(AppSkeleton, {rows: 4})
const PANEL_ERROR = () => h("div", {class: "flex flex-1 items-center justify-center gap-2 text-base text-danger-text"}, [
	h(Icon, {name: "info", size: 18}),
	h("span", UI_I18N.value.loadFailed),
])

const PANEL_OPTIONS = {
	loadingComponent: PANEL_LOADING,
	errorComponent: PANEL_ERROR,
	delay: 120,
	timeout: 15_000,
} as const

const HomePanel = defineAsyncComponent({loader: () => import("../components/home/HomePanel.vue"), ...PANEL_OPTIONS})
const SettingsPanel = defineAsyncComponent({loader: () => import("../components/settings/SettingsPanel.vue"), ...PANEL_OPTIONS})
const ModelManagement = defineAsyncComponent({loader: () => import("../components/settings/ModelManagement.vue"), ...PANEL_OPTIONS})
const ChatView = defineAsyncComponent({loader: () => import("../components/ChatView.vue"), ...PANEL_OPTIONS})

// ---- 侧边导航 (「关于」已并入设置的二级列表) ----
type NavKey = "home" | "talk" | "model" | "settings"

const aiConfigured = computed(() => RUNTIME.snapshot.value?.ai.configured ?? false)

const NAV_ITEMS = computed<{key: NavKey; label: string; icon: IconName; badge?: boolean}[]>(() => [
	{key: "home", label: I18N.value.nav.home, icon: "noriOS"},
	{key: "talk", label: I18N.value.nav.talk, icon: "send"},
	{key: "model", label: I18N.value.nav.model, icon: "package"},
	{key: "settings", label: I18N.value.nav.settings, icon: "settings", badge: !aiConfigured.value},
])

const activeNav = ref<NavKey>("home")
const currentNav = computed(() => NAV_ITEMS.value.find((item) => item.key === activeNav.value))

// 设置面板要打开的初始子页 (从主页磁贴跳过来时直达)
const settingsTarget = ref("")

// ---- 侧边栏折叠 (状态持久化在 general.sidebarCollapsed) ----
const collapsed = ref(false)
watch(() => RUNTIME.snapshot.value?.general.sidebarCollapsed, (value) => {
	if (typeof value === "boolean") collapsed.value = value
}, {immediate: true})

const toggleSidebar = async () => {
	collapsed.value = !collapsed.value
	try {
		await RUNTIME.updateGeneral({sidebarCollapsed: collapsed.value})
	} catch (error) {
		feedback.error(UI_I18N.value.saveFailed, error)
	}
}

// 窗口操作: 最小化主窗口 / 退出应用
const minimizeMain = async () => {
	await hideWindow("main")
}

const exitApp = () => {
	void RUNTIME.exitApp()
}

// 桌宠当前是否显示: 宿主快照是唯一真相 (托盘切换后会广播 state-changed, 不会陈旧)
const petVisible = computed(() => RUNTIME.snapshot.value?.pet.visible ?? false)
const selectedModelName = computed(() => {
	const MODEL_ID = RUNTIME.snapshot.value?.models.selected ?? "nori"
	return MODEL_LIST.find(model => model.id === MODEL_ID)?.name ?? MODEL_ID
})

// 召唤 / 收起桌宠
const togglePet = async () => {
	try {
		if (petVisible.value) {
			await hideWindow("pet")
			await RUNTIME.writeLog("info", "主窗口收起 Nori")
		} else {
			await showWindow("pet")
			await RUNTIME.writeLog("info", "主窗口唤出 Nori")
		}
	} catch (error) {
		feedback.error(UI_I18N.value.saveFailed, error)
	} finally {
		await RUNTIME.refresh()
	}
}

// 主页磁贴跳转
const navigate = (tab: "talk" | "model" | "settings") => {
	if (tab === "settings") settingsTarget.value = "ai"
	activeNav.value = tab
}

// 面板内未捕获异常不能拖崩整个主窗口
const panelError = ref("")
onErrorCaptured((error) => {
	panelError.value = error instanceof Error ? error.message : String(error)
	console.error("面板异常:", error)
	return false
})

watch(activeNav, () => {
	panelError.value = ""
})

onMounted(async () => {
	await RUNTIME.init()
	// 主窗口兼任音频宿主: TTS 播放与麦克风录音都在这里 (关窗只隐藏, 所以一直在线)
	await installAudioHost()
	void RUNTIME.writeLog("info", "主窗口 Main 挂载完成")
})

onBeforeUnmount(() => {
	uninstallAudioHost()
})
</script>

<template>
	<div
		class="w-100vw h-100vh flex flex-col overflow-hidden select-none rounded-lg
			bg-[radial-gradient(110rem_70rem_at_90%_0%,rgba(125,227,255,0.18)_0%,transparent_60%),radial-gradient(60rem_50rem_at_0%_100%,rgba(15,45,71,0.5)_0%,transparent_60%),linear-gradient(165deg,var(--bg-panel)_0%,var(--bg-deep)_45%,var(--bg-abyss)_100%)]
			shadow-[0_1.2rem_3.6rem_rgba(0,0,0,0.65),inset_0_0_0_0.1rem_var(--line-subtle)]"
	>
		<TitleBar>
			<div class="flex items-center gap-2 px-2.5 py-0.8 rounded-pill bg-white/4 border border-line-subtle text-xs text-text-faint font-500 backdrop-blur-[0.8rem]">
				<Icon :name="currentNav?.icon || 'noriOS'" :size="13" class="text-nori-teal-bright"/>
				<span class="text-text-muted">{{ currentNav?.label }}</span>
			</div>

			<div class="flex items-center gap-1.5">
				<button type="button" class="btn-icon" :title="I18N.footer.minimize" :aria-label="I18N.footer.minimize" @click="minimizeMain">
					<Icon name="minus" :size="13"/>
				</button>
				<button type="button" class="close-btn" :title="I18N.footer.exit" :aria-label="I18N.footer.exit" @click="exitApp">
					<Icon name="close" class="close-icon"/>
				</button>
			</div>
		</TitleBar>

		<div class="flex-1 flex min-h-0 relative">
			<!-- 侧边导航控制台 -->
			<aside
				class="shrink-0 flex flex-col justify-between gap-3 py-3 px-2.5 border-r border-line-subtle
					bg-bg-abyss/55 backdrop-blur-[1.4rem] transition-[width] duration-250 z-2"
				:class="collapsed ? 'w-[5.6rem]' : 'w-[15.5rem]'"
			>
				<nav class="flex flex-col gap-1.5" :aria-label="I18N.nav.home">
					<button
						v-for="item in NAV_ITEMS"
						:key="item.key"
						type="button"
						class="nav-item group"
						:class="[
							item.key === activeNav ? 'nav-item-active' : '',
							collapsed ? 'justify-center px-0' : '',
						]"
						:title="collapsed ? item.label : undefined"
						:aria-label="item.label"
						:aria-current="item.key === activeNav ? 'page' : undefined"
						@click="activeNav = item.key"
					>
						<span
							v-if="item.key === activeNav"
							class="absolute left-0 top-1.5 bottom-1.5 w-[0.35rem] rounded-pill bg-gradient-to-b from-nori-teal-bright to-nori-teal shadow-[0_0_1rem_var(--glow-teal)]"
						/>
						<span
							class="flex items-center justify-center shrink-0 transition-all duration-200"
							:class="item.key === activeNav ? 'text-nori-teal-bright scale-105' : 'group-hover:scale-110'"
						>
							<Icon :name="item.icon" :size="17"/>
						</span>
						<span v-if="!collapsed" class="truncate font-500">{{ item.label }}</span>
						<span
							v-if="item.badge"
							class="w-1.8 h-1.8 rounded-full bg-warning shadow-[0_0_0.8rem_var(--warning)] animate-pulse"
							:class="collapsed ? 'absolute top-1.5 right-1.5' : 'absolute right-3'"
						/>
					</button>
				</nav>

				<button
					type="button"
					class="nav-item justify-center text-text-faint hover:text-nori-teal-bright hover:bg-white/6"
					:title="collapsed ? I18N.sidebar.expand : I18N.sidebar.collapse"
					:aria-label="collapsed ? I18N.sidebar.expand : I18N.sidebar.collapse"
					@click="toggleSidebar"
				>
					<Icon :name="collapsed ? 'arrow-right' : 'arrow-left'" :size="14"/>
					<span v-if="!collapsed" class="text-xs font-500">{{ I18N.sidebar.collapse }}</span>
				</button>
			</aside>

			<!-- 主工作区 -->
			<main class="flex-1 min-h-0 flex flex-col items-stretch overflow-hidden px-5 py-4 relative">
				<p
					v-if="panelError"
					class="shrink-0 mb-2.5 px-3 py-1.5 rounded-sm text-sm text-danger-text bg-danger/12 border border-danger/28"
					role="alert"
				>{{ panelError }}</p>

				<Transition name="tab-fade" mode="out-in">
					<!-- 主页看板 -->
					<HomePanel
						v-if="activeNav === 'home'"
						:pet-visible="petVisible"
						@toggle-pet="togglePet"
						@navigate="navigate"
					/>

					<!-- 对话 -->
					<ChatView v-else-if="activeNav === 'talk'" @go-settings="navigate('settings')"/>

					<!-- 模型管理 -->
					<ModelManagement v-else-if="activeNav === 'model'"/>

					<!-- 全功能设置面板 (含关于) -->
					<SettingsPanel v-else :initial-tab="settingsTarget"/>
				</Transition>
			</main>
		</div>

		<!-- 底部状态与操作胶囊栏 -->
		<div class="relative shrink-0 flex flex-col gap-1.5 px-5 py-2.5 border-t border-line-subtle bg-bg-abyss/75 backdrop-blur-[1.4rem]">
			<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/20 to-transparent pointer-events-none"/>

			<!-- 平台降级提示: 没有托盘/穿透/全局光标时明确告知, 而不是静默失效 -->
			<p
				v-for="hint in DEGRADED_HINTS"
				:key="hint"
				class="m-0 inline-flex items-center gap-1.5 text-xs text-warning"
				role="note"
			>
				<Icon name="info" :size="12"/>
				<span>{{ hint }}</span>
			</p>

			<div class="flex items-center justify-between gap-3">
				<!-- 桌宠实时连接胶囊 -->
				<div class="flex items-center gap-2">
					<AppChip :tone="petVisible ? 'success' : 'neutral'" dot>
						<span>{{ I18N.footer.petLabel }}: {{ petVisible ? I18N.footer.petOnline : I18N.footer.petOffline }}</span>
						<span class="mono opacity-80">({{ selectedModelName }})</span>
					</AppChip>
				</div>

				<div class="flex items-center gap-2.5">
					<!-- 托盘不可用时的内建退出入口 -->
					<AppButton v-if="!PLATFORM.supportsTray" icon="power" @click="exitApp">
						{{ I18N.footer.exit }}
					</AppButton>
					<AppButton
						variant="primary"
						:icon="petVisible ? 'close' : 'sparkles'"
						class="shadow-[0_0.2rem_1.4rem_var(--glow-teal-soft)] hover:shadow-[0_0.4rem_2rem_var(--glow-teal)]"
						@click="togglePet"
					>
						{{ petVisible ? I18N.hidePet : I18N.summonPet }}
					</AppButton>
				</div>
			</div>
		</div>
	</div>
</template>
