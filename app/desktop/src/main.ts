import {createApp} from "vue"
import App from "./App.vue"
import router from "./router"
import "./assets/style/theme.less"

const APP = createApp(App)

APP.use(router)

APP.mount("#app")
