<script setup lang="ts">
import {onMounted} from "vue"
import {useRouter} from "vue-router"
import {invoke} from "./services/host/invoke"
import {getCurrentWindowLabel, navigateToOwnWindow} from "./services/window"

const ROUTER = useRouter()

onMounted(async () => {
	// 窗口导航: 按当前窗口 label 跳转到对应页面 (纯浏览器调试时跳过)
	try {
		const LABEL = await getCurrentWindowLabel()
		const TARGET = await navigateToOwnWindow(ROUTER)
		await invoke("write_log", {level: "info", message: `窗口 ${LABEL} 已挂载, 跳转到 ${TARGET}`})
	} catch {
		// 非 Tauri 环境(纯 vite 调试)忽略
	}
})
</script>

<template>
	<RouterView/>
</template>
