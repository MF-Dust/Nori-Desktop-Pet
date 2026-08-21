<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import {listen, type UnlistenFn} from "../services/host/event"
import {getCurrentWindow} from "../services/host/window"
import useLanguages from "../services/i18n/useLanguages.ts"
import {closeWindow, showWindow} from "../services/window"
import TitleBar from "../components/TitleBar.vue"
import Icon from "../components/Icon.vue"
import logo from "../assets/images/logo.png"

const I18N = computed(() => useLanguages().views.init)

// 状态文本
const statusText = ref(I18N.value.title)

// 配置键名
const CONFIG_KEY = "selected_model"

// 模型名
const modelName = ref("arg-nori")

// 关闭窗口
const closeApp = () => {
	invoke("exit_app")
}

let unlistenInitStart: UnlistenFn | null = null

onBeforeUnmount(() => {
	if (unlistenInitStart) unlistenInitStart()
})

// 初始化流程: 检查 Live2D 资源, 完成后打开主窗口并关闭 init 窗口
const startInitFlow = async () => {
	try {
		const INSTALLED = await invoke<boolean>("check_resource", {resourceType: "live2d", name: modelName.value})
		if (!INSTALLED) {
			await invoke("write_log", {level: "warn", message: `模型 ${modelName.value} 未安装，请导入本地模型`})
		}
	} catch (error) {
		console.error("检查资源失败:", error)
	}

	// 初始化完成: 打开主窗口
	const sleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms))
	await showWindow("main")
	await sleep(600)
	await closeWindow("init")
}

// 当前窗口是否可见 (非 Tauri 环境视为可见, 保持原行为)
const isWindowVisible = async (): Promise<boolean> => {
	try {
		return await getCurrentWindow().isVisible()
	} catch {
		return true
	}
}

onMounted(async () => {
	// 读取 Live2D 模型名
	statusText.value = I18N.value.live2d
	try {
		const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
		if (typeof SAVED === "string" && SAVED.trim().length > 0) {
			modelName.value = SAVED.trim()
		}
	} catch (error) {
		console.error("读取模型配置失败:", error)
	}
	// 首次运行路径下 init 窗口隐藏启动: 若直接执行会在引导页旁弹出主窗口,
	// 因此等待 Rust 在向导完成后 emit 事件 (nori:init-start) 再执行
	if (await isWindowVisible()) {
		await startInitFlow()
		return
	}
	unlistenInitStart = await listen("nori:init-start", () => {
		void startInitFlow()
	})
})
</script>

<template>
	<div class="init-window">
		<TitleBar>
			<button class="close-btn" title="关闭" @click="closeApp">
				<Icon name="close" class="close-icon"/>
			</button>
		</TitleBar>

		<div class="body">
			<div class="avatar-stage">
				<div class="avatar-ring-outer"></div>
				<div class="avatar-ring-inner"></div>
				<img class="avatar" :src="logo" alt="Nori"/>
			</div>

			<div class="status-pill">
				<Icon name="loading" class="spin status-icon" :size="13"/>
				<span class="status-text">{{ statusText }}</span>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.init-window {
	width: 100vw;
	height: 100vh;
	background: radial-gradient(40rem 26rem at 50% 45%, rgba(94, 234, 212, 0.16) 0%, transparent 68%),
		linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	border-radius: var(--radius-lg);
	box-shadow: 0 1.2rem 3.6rem rgba(0, 0, 0, 0.65), inset 0 0 0 0.1rem var(--line-subtle);
	display: flex;
	flex-direction: column;
	overflow: hidden;
	user-select: none;
}

.body {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 2.2rem;
	padding-bottom: 2rem;
}

.avatar-stage {
	position: relative;
	width: 13rem;
	height: 13rem;
	display: flex;
	align-items: center;
	justify-content: center;
}

.avatar-ring-outer {
	position: absolute;
	inset: 0;
	border-radius: 50%;
	border: 0.1rem dashed rgba(125, 227, 255, 0.35);
	animation: rotate 12s linear infinite;
}

.avatar-ring-inner {
	position: absolute;
	inset: 1rem;
	border-radius: 50%;
	background: radial-gradient(circle, rgba(125, 227, 255, 0.2) 0%, rgba(94, 234, 212, 0.05) 50%, transparent 70%);
	animation: glow-pulse 2.5s ease-in-out infinite;
}

.avatar {
	width: 7.6rem;
	height: 7.6rem;
	object-fit: contain;
	animation: breathe 2.4s ease-in-out infinite;
	position: relative;
	z-index: 1;
}

.status-pill {
	display: inline-flex;
	align-items: center;
	gap: 0.8rem;
	padding: 0.5rem 1.4rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	backdrop-filter: blur(0.8rem);
	box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.25);
}

.status-icon {
	color: var(--nori-teal-bright);
}

.status-text {
	color: var(--text-primary);
	font-size: 1.25rem;
	font-weight: 500;
	letter-spacing: 0.02rem;
}

.close-btn {
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
		background-color: rgba(251, 60, 68, 0.18);
		color: var(--danger);
	}
}

.close-icon {
	width: 1.4rem;
	height: 1.4rem;
}
</style>

