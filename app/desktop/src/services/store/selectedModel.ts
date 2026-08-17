import {ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {emit} from "@tauri-apps/api/event"
import {i18n} from "../i18n"
import {DEFAULT_MODEL, MODEL_CONFIG_KEY} from "../live2d/models"

const t = i18n.global.t

export const selectedModel = ref<string>(DEFAULT_MODEL)

export async function loadSelectedModel(): Promise<void> {
	try {
		const SAVED = await invoke<string | null>("get_config", {key: MODEL_CONFIG_KEY})
		if (SAVED) {
			selectedModel.value = SAVED
		}
	} catch (error) {
		console.error(t("log.store.modelConfigReadFailed", {error: String(error)}))
	}
}

export async function setSelectedModel(id: string): Promise<void> {
	if (id === selectedModel.value) return
	selectedModel.value = id
	try {
		await invoke("set_config", {key: MODEL_CONFIG_KEY, value: id})
		await invoke("write_log", {level: "info", message: t("log.model.switch", {id})})
		await emit("model-changed", {model: id})
	} catch (error) {
		console.error(t("log.store.modelConfigSaveFailed", {error: String(error)}))
	}
}
