<script setup lang="ts">
import {onMounted} from "vue"
import {useRouter} from "vue-router"
import {getCurrentWindowLabel, navigateToOwnWindow} from "./services/window"
import {RUNTIME} from "./services/runtime"
import useLanguage from "./services/i18n"
import {naiveDarkTheme, naiveThemeOverrides} from "./assets/style/naiveTheme"
import FeedbackHost from "./components/ui/FeedbackHost.vue"

const ROUTER = useRouter()

onMounted(async () => {
	// 窗口导航: 按当前窗口 label 跳转到对应页面 (纯浏览器调试时跳过)
	try {
		const LABEL = await getCurrentWindowLabel()
		const TARGET = await navigateToOwnWindow(ROUTER)
		await RUNTIME.writeLog("info", `窗口 ${LABEL} 已挂载, 跳转到 ${TARGET}`)
	} catch {
		// 非宿主环境(纯 vite 调试)忽略
	}
})

// 语言在任何窗口被改都会广播 state-changed, 这里跟随快照重放, 避免多窗口语言不一致
RUNTIME.onLanguageChanged((language) => {
	void useLanguage.setLanguage(language)
})
</script>

<template>
	<NConfigProvider :theme="naiveDarkTheme" :theme-overrides="naiveThemeOverrides">
		<NMessageProvider>
			<NDialogProvider>
				<NNotificationProvider>
					<FeedbackHost/>
					<RouterView/>
				</NNotificationProvider>
			</NDialogProvider>
		</NMessageProvider>
	</NConfigProvider>
</template>

