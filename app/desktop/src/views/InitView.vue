<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import useLanguages from "../services/i18n/useLanguages.ts"
import {createResourceDownload, formatBytes} from "../services/resourceDownload"
import {closeWindow, showWindow} from "../services/window"
import TitleBar from "../components/TitleBar.vue"
import ProgressBar from "../components/ProgressBar.vue"
import Icon from "../components/Icon.vue"
import logo from "../assets/images/logo.png"

// 当前初始化的是 Live2D 资源 (模型)
const RESOURCE_TYPE = "live2d"

const I18N = computed(() => useLanguages().views.init)

// 通用资源下载控制器
const DOWNLOAD = createResourceDownload()

// 状态文本
const statusText = ref(I18N.value.title)

// 配置键名
const CONFIG_KEY = "selected_model"

// 模型名
const modelName = ref("arg-nori")

// 下载状态文本: 由下载/检查阶段映射
const downloadStatusText = computed(() => {
	switch (DOWNLOAD.state.step) {
		case "downloading":
			return I18N.value.downloading
		case "download-done":
			return I18N.value.downloadDone
		case "extracting":
			return I18N.value.extracting
		case "done":
			return I18N.value.ready
		case "installed":
			return I18N.value.installed
		case "error":
			return DOWNLOAD.state.message || I18N.value.downloadFailed
		default:
			return I18N.value.check
	}
})

// 进度明细文案 (仅下载中显示字节)
const progressText = computed(() =>
	DOWNLOAD.state.step === "downloading" ? DOWNLOAD.state.total ? `${formatBytes(DOWNLOAD.state.downloaded ?? 0)} / ${formatBytes(DOWNLOAD.state.total)}` : DOWNLOAD.state.downloaded != null? formatBytes(DOWNLOAD.state.downloaded) : "" : ""
)

// 进度条显示
const showProgress = computed(() => DOWNLOAD.state.step === "downloading")

// 关闭窗口
const closeApp = () => {
	invoke("exit_app")
}

onBeforeUnmount(() => {
	DOWNLOAD.stop()
})

onMounted(async () => {
	// 读取 Live2D 模型名
	statusText.value = I18N.value.live2d
	try {
		const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
		if (SAVED) modelName.value = SAVED
	} catch (error) {
		console.error("读取模型配置失败:", error)
	}
	// 检查是否已安装, 未安装则触发下载+解压
	const INSTALLED = await DOWNLOAD.check(RESOURCE_TYPE, modelName.value)
	if (!INSTALLED) await DOWNLOAD.ensure(RESOURCE_TYPE, modelName.value)
	// 初始化完成: 先打开桌宠窗口, 延迟后再关闭 init 窗口 (避免窗口销毁打断后续逻辑)
	const sleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms))
	await showWindow("pet")
	await sleep(600)
	await closeWindow("init")
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
			<ProgressBar v-if="showProgress" :percent="DOWNLOAD.state.percent" :text="progressText"/>
			<div class="download-status">{{ downloadStatusText }}</div>
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

.download-status {
	color: var(--text-faint);
	font-size: 1.1rem;
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
