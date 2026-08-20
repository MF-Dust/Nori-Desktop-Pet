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
			<img class="avatar" :src="logo" alt="Nori"/>
			<div class="status">{{ statusText }}</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.init-window {
	width: 100vw;
	height: 100vh;
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	border-radius: var(--radius-lg);
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
	gap: 1.8rem;
	padding-bottom: 2rem;
}

.avatar {
	width: 7.2rem;
	height: 7.2rem;
	object-fit: contain;
	animation: breathe 2.2s ease-in-out infinite;
}

.status {
	color: var(--text-body);
	font-size: 1.3rem;
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

	&:hover {
		background-color: rgba(255, 255, 255, 0.08);
		color: var(--danger);
	}
}

.close-icon {
	width: 1.4rem;
	height: 1.4rem;
}
</style>
