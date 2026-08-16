import {createRouter, createWebHashHistory} from "vue-router"
import InitView from "../views/InitView.vue"
import FirstRunView from "../views/FirstRunView.vue"

const router = createRouter({
	history: createWebHashHistory(),
	routes: [
		{
			path: "/",
			name: "init",
			component: InitView
		},
		{
			path: "/first-run",
			name: "first-run",
			component: FirstRunView
		}
	]
})

export default router
