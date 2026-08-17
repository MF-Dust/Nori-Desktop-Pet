import type {Component} from "vue"
import type {IconName} from "../icon"

export type NavLabel = "settings"

export interface NavModule {
	id: string
	icon: IconName
	label: NavLabel
	loader: () => Promise<{default: Component}>
	order: number
}

export const MODULES: NavModule[] = [
	{
		id: "settings",
		icon: "settings",
		label: "settings",
		loader: () => import("../../components/modules/SettingsModule.vue"),
		order: 90,
	},
]

export function getModules(): NavModule[] {
	return [...MODULES].sort((a, b) => a.order - b.order)
}
