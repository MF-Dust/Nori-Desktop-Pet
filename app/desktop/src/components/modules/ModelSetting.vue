<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {i18n} from "../../services/i18n"
import Icon from "../Icon.vue"
import {live2dController, MODEL_CATALOG, type Live2DModelEntry} from "../../services/live2d"
import {selectedModel, setSelectedModel} from "../../services/store/selectedModel"

const I18N = computed(() => useLanguages().components.main.settings.model)

const models = ref<Live2DModelEntry[]>([])

onMounted(async () => {
	let entries: Live2DModelEntry[] = MODEL_CATALOG.map(c => ({
		id: c.id,
		name: c.name,
		thumb: c.thumb,
		installed: false,
	}))
	try {
		entries = await live2dController.listModels()
	} catch (error) {
		console.error(i18n.global.t("log.settings.modelListFailed"), error)
		try {
			await invoke("write_log", {level: "warn", message: i18n.global.t("log.settings.modelListFailed")})
		} catch {
		}
	}
	for (const c of MODEL_CATALOG) {
		if (!entries.some(e => e.id === c.id)) {
			entries.push({id: c.id, name: c.name, thumb: c.thumb, installed: false})
		}
	}
	models.value = entries
})

const select = async (id: string) => {
	if (id === selectedModel.value) return
	await setSelectedModel(id)
	await live2dController.loadModel(id)
}
</script>

<template>
	<div class="setting-block">
		<div class="block-head">
			<h3 class="block-title">{{ I18N.title }}</h3>
			<p class="block-sub">{{ I18N.sub }}</p>
		</div>
		<div class="model-grid">
			<button
				v-for="m in models"
				:key="m.id"
				class="model-card"
				:class="{active: selectedModel === m.id}"
				@click="select(m.id)"
			>
				<span class="model-thumb-wrap">
					<img v-if="m.thumb" class="model-thumb" :src="m.thumb" :alt="m.name" draggable="false"/>
					<span v-else class="model-thumb model-thumb-empty">
						<icon name="cube" :size="40"/>
					</span>
					<span class="model-check"><icon name="check"/></span>
					<span class="model-install" :class="{installed: m.installed}">
						{{ m.installed ? I18N.installed : I18N.notInstalled }}
					</span>
				</span>
				<span class="model-name">{{ m.name }}</span>
			</button>
		</div>
	</div>
</template>

<style scoped lang="less">
.setting-block {
	display: flex;
	flex-direction: column;
	gap: 1rem;
}

.block-head {
	display: flex;
	flex-direction: column;
	gap: 0.2rem;
}

.block-title {
	font-size: 1.5rem;
	font-weight: 600;
	color: var(--text-primary);
}

.block-sub {
	font-size: 1.1rem;
	color: var(--text-muted);
	line-height: 1.5;
}

.model-grid {
	display: grid;
	grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
	gap: 1.2rem;
}

.model-card {
	padding: 1.2rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background-color: rgba(255, 255, 255, 0.03);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		background-color: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: var(--nori-teal);
		background-color: rgba(125, 227, 255, 0.1);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.model-thumb-wrap {
	position: relative;
	width: 100%;
	display: flex;
	align-items: center;
	justify-content: center;
}

.model-thumb {
	width: auto;
	height: 14rem;
	max-width: 100%;
	object-fit: contain;

	&.model-thumb-empty {
		height: 14rem;
		color: var(--text-muted);
		opacity: 0.4;
	}
}

.model-check {
	position: absolute;
	right: 0;
	top: 0;
	color: var(--nori-teal);
	opacity: 0;
	transition: opacity 0.2s ease;

	:deep(svg) {
		width: 1.8rem;
		height: 1.8rem;
	}

	.active & {
		opacity: 1;
	}
}

.model-install {
	position: absolute;
	left: 0;
	bottom: 0;
	padding: 0.2rem 0.6rem;
	font-size: 0.95rem;
	border-radius: var(--radius-sm);
	background-color: rgba(241, 178, 74, 0.16);
	color: var(--warning);

	&.installed {
		background-color: rgba(94, 234, 212, 0.16);
		color: var(--nori-teal);
	}
}

.model-name {
	font-size: 1.2rem;
	color: var(--text-body);
}
</style>
