<script setup lang="ts">
import {onMounted} from "vue"
import {useRouter} from "vue-router"
import {getCurrentWebviewWindow} from "@tauri-apps/api/webviewWindow"
import {invoke} from "@tauri-apps/api/core"

const ROUTER = useRouter()

// 窗口 label → 页面路由: 各窗口挂载后跳到自己的页面
const NAVIGATION: Record<string, string> = {
	"first-run": "/first-run",
	"init": "/init",
}

onMounted(async () => {
	// 窗口导航: 按当前窗口 label 跳转到对应页面(纯浏览器调试时跳过)
	try {
		const LABEL = getCurrentWebviewWindow().label
		const TARGET = NAVIGATION[LABEL]
		if (TARGET && ROUTER.currentRoute.value.path !== TARGET) {
			await ROUTER.replace(TARGET)
		}
		await invoke("write_log", {level: "info", message: `窗口 ${LABEL} 已挂载, 跳转到 ${TARGET}`})
	} catch {
		// 非 Tauri 环境(纯 vite 调试)忽略
	}
})
</script>

<template>
	<router-view/>
</template>
