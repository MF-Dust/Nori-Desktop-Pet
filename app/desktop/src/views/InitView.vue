<script setup lang="ts">
import {onMounted, ref} from "vue"
import logo from "../assets/images/logo.png"
import TitleBar from "../components/TitleBar.vue"

const status = ref("正在初始化...")
const progress = ref(0)

onMounted(() => {
})
</script>

<template>
	<div class="init-window">
		<TitleBar/>

		<div class="body">
			<img class="avatar" :src="logo" alt="Nori"/>
			<div class="status">{{ status }}</div>
			<div class="progress-track" v-if="progress > 0">
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

.progress-track {
	width: 20rem;
	height: 0.4rem;
	border-radius: 0.2rem;
	background: rgba(255, 255, 255, 0.08);
	overflow: hidden;
}

.progress-bar {
	height: 100%;
	border-radius: 0.2rem;
	background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	transition: width 0.4s ease;
}
</style>
