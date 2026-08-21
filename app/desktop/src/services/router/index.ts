import {createRouter, createWebHashHistory} from "vue-router"

const router = createRouter({
	history: createWebHashHistory(),
	routes: [
		{
			path: "/",
			redirect: "/init"
		},
		{
			path: "/first-run",
			name: "first-run",
			component: () => import("../../views/FirstRunView.vue")
		},
		{
			path: "/init",
			name: "init",
			component: () => import("../../views/InitView.vue")
		},
		{
			path: "/main",
			name: "main",
			component: () => import("../../views/Main.vue")
		}
	]
})

export default router
