import {createApp, watch} from "vue"
import App from "./App.vue"
import router from "./services/router"
import useLanguage, {i18n} from "./services/i18n"
import {installErrorHandlers} from "./services/error"
import {RUNTIME} from "./services/runtime"
import {SyncWebTelemetry} from "./services/telemetry"
// 顺序重要: 先基座与令牌, 再原子类 (同名声明时原子类赢)
import "./assets/style/theme.less"
import "virtual:uno.css"

const APP = createApp(App)

// Install before runtime bootstrap so host/locale initialization failures are observable.
installErrorHandlers(APP)

// 先引导后端运行时, 只有拿到快照并确认用户开关后才初始化 Web Sentry。
try {
	await RUNTIME.init()
	await SyncWebTelemetry(APP, router, RUNTIME.snapshot.value)
	await useLanguage.init(RUNTIME.snapshot.value?.general.language)
} catch {
	// 非宿主环境 (纯 vite 调试) 回退系统语言, 且不初始化遥测。
	await SyncWebTelemetry(APP, router, null)
	await useLanguage.init()
}

// 其他窗口或设置页修改遥测开关后, 通过快照广播即时启停 Web transport。
watch(RUNTIME.snapshot, (snapshot) => {
	void SyncWebTelemetry(APP, router, snapshot)
})

APP.use(router)
APP.use(i18n)

APP.mount("#app")
