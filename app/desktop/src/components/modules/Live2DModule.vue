<script setup lang="ts">
import {computed, onMounted} from "vue"
import {invoke} from "@tauri-apps/api/core"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {i18n} from "../../services/i18n"
import Live2DCanvas from "../live2d/Live2DCanvas.vue"
import {selectedModel} from "../../services/store/selectedModel"
import {live2dController} from "../../services/live2d"

const I18N = computed(() => useLanguages().components.main.live2d)

const modelName = computed(() => {
	const ENTRY = live2dController.getLoadedModel() ?? selectedModel.value
	return ENTRY
})

onMounted(async () => {
	try {
		await invoke("write_log", {level: "info", message: i18n.global.t("log.firstRun.l2dModuleEnter", {model: selectedModel.value})})
	} catch {
	}
})
</script>

<template>
	<section class="l2d-module">
		<header class="mod-head">
			<h2 class="mod-title glow-teal">{{ I18N.title }}</h2>
			<span class="mod-model">{{ modelName }}</span>
		</header>
		<div class="mod-stage">
			<Live2DCanvas :model-id="selectedModel"/>
		</div>
	</section>
</template>

<style scoped lang="less">
.l2d-module {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	min-height: 0;
}

.mod-head {
	display: flex;
	align-items: baseline;
	gap: 1.2rem;
	padding: 0 0.4rem;
	flex-shrink: 0;
}

.mod-title {
	font-size: 1.6rem;
	font-weight: 600;
	color: var(--text-primary);
}

.mod-model {
	font-size: 1.2rem;
	color: var(--text-muted);
	font-variant-numeric: tabular-nums;
}

.mod-stage {
	flex: 1;
	min-height: 0;
	width: 100%;
	display: flex;
}
</style>
