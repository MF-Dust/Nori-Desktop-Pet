import {createApp} from "vue"
import App from "./App.vue"
import router from "./services/router"
import useLanguage, {i18n} from "./services/i18n"
import {installErrorHandlers} from "./services/error"
import "./assets/style/theme.less"

const APP = createApp(App)

// 尽早安装, 让初始化阶段的错误也能落日志
installErrorHandlers(APP)

await useLanguage.init()

APP.use(router)
APP.use(i18n)

APP.mount("#app")
