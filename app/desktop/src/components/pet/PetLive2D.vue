<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWebviewWindow} from "@tauri-apps/api/webviewWindow"
import {live2dController} from "../../services/live2d"
import type {Live2DModelState} from "../../services/live2d"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {i18n} from "../../services/i18n"
import Icon from "../Icon.vue"
import {petChatController} from "../../services/pet/PetChatController"
import {loadSelectedModel} from "../../services/store/selectedModel"

const props = withDefaults(defineProps<{
	modelId?: string
}>(), {
	modelId: "arg-nori",
})

const I18N = computed(() => useLanguages().components.pet)

const stageRef = ref<HTMLDivElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)
const inputRef = ref<HTMLInputElement | null>(null)

const state = ref<Live2DModelState>("unmounted")
const hasRenderer = ref(false)
const bubble = ref(petChatController.getBubbleState())
const dialog = ref(petChatController.getDialogState())
const inputText = ref("")

const thumb = computed(() => {
	return MODEL_CATALOG.find(m => m.id === props.modelId)?.thumb ?? ""
})

const showFallback = computed(() => {
	if (hasRenderer.value && state.value === "ready") return false
	return true
})

let unsubState: (() => void) | null = null
let unsubChat: (() => void) | null = null
let observer: ResizeObserver | null = null

let downX = 0
let downY = 0
let downTime = 0
let dragging = false

const showBubble = computed(() => bubble.value.visible && bubble.value.message)
const showHint = computed(() => !dialog.value.visible && !bubble.value.visible)

const log = async (level: "info" | "warn" | "error", message: string) => {
	try {
		await invoke("write_log", {level, message: `[PetLive2D] ${message}`})
	} catch {
	}
}

const startDrag = async () => {
	try {
		await getCurrentWebviewWindow().startDragging()
	} catch {
	}
}

const onMouseDown = (e: MouseEvent) => {
	downX = e.clientX
	downY = e.clientY
	downTime = Date.now()
	dragging = false
}

const onMouseMove = (e: MouseEvent) => {
	if (!stageRef.value) return
	const RECT = stageRef.value.getBoundingClientRect()
	const X = ((e.clientX - RECT.left) / RECT.width) * 2 - 1
	const Y = ((e.clientY - RECT.top) / RECT.height) * 2 - 1
	live2dController.setLookAt(X, -Y)

	if (e.buttons === 1 && !dragging && downTime > 0) {
		const DX = Math.abs(e.clientX - downX)
		const DY = Math.abs(e.clientY - downY)
		if (DX > 5 || DY > 5) {
			dragging = true
			void startDrag()
		}
	}
}

const onClick = () => {
	if (dragging) {
		dragging = false
		return
	}
	const DT = Date.now() - downTime
	if (DT < 400) {
		petChatController.toggleDialog()
	}
	downTime = 0
}

const onDialogMouseDown = (e: MouseEvent) => {
	e.stopPropagation()
}

const send = async () => {
	const TEXT = inputText.value.trim()
	if (!TEXT) return
	inputText.value = ""
	await petChatController.sendMessage(TEXT)
	await nextTick()
	inputRef.value?.focus()
}

const onInputKeyDown = (e: KeyboardEvent) => {
	if (e.key === "Enter" && !e.shiftKey) {
		e.preventDefault()
		void send()
	}
}

const load = async (id: string) => {
	await log("info", i18n.global.t("log.canvas.loadModel", {id}))
	await live2dController.loadModel(id)
}

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

onMounted(async () => {
	await loadSelectedModel()

	unsubState = live2dController.on("state:change", payload => {
		state.value = payload.state
		hasRenderer.value = live2dController.hasRenderer()
	})
	unsubChat = petChatController.subscribe(() => {
		bubble.value = petChatController.getBubbleState()
		dialog.value = petChatController.getDialogState()
	})

	if (!stageRef.value || !canvasRef.value) return
	await live2dController.mount(canvasRef.value)
	setupObserver()
	live2dController.enableMouseFollow()
	live2dController.startIdle()
	await load(props.modelId)
	await log("info", i18n.global.t("log.canvas.mountComplete", {state: live2dController.getState()}))
})

onBeforeUnmount(async () => {
	unsubState?.()
	unsubState = null
	unsubChat?.()
	unsubChat = null
	observer?.disconnect()
	observer = null
	live2dController.stopIdle()
	live2dController.disableMouseFollow()
	await live2dController.unmount()
	petChatController.destroy()
	await log("info", i18n.global.t("log.canvas.unmountComplete"))
})

watch(() => props.modelId, async (id, prev) => {
	if (id === prev) return
	await load(id)
})

watch(() => dialog.value.visible, async (visible) => {
	if (visible) {
		await nextTick()
		inputRef.value?.focus()
	}
})
</script>

