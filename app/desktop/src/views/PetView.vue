<script setup lang="ts">
import {onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {PhysicalPosition, PhysicalSize} from "@tauri-apps/api/dpi"
import {listen, type UnlistenFn} from "@tauri-apps/api/event"
import {getCurrentWebviewWindow} from "@tauri-apps/api/webviewWindow"
import {createLive2D} from "../services/live2d"
import {
	L2D_CONFIG_KEYS,
	parseExpressionList,
	parseNumber,
	readModelConfig,
	resolveModelFileBase,
	type L2DConfigKey,
} from "../services/live2d/config"
import {applyCanvasLayout} from "../services/live2d/stage"

const L2D = createLive2D()

const modelName = ref("arg-nori")

// 模型基础尺寸 (CSS px): 挂载后按实际可视范围测量, 窗口始终紧贴模型
const baseWidth = ref(400)
const baseHeight = ref(520)

// ---- L2D 显示配置 (由模型管理页修改, 这里只应用) ----
const scale = ref(1)
const opacity = ref(1)
const expressionList = ref<string[]>([])

// 画布铺满窗口: 窗口尺寸 = 模型可视尺寸 × scale, 模型始终完整显示不被裁剪
const applyCanvas = () => {
	const CANVAS = L2D.canvas()
	if (!CANVAS) return
	applyCanvasLayout(CANVAS, null, {
		zIndex: "auto",
		scale: 1,
		offsetX: 0,
		offsetY: 0,
		animate: false,
	})
	CANVAS.style.opacity = String(opacity.value)
}

// ---- 窗口尺寸动态适配: 跟随模型尺寸变化, 始终保持窗口中心不动 ----
const applyWindowSize = async (): Promise<void> => {
	const WEBVIEW = getCurrentWebviewWindow()
	try {
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		const OLD_POS = await WEBVIEW.outerPosition()
		const OLD_SIZE = await WEBVIEW.outerSize()
		const NEW_W = Math.max(80, Math.round(baseWidth.value * scale.value * SCALE_FACTOR))
		const NEW_H = Math.max(80, Math.round(baseHeight.value * scale.value * SCALE_FACTOR))
		await WEBVIEW.setSize(new PhysicalSize(NEW_W, NEW_H))
		const CENTER_X = OLD_POS.x + OLD_SIZE.width / 2
		const CENTER_Y = OLD_POS.y + OLD_SIZE.height / 2
		await WEBVIEW.setPosition(new PhysicalPosition(Math.round(CENTER_X - NEW_W / 2), Math.round(CENTER_Y - NEW_H / 2)))
	} catch (error) {
		console.error("窗口尺寸适配失败:", error)
	}
}

// ---- 测量模型可视范围: 读取画布像素, 找到不透明区域的边界 ----
// (库已补丁开启 preserveDrawingBuffer, 可以随时读像素)
const measureVisualBounds = async (): Promise<void> => {
	const CANVAS = L2D.canvas()
	if (!CANVAS) return
	const GL = CANVAS.getContext("webgl2")
	if (!GL) return
	const W = CANVAS.width
	const H = CANVAS.height
	if (W === 0 || H === 0) return
	let minX = Infinity
	let minY = Infinity
	let maxX = -Infinity
	let maxY = -Infinity

	// 纹理加载需要时间: 轮询等待画布渲染出内容, 最多等 6 秒
	const DEADLINE = Date.now() + 6000
	while (Date.now() < DEADLINE) {
		const PIXELS = new Uint8Array(W * H * 4)
		GL.readPixels(0, 0, W, H, GL.RGBA, GL.UNSIGNED_BYTE, PIXELS)
		let found = false
		for (let index = 0; index < W * H; index++) {
			if (PIXELS[index * 4 + 3] <= 8) continue
			found = true
			const X = index % W
			const Y = (index / W) | 0
			if (X < minX) minX = X
			if (X > maxX) maxX = X
			if (Y < minY) minY = Y
			if (Y > maxY) maxY = Y
		}
		if (found) {
			// 先按当前范围立即调整窗口, 再继续采样细化
			applyVisualBounds(W, H, minX, minY, maxX, maxY, CANVAS)
			break
		}
		await new Promise((resolve) => setTimeout(resolve, 200))
	}
	if (minX === Infinity) return

	// 内容出现后长时采样 (约 3 秒), 覆盖待机动画各姿态的最大范围
	for (let sample = 0; sample < 12; sample++) {
		await new Promise((resolve) => setTimeout(resolve, 250))
		const PIXELS = new Uint8Array(W * H * 4)
		GL.readPixels(0, 0, W, H, GL.RGBA, GL.UNSIGNED_BYTE, PIXELS)
		for (let index = 0; index < W * H; index++) {
			if (PIXELS[index * 4 + 3] <= 8) continue
			const X = index % W
			const Y = (index / W) | 0
			if (X < minX) minX = X
			if (X > maxX) maxX = X
			if (Y < minY) minY = Y
			if (Y > maxY) maxY = Y
		}
	}
	applyVisualBounds(W, H, minX, minY, maxX, maxY, CANVAS)
}

// 按测量范围设置基础尺寸 (留 12% 边距, 防止动画/转头时被窗口边缘裁切)
const applyVisualBounds = (
	bitmapW: number,
	bitmapH: number,
	minX: number,
	minY: number,
	maxX: number,
	maxY: number,
	canvas: HTMLCanvasElement
) => {
	const WIDTH = ((maxX - minX + 1) / bitmapW) * canvas.clientWidth
	const HEIGHT = ((maxY - minY + 1) / bitmapH) * canvas.clientHeight
	baseWidth.value = Math.max(40, WIDTH + Math.max(16, WIDTH * 0.12))
	baseHeight.value = Math.max(40, HEIGHT + Math.max(16, HEIGHT * 0.12))
	void applyWindowSize()
}

// ---- 配置读取 ----
const readNumberConfig = async (key: string, fallback: number): Promise<number> => {
	try {
		const VALUE = await invoke<string | null>("get_config", {key})
		if (VALUE != null) {
			const NUM = parseFloat(VALUE)
			if (!Number.isNaN(NUM)) return NUM
		}
	} catch (error) {
		console.error(`读取配置失败: ${key}`, error)
	}
	return fallback
}

// 读取当前模型全部显示配置
const loadModelConfigs = async (): Promise<void> => {
	scale.value = await readModelConfig(modelName.value, "l2d_scale", parseNumber, 1)
	expressionList.value = await readModelConfig(modelName.value, "l2d_expression", (value) => {
		const LIST = parseExpressionList(value)
		return LIST.length > 0 ? LIST : null
	}, [])
	opacity.value = await readNumberConfig("l2d_opacity", 1)
}

// ---- 表情应用 ----
const applyExpressions = async (list: string[]): Promise<void> => {
	try {
		await L2D.stopExpression()
		for (const name of list) await L2D.playExpression(name)
	} catch {
		/* 模型未加载时忽略 */
	}
}

// ---- 拖动: 指针捕获式实时拖动 ----
// setPointerCapture 后光标移出窗口仍持续收到指针事件, 拖动不会中断,
// 位移实时应用 (rAF 节流), 无系统级拖动的延迟与丢事件问题
let dragging = false
let startClientX = 0
let startClientY = 0
let startWinX = 0
let startWinY = 0
let winScale = 1
let pendingX = 0
let pendingY = 0
let posRafId: number | null = null

const onStagePointerDown = async (e: PointerEvent) => {
	if (e.button !== 0 || dragging) return
	e.preventDefault()
	startClientX = e.clientX
	startClientY = e.clientY
	// 先取窗口位置再进入拖动状态, 避免位移基准未就绪时窗口乱跳
	let POS: {x: number; y: number} | null = null
	try {
		const WEBVIEW = getCurrentWebviewWindow()
		POS = await WEBVIEW.outerPosition()
		winScale = await WEBVIEW.scaleFactor()
	} catch {
		/* 非 Tauri 环境忽略 */
	}
	if (!POS) return
	startWinX = POS.x
	startWinY = POS.y
	dragging = true
	try {
		;(e.target as HTMLElement).setPointerCapture(e.pointerId)
	} catch {
		/* 指针捕获失败时降级为窗口内拖动 */
	}
}

const onStagePointerMove = (e: PointerEvent) => {
	if (!dragging) return
	pendingX = Math.round(startWinX + (e.clientX - startClientX) * winScale)
	pendingY = Math.round(startWinY + (e.clientY - startClientY) * winScale)
	if (posRafId == null) {
		posRafId = requestAnimationFrame(() => {
			posRafId = null
			void getCurrentWebviewWindow().setPosition(new PhysicalPosition(pendingX, pendingY))
		})
	}
}

const onStagePointerUp = (e: PointerEvent) => {
	if (!dragging) return
	dragging = false
	try {
		;(e.target as HTMLElement).releasePointerCapture(e.pointerId)
	} catch {
		/* 忽略 */
	}
}

// ---- 全局头部跟踪: 光标在窗口外也持续跟踪 ----
let tracking = false
let trackRafId: number | null = null

const trackCursor = async () => {
	if (!tracking) return
	try {
		const CURSOR = await invoke<[number, number]>("get_cursor_pos")
		const WEBVIEW = getCurrentWebviewWindow()
		const POS = await WEBVIEW.outerPosition()
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		const CANVAS = L2D.canvas()
		if (!CANVAS) return
		// setAngle 需要 target (offsetLeft/offsetTop) 与 pageX/pageY,
		// 画布固定于窗口左上角, offset 为 0
		await L2D.lookAt({
			target: CANVAS,
			pageX: (CURSOR[0] - POS.x) / SCALE_FACTOR,
			pageY: (CURSOR[1] - POS.y) / SCALE_FACTOR,
		} as unknown as MouseEvent)
	} catch {
		/* 模型未加载时忽略 */
	}
	trackRafId = requestAnimationFrame(() => void trackCursor())
}

// ---- 模型加载 ----
let unlistenPetStart: UnlistenFn | null = null
let unlistenConfigChanged: UnlistenFn | null = null
let mountedOnce = false

const afterMount = async () => {
	applyCanvas()
	await measureVisualBounds()
	await applyWindowSize()
	await applyExpressions(expressionList.value)
}

const mountModel = async () => {
	if (mountedOnce) return
	mountedOnce = true
	await loadModelConfigs()
	try {
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
	} catch (error) {
		console.error("加载 Live2D 模型失败:", error)
	}
	await afterMount()
}

// 切换模型: 卸载旧模型并加载新模型
const reloadModel = async () => {
	mountedOnce = true
	try {
		await L2D.destroy()
	} catch {
		/* 未加载时忽略 */
	}
	await loadModelConfigs()
	try {
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
	} catch (error) {
		console.error("加载 Live2D 模型失败:", error)
	}
	await afterMount()
}

// 当前窗口是否可见 (非 Tauri 环境视为可见, 保持原行为)
const isWindowVisible = async (): Promise<boolean> => {
	try {
		return await getCurrentWebviewWindow().isVisible()
	} catch {
		return true
	}
}

// 解析按模型存储的配置键
const parseModelConfigKey = (key: string): {base: L2DConfigKey; modelId: string} | null => {
	for (const BASE of L2D_CONFIG_KEYS) {
		const PREFIX = `${BASE}_`
		if (key.startsWith(PREFIX)) return {base: BASE, modelId: key.slice(PREFIX.length)}
	}
	return null
}

// 应用配置键 (按模型过滤 + 旧版全局键兜底)
const applyConfigKey = (base: L2DConfigKey, value: string) => {
	if (base === "l2d_expression") {
		const LIST = parseExpressionList(value)
		expressionList.value = LIST
		void applyExpressions(LIST)
		return
	}
	if (base === "l2d_scale") {
		const NUM = parseFloat(value)
		if (!Number.isNaN(NUM)) {
			scale.value = NUM
			applyCanvas()
			void applyWindowSize()
		}
	}
}

onMounted(async () => {
	try {
		const SAVED = await invoke<string | null>("get_config", {key: "selected_model"})
		if (SAVED) modelName.value = SAVED
	} catch {
		/* 读取失败保持默认 */
	}

	// 配置变更 (Rust set_config 全局广播): 模型切换即时生效, 显示参数即时调整
	unlistenConfigChanged = await listen("nori:config-changed", (event) => {
		const {key, value} = event.payload as {key: string; value: string}
		if (key === "selected_model" && value) {
			modelName.value = value
			void reloadModel()
			return
		}
		const PARSED = parseModelConfigKey(key)
		if (PARSED) {
			if (PARSED.modelId !== modelName.value) return
			applyConfigKey(PARSED.base, value)
			return
		}
		if (key === "l2d_opacity") {
			const NUM = parseFloat(value)
			if (!Number.isNaN(NUM)) {
				opacity.value = NUM
				const CANVAS = L2D.canvas()
				if (CANVAS) CANVAS.style.opacity = String(NUM)
			}
			return
		}
		if (L2D_CONFIG_KEYS.includes(key as L2DConfigKey)) applyConfigKey(key as L2DConfigKey, value)
	})

	// 全局光标跟踪: 光标在窗口内外模型头部都跟随
	tracking = true
	trackRafId = requestAnimationFrame(() => void trackCursor())

	// 桌宠窗口通常隐藏启动: 等被显示 (nori:pet-start) 时再加载模型,
	// 避免资源未就绪时加载失败
	if (await isWindowVisible()) {
		await mountModel()
		return
	}
	unlistenPetStart = await listen("nori:pet-start", () => {
		void mountModel()
	})
})

onBeforeUnmount(() => {
	tracking = false
	if (trackRafId != null) cancelAnimationFrame(trackRafId)
	void L2D.destroy()
	if (unlistenPetStart) unlistenPetStart()
	if (unlistenConfigChanged) unlistenConfigChanged()
})
</script>

<template>
	<div
		class="pet-stage"
		@pointerdown="onStagePointerDown"
		@pointermove="onStagePointerMove"
		@pointerup="onStagePointerUp"
		@pointercancel="onStagePointerUp"
	/>
</template>

<style scoped lang="less">
.pet-stage {
	position: relative;
	width: 100%;
	height: 100%;
	overflow: visible;
	background: transparent;
	cursor: grab;
	user-select: none;
}
</style>