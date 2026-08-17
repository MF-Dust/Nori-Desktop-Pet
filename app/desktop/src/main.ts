import {createApp} from "vue"
import App from "./App.vue"
import router from "./services/router"
import useLanguage, {i18n} from "./services/i18n"
import "./assets/style/theme.less"

const APP = createApp(App)

await useLanguage.init()

APP.use(router)
APP.use(i18n)

APP.mount("#app")
