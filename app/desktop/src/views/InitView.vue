<script setup lang="ts">
import {onMounted, ref} from "vue"
import logo from "../assets/images/logo.png"

const status = ref("正在初始化...")
const progress = ref(0)

onMounted(() => {
	const steps = ["正在加载 Nori 核心...", "正在连接语音服务...", "即将唤醒 Nori"]
	steps.forEach((text, i) => {
		setTimeout(() => {
			status.value = text
			progress.value = Math.round(((i + 1) / steps.length) * 100)
		}, 800 * (i + 1))
	})
})
</script>

<template>
	<div class="init-window">
		<div class="titlebar" data-tauri-drag-region>
			<span class="title" data-tauri-drag-region>Nori</span>
		</div>

		<div class="body">
			<img class="avatar" :src="logo" alt="Nori"/>
			<div class="status">{{ status }}</div>
			<div class="progress-track">
				<div class="progress-bar" :style="{width: progress + '%'}"/>
			</div>
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

.titlebar {
	height: 44px;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 12px 0 16px;
	flex-shrink: 0;
}

.title {
	color: var(--text-primary);
	font-size: 13px;
	font-weight: 600;
	letter-spacing: 0.5px;
}

.body {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 18px;
	padding-bottom: 20px;
}

.avatar {
	width: 72px;
	height: 72px;
	object-fit: contain;
	animation: breathe 2.2s ease-in-out infinite;
}

.status {
	color: var(--text-body);
	font-size: 13px;
}

.progress-track {
	width: 200px;
	height: 4px;
	border-radius: 2px;
	background: rgba(255, 255, 255, 0.08);
	overflow: hidden;
}

.progress-bar {
	height: 100%;
	border-radius: 2px;
	background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	transition: width 0.4s ease;
}

@keyframes breathe {
	0%, 100% {
		transform: scale(1);
	}
	50% {
		transform: scale(1.12);
	}
}
</style>
