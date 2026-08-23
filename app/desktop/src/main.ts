import {createApp} from "vue"
import App from "./App.vue"
import router from "./services/router"
import useLanguage, {i18n} from "./services/i18n"
import {installErrorHandlers} from "./services/error"
// 顺序重要: 先基座与令牌, 再原子类 (同名声明时原子类赢)
import "./assets/style/theme.less"
import "virtual:uno.css"

const APP = createApp(App)

// 尽早安装, 让初始化阶段的错误也能落日志
installErrorHandlers(APP)

// 引导后端运行时 (拉取快照 + 订阅广播), 用持久化语言初始化 i18n
try {
	const {RUNTIME} = await import("./services/runtime")
	await RUNTIME.init()
	await useLanguage.init(RUNTIME.snapshot.value?.general.language)
} catch {
	// 非宿主环境 (纯 vite 调试) 回退系统语言
	await useLanguage.init()
}

APP.use(router)
APP.use(i18n)

APP.mount("#app")
