<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref, watch} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {live2dController, MODEL_CATALOG} from "../../services/live2d"
import type {Live2DModelState} from "../../services/live2d"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {i18n} from "../../services/i18n"
import Icon from "../Icon.vue"

const props = withDefaults(defineProps<{
	modelId?: string
	interactive?: boolean
}>(), {
	modelId: "arg-nori",
	interactive: true,
})

const I18N = computed(() => useLanguages().components.main.live2d)

const stageRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)

const state = ref<Live2DModelState>("unmounted")
const hasRenderer = ref(false)

const thumb = computed(() => {
	return MODEL_CATALOG.find(m => m.id === props.modelId)?.thumb ?? ""
})

const showPlaceholder = computed(() => {
	if (state.value === "ready" && hasRenderer.value) return false
	return true
})

const stateText = computed(() => {
	const KEY = state.value as keyof typeof I18N.value.state
	return I18N.value.state[KEY] ?? state.value
})

const log = async (level: "info" | "warn" | "error", message: string) => {
	try {
		await invoke("write_log", {level, message: `[Live2DCanvas] ${message}`})
	} catch {
	}
	const fn = level === "error" ? console.error : level === "warn" ? console.warn : console.info
	fn(`[Live2DCanvas] ${message}`)
}

let observer: ResizeObserver | null = null
let unsubState: (() => void) | null = null

const setupObserver = () => {
	if (!stageRef.value) return
	observer = new ResizeObserver(entries => {
		for (const entry of entries) {
			const {width, height} = entry.contentRect
			if (width > 0 && height > 0) {
				live2dController.resize(width, height)
			}
		}
	})
	observer.observe(stageRef.value)
}

const onMouseMove = (event: MouseEvent) => {
	if (!props.interactive || !stageRef.value) return
	const RECT = stageRef.value.getBoundingClientRect()
	const X = ((event.clientX - RECT.left) / RECT.width) * 2 - 1
	const Y = ((event.clientY - RECT.top) / RECT.height) * 2 - 1
	live2dController.setLookAt(X, -Y)
}

const load = async (id: string) => {
	await log("info", i18n.global.t("log.canvas.loadModel", {id}))
	await live2dController.loadModel(id)
}

onMounted(async () => {
	unsubState = live2dController.on("state:change", payload => {
		state.value = payload.state
	})
	hasRenderer.value = live2dController.hasRenderer()

	if (!stageRef.value || !canvasRef.value) return
	await live2dController.mount(canvasRef.value)
	setupObserver()
	if (props.interactive) live2dController.enableMouseFollow()
	live2dController.startIdle()
	await load(props.modelId)
	await log("info", i18n.global.t("log.canvas.mountComplete", {state: live2dController.getState()}))
})

onBeforeUnmount(async () => {
	unsubState?.()
	unsubState = null
	observer?.disconnect()
	observer = null
	live2dController.stopIdle()
	live2dController.disableMouseFollow()
	await live2dController.unmount()
	await log("info", i18n.global.t("log.canvas.unmountComplete"))
})

watch(() => props.modelId, async (id, prev) => {
	if (id === prev) return
	await load(id)
})
</script>

<template>
	<div
		class="l2d-stage"
		ref="stageRef"
		@mousemove="onMouseMove"
	>
		<canvas ref="canvasRef" class="l2d-canvas"/>

		<div v-if="showPlaceholder" class="l2d-placeholder">
			<img
				v-if="thumb"
				class="l2d-thumb"
				:src="thumb"
				:alt="modelId"
				draggable="false"
			/>
			<div v-else class="l2d-thumb-empty">
				<icon name="cube" :size="64"/>
			</div>

			<div class="l2d-badge">
				<span class="l2d-state" :data-state="state">{{ stateText }}</span>
				<span v-if="!hasRenderer" class="l2d-hint">{{ I18N.notReady }}</span>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.l2d-stage {
	position: relative;
	width: 100%;
	height: 100%;
	min-height: 0;
	display: grid;
	overflow: hidden;
	border-radius: var(--radius-md);
	background: radial-gradient(60% 60% at 50% 40%, rgba(125, 227, 255, 0.08), transparent 70%),
	linear-gradient(160deg, var(--bg-deep) 0%, var(--bg-abyss) 100%);
}

.l2d-canvas {
	grid-area: 1 / 1;
	width: 100%;
	height: 100%;
	display: block;
}

.l2d-placeholder {
	grid-area: 1 / 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 2.4rem;
	padding: 2rem;
	user-select: none;
}

.l2d-thumb {
	width: auto;
	max-width: 60%;
	max-height: 60%;
	object-fit: contain;
	filter: drop-shadow(0 0 3rem var(--glow-teal-soft));
	animation: breathe 4s ease-in-out infinite;
}

.l2d-thumb-empty {
	color: var(--text-muted);
	opacity: 0.5;
}

.l2d-badge {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.6rem;
}

.l2d-state {
	font-size: 1.3rem;
	font-weight: 600;
	color: var(--text-primary);
	letter-spacing: 0.04rem;

	&[data-state="missing"],
	&[data-state="error"] {
		color: var(--warning);
	}

	&[data-state="loading"] {
		color: var(--nori-teal-soft);
	}

	&[data-state="ready"] {
		color: var(--nori-teal);
	}
}

.l2d-hint {
	font-size: 1.1rem;
	color: var(--text-faint);
}
</style>
