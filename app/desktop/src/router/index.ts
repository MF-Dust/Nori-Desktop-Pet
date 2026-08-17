import {createRouter, createWebHashHistory} from "vue-router"
import InitView from "../views/InitView.vue"
import FirstRunView from "../views/FirstRunView.vue"
import PetView from "../views/PetView.vue"

const router = createRouter({
	history: createWebHashHistory(),
	routes: [
		{
			path: "/",
			redirect: "/init"
		},
		{
			path: "/init",
			name: "init",
			component: InitView
		},
		{
			path: "/first-run",
			name: "first-run",
			component: FirstRunView
		},
		{
			path: "/pet",
			name: "pet",
			component: PetView
		}
	]
})

export default router
