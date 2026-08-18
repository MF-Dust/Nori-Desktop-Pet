<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref} from "vue"
import Icon from "../components/Icon.vue"
import useLanguages from "../services/i18n/useLanguages.ts"
import {createLive2D, type Live2DModelSpec} from "../services/live2d"
import {resolveModelFileBase} from "../services/live2d/config"

// 多语言
const I18N = computed(() => useLanguages().views.pet)

// ---- Live2D 控制器 (渲染 / 交互均经由 services/live2d 解耦) ----
const L2D = createLive2D()

// 当前模型: 从配置读取目录名, 文件名基础名由服务层解析
const modelName = ref("arg-nori")
const modelLoading = ref(false)

// ---- DOM 引用 ----
const stageRef = ref<HTMLDivElement>()
const canvasRef = ref<HTMLCanvasElement>()
const inputRef = ref<HTMLInputElement>()

// ---- 气泡 / 提示 / 对话状态 ----
const showBubble = ref(false)
const bubble = ref({message: ""})
const showHint = ref(true)
const dialog = ref({visible: false})
const inputText = ref("")
const bubbleTimer = ref<number>()

// 隐藏气泡 (延迟触发气泡退出动画)
const hideBubbleAfter = (ms: number) => {
	window.clearTimeout(bubbleTimer.value)
	bubbleTimer.value = window.setTimeout(() => {
		showBubble.value = false
	}, ms)
}

// 展示一条宠物发言气泡
const say = (message: string, autoHide = 3000) => {
	bubble.value.message = message
	showBubble.value = true
	hideBubbleAfter(autoHide)
}

// 按下鼠标
const onMouseDown = () => {
	/* 预留: 拖拽 / 抬起标记等后续实现 */
}

// 移动鼠标 → 递给 Live2D 眼神追踪
const onMouseMove = (e: MouseEvent) => {
	L2D.setAngle(e).catch(() => {
		/* 未加载完成时忽略 */
	})
}

// 点击事件
const onClick = () => {
	if (modelLoading.value) return
	// 打开对话输入框 (真正的 L2D 随机表情/动作由库的点击事件 `enableClickTap` 处理)
	// say(I18N.value.dialog.placeholder)
	dialog.value.visible = true
	showHint.value = false
	window.setTimeout(() => inputRef.value?.focus(), 0)
}

const onDialogMouseDown = (e: MouseEvent) => e.stopPropagation()

const onInputKeyDown = (e: KeyboardEvent) => {
	if (e.key === "Enter") send()
}

const send = async () => {
	const text = inputText.value.trim()
	if (!text) return
	inputText.value = ""
	say(text)
	dialog.value.visible = false
	// 简单联动: 让 Nori 做一个动作 + 开口 (预留语音/口型)
	L2D.playMotion("Reactions", 0).catch(() => {
		/* 动作组可能不存在, 忽略 */
	})
}

onMounted(async () => {
	// 读取已选模型目录 (config.selected_model), 缺省 arg-nori
	try {
		const {invoke} = await import("@tauri-apps/api/core")
		const SAVED = await invoke<string | null>("get_config", {key: "selected_model"})
		if (SAVED) modelName.value = SAVED
	} catch {
		/* 非 Tauri 环境忽略 */
	}

	// 加载并渲染 Live2D 模型
	modelLoading.value = true
	try {
		const spec: Live2DModelSpec = {
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		}
		await L2D.mount(spec, {
			enableIdleTracking: true, // 眼神跟随
			enableClickTap: true,     // 点击随机表情/动作
		})
		showHint.value = true
		// 欢迎语
		say("你好呀，我是 Nori！", 2500)
	} catch (error) {
		console.error("加载 Live2D 模型失败:", error)
		say("呜…模型加载失败了 😢", 3500)
	} finally {
		modelLoading.value = false
	}
})

onBeforeUnmount(() => {
	window.clearTimeout(bubbleTimer.value)
	void L2D.destroy()
})
</script>

<template>
	<div class="pet-stage" ref="stageRef" @mousedown="onMouseDown" @mousemove="onMouseMove" @click="onClick">
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
					@keydown="onInputKeyDown"
				/>
				<button class="dialog-send" @click="send">
					<Icon name="send" :size="18"/>
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