<template>
	<div
		class="pet-stage"
		ref="stageRef"
		@mousedown="onMouseDown"
		@mousemove="onMouseMove"
		@click="onClick"
	>
		<canvas ref="canvasRef" class="pet-canvas"/>

		<Transition name="bubble">
			<div v-if="showBubble" class="chat-bubble" @mousedown="onDialogMouseDown">
				<span class="bubble-text">{{ bubble.message }}</span>
			</div>
		</Transition>

		<Transition name="hint">
			<div v-if="showHint" class="click-hint">
				<span>{{ I18N.hint }}</span>
			</div>
		</Transition>

		<Transition name="dialog">
			<div v-if="dialog.visible" class="dialog-box" @mousedown="onDialogMouseDown">
				<input
					ref="inputRef"
					v-model="inputText"
					class="dialog-input"
					:placeholder="I18N.dialog.placeholder"
					@keydown="onInputKeyDown"
				/>
				<button class="dialog-send" @click="send">
					<icon name="send" :size="18"/>
				</button>
			</div>
		</Transition>
	</div>
</template>

<style scoped lang="less">
.pet-stage {
	position: relative;
	width: 100%;
	height: 100%;
	overflow: visible;
	background: transparent;
	cursor: pointer;
	user-select: none;
}

.pet-canvas {
	position: absolute;
	top: 0;
	left: 0;
	width: 100%;
	height: 100%;
	pointer-events: none;
	display: block;
}

.chat-bubble {
	position: absolute;
	top: 0.5rem;
	left: 50%;
	transform: translateX(-50%);
	max-width: 90%;
	padding: 0.8rem 1.4rem;
	background: rgba(18, 28, 42, 0.92);
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: 1.2rem;
	box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.4), 0 0 0.8rem var(--glow-teal-soft);
	z-index: 10;
	pointer-events: auto;
	cursor: default;

	&::after {
		content: "";
		position: absolute;
		bottom: -0.7rem;
		left: 50%;
		transform: translateX(-50%);
		width: 0;
		height: 0;
		border-left: 0.7rem solid transparent;
		border-right: 0.7rem solid transparent;
		border-top: 0.7rem solid var(--nori-teal-soft);
	}

	&::before {
		content: "";
		position: absolute;
		bottom: -0.5rem;
		left: 50%;
		transform: translateX(-50%);
		width: 0;
		height: 0;
		border-left: 0.6rem solid transparent;
		border-right: 0.6rem solid transparent;
		border-top: 0.6rem solid rgba(18, 28, 42, 0.92);
		z-index: 1;
	}
}

.bubble-text {
	font-size: 1.3rem;
	color: var(--text-primary);
	line-height: 1.6;
	word-break: break-word;
	white-space: pre-wrap;
}

.click-hint {
	position: absolute;
	bottom: 0.5rem;
	left: 50%;
	transform: translateX(-50%);
	padding: 0.4rem 1rem;
	background: rgba(18, 28, 42, 0.7);
	border-radius: 0.8rem;
	z-index: 5;
	pointer-events: none;

	span {
		font-size: 1.1rem;
		color: var(--text-faint);
		letter-spacing: 0.04rem;
	}
}

.dialog-box {
	position: absolute;
	bottom: 0.5rem;
	left: 0.5rem;
	right: 0.5rem;
	display: flex;
	gap: 0.6rem;
	padding: 0.6rem;
	background: rgba(18, 28, 42, 0.92);
	border: 0.1rem solid var(--line-subtle);
	border-radius: 1rem;
	box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.4);
	z-index: 10;
	pointer-events: auto;
}

.dialog-input {
	flex: 1;
	min-width: 0;
	padding: 0.6rem 1rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: 0.6rem;
	background: rgba(0, 0, 0, 0.3);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	outline: none;
	transition: border-color 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.6rem var(--glow-teal-soft);
	}

	&::placeholder {
		color: var(--text-faint);
	}
}

.dialog-send {
	flex-shrink: 0;
	width: 3.2rem;
	height: 3.2rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: 0.6rem;
	background: rgba(125, 227, 255, 0.1);
	color: var(--nori-teal-bright);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.2);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
	}

	&:active {
		transform: scale(0.95);
	}
}

.bubble-enter-active,
.bubble-leave-active {
	transition: all 0.3s ease;
}

.bubble-enter-from,
.bubble-leave-to {
	opacity: 0;
	transform: translateX(-50%) translateY(-0.8rem);
}

.hint-enter-active,
.hint-leave-active {
	transition: all 0.3s ease;
}

.hint-enter-from,
.hint-leave-to {
	opacity: 0;
}

.dialog-enter-active,
.dialog-leave-active {
	transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.dialog-enter-from,
.dialog-leave-to {
	opacity: 0;
	transform: translateY(1.6rem);
}
</style>
