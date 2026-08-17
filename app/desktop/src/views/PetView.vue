<script setup lang="ts">
import {onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWebviewWindow} from "@tauri-apps/api/webviewWindow"
import {listen} from "@tauri-apps/api/event"
import PetLive2D from "../components/pet/PetLive2D.vue"
import {selectedModel, loadSelectedModel} from "../services/store/selectedModel"
import {live2dController} from "../services/live2d"
import {i18n} from "../services/i18n"

const modelId = ref(selectedModel.value)

let unlisten: (() => void) | null = null

onMounted(async () => {
	await loadSelectedModel()
	modelId.value = selectedModel.value
	try {
		const LABEL = getCurrentWebviewWindow().label
		await invoke("write_log", {level: "info", message: i18n.global.t("log.pet.mounted", {label: LABEL, model: selectedModel.value})})
	} catch {
	}
	unlisten = await listen<{model: string}>("model-changed", (event) => {
		selectedModel.value = event.payload.model
		modelId.value = event.payload.model
	})
})

onBeforeUnmount(async () => {
	unlisten?.()
	unlisten = null
	live2dController.stopIdle()
	live2dController.disableMouseFollow()
	await live2dController.unmount()
	try {
		await invoke("write_log", {level: "info", message: i18n.global.t("log.pet.unmounted")})
	} catch {
	}
})
</script>

<template>
	<div class="pet-window">
		<PetLive2D :model-id="modelId"/>
	</div>
</template>

<style scoped lang="less">
.pet-window {
	width: 100%;
	height: 100%;
	background: transparent;
	overflow: hidden;
}
</style>
