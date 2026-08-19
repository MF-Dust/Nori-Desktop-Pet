<script setup lang="ts">
import {onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import {PhysicalPosition, PhysicalSize} from "../services/host/window"
import {listen, type UnlistenFn} from "../services/host/event"
import {getCurrentWindow} from "../services/host/window"
import {createLive2D, type MotionGroup} from "../services/live2d"
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

// 模型动作组数组 (挂载后读取, 供 AI / 主窗口调用)
const motionGroups = ref<MotionGroup[]>([])

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

// ---- 窗口尺寸动态适配: 固定基础尺寸 × 缩放系数, 始终保持窗口中心不动 ----
const applyWindowSize = async (): Promise<void> => {
	const WEBVIEW = getCurrentWindow()
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

// ---- 全局头部跟踪与平滑拖动 ----
let tracking = false
let trackRafId: number | null = null

// 拖拽状态
let isDragging = false
let dragStartCursorX = 0
let dragStartCursorY = 0
let dragStartWindowX = 0
let dragStartWindowY = 0
let hasDragged = false

const trackCursor = async () => {
	if (!tracking) return
	try {
		const CURSOR_INFO = await invoke<[number, number, boolean]>("get_cursor_pos")
		const CURSOR_X = CURSOR_INFO[0]
		const CURSOR_Y = CURSOR_INFO[1]
		const IS_LEFT_DOWN = CURSOR_INFO[2]

		const WEBVIEW = getCurrentWindow()

		// 全局检测按键释放: 如果鼠标按键已松开, 立即停止拖动
		if (isDragging && !IS_LEFT_DOWN) {
			isDragging = false
		}

		// 正在拖动时同步窗口位置
		if (isDragging && IS_LEFT_DOWN) {
			const dx = CURSOR_X - dragStartCursorX
			const dy = CURSOR_Y - dragStartCursorY
			if (Math.abs(dx) > 1 || Math.abs(dy) > 1) {
				hasDragged = true
				await WEBVIEW.setPosition(new PhysicalPosition(
					Math.round(dragStartWindowX + dx),
					Math.round(dragStartWindowY + dy)
				))
			}
		}

		const POS = await WEBVIEW.outerPosition()
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		const CANVAS = L2D.canvas()
		if (CANVAS) {
			await L2D.lookAt({
				target: CANVAS,
				pageX: (CURSOR_X - POS.x) / SCALE_FACTOR,
				pageY: (CURSOR_Y - POS.y) / SCALE_FACTOR,
			} as unknown as MouseEvent)
		}
	} catch {
		/* 模型未加载时忽略 */
	}
	trackRafId = requestAnimationFrame(() => void trackCursor())
}

// ---- 模型加载 ----
let unlistenPetStart: UnlistenFn | null = null
let unlistenConfigChanged: UnlistenFn | null = null
let unlistenPlayMotion: UnlistenFn | null = null
let mountedOnce = false

const afterMount = async () => {
	applyCanvas()
	await applyWindowSize()
	// 读取模型动作组数组 (供 AI 调用)
	try {
		motionGroups.value = (await L2D.getMotions()) ?? []
	} catch {
		motionGroups.value = []
	}
	// 写入配置, 聊天时 Rust 据此把可用动作列表注入系统提示词
	if (motionGroups.value.length > 0) {
		invoke("set_config", {
			key: `l2d_motions_${modelName.value}`,
			value: JSON.stringify(motionGroups.value),
		}).catch(() => {})
	}
	await applyExpressions(expressionList.value)
}

const ensureResourceInstalled = async (name: string): Promise<boolean> => {
	try {
		const installed = await invoke<boolean>("check_resource", {resourceType: "live2d", name})
		if (!installed) {
			await invoke("write_log", {level: "info", message: `桌宠检测到模型 ${name} 未下载，开始自动下载`})
			await invoke("ensure_resource", {resourceType: "live2d", name})
		}
		return true
	} catch (error) {
		console.error(`确保桌宠资源失败: ${name}`, error)
		return false
	}
}

const mountModel = async () => {
	if (mountedOnce) return
	mountedOnce = true
	await loadModelConfigs()
	try {
		await ensureResourceInstalled(modelName.value)
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
	} catch (error) {
		console.error("加载 Live2D 模型失败:", error)
		void invoke("write_log", {
			level: "error",
			message: `桌宠模型加载失败: ${String(error)}`,
		}).catch(() => {})
		// 失败时允许下次 pet-start / 可见性轮询重试, 否则窗口会永远空着
		mountedOnce = false
		return
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
		await ensureResourceInstalled(modelName.value)
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
		return await getCurrentWindow().isVisible()
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
		if (typeof SAVED === "string" && SAVED.trim().length > 0) modelName.value = SAVED.trim()
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
	// 避免资源未就绪时加载失败; 挂载失败时监听器仍在, 下次唤出会重试
	if (await isWindowVisible()) {
		await mountModel()
	}
	unlistenPetStart = await listen("nori:pet-start", () => {
		void mountModel()
	})

	// AI / 主窗口触发动作: payload {group, no} 或 {name}
	unlistenPlayMotion = await listen("nori:play-motion", (event) => {
		const PAYLOAD = event.payload as {group?: string; no?: number; name?: string}
		if (typeof PAYLOAD.group === "string" && typeof PAYLOAD.no === "number") {
			void L2D.playMotionByIndex(PAYLOAD.group, PAYLOAD.no)
			return
		}
		if (typeof PAYLOAD.name === "string") {
			void L2D.playMotionByName(PAYLOAD.name)
		}
	})

	// 鼠标交互: 左键按住拖动窗口, 点击触发 TapBody 动作
	window.addEventListener("mousedown", onMouseDown)
	window.addEventListener("mouseup", onMouseUp)

	// 兜底: pet-start 事件可能在监听注册前就发出 (用户点唤出时桌宠 webview 尚未就绪),
	// 轮询窗口可见性补挂载, 最多 30 次 × 500ms
	void ensurePetMounted()
})

// 鼠标拖动与点击交互
const onMouseDown = async (event: MouseEvent) => {
	if (event.button !== 0) return
	isDragging = true
	hasDragged = false
	try {
		const CURSOR_INFO = await invoke<[number, number, boolean]>("get_cursor_pos")
		dragStartCursorX = CURSOR_INFO[0]
		dragStartCursorY = CURSOR_INFO[1]
		const POS = await getCurrentWindow().outerPosition()
		dragStartWindowX = POS.x
		dragStartWindowY = POS.y
	} catch (error) {
		console.error("初始化拖拽位置失败:", error)
	}
}

const onMouseUp = async (event: MouseEvent) => {
	if (event.button !== 0) return
	const wasDragging = isDragging
	isDragging = false
	if (wasDragging && !hasDragged) {
		try {
			await L2D.playMotionByIndex("TapBody", 0)
		} catch {
			/* 无动作时忽略 */
		}
	}
}

// 兜底补挂载: 窗口已显示但模型未加载时, 触发 mountModel (成功后自停)
const ensurePetMounted = async (attempt = 0): Promise<void> => {
	if (disposed || mountedOnce || attempt >= 30) return
	await new Promise((resolve) => setTimeout(resolve, 500))
	if (disposed || mountedOnce) return
	if (await isWindowVisible()) {
		await mountModel()
		return
	}
	void ensurePetMounted(attempt + 1)
}

let disposed = false

onBeforeUnmount(() => {
	disposed = true
	tracking = false
	window.removeEventListener("mousedown", onMouseDown)
	window.removeEventListener("mouseup", onMouseUp)
	if (trackRafId != null) cancelAnimationFrame(trackRafId)
	void L2D.destroy()
	if (unlistenPetStart) unlistenPetStart()
	if (unlistenConfigChanged) unlistenConfigChanged()
	if (unlistenPlayMotion) unlistenPlayMotion()
})
</script>

<template>
	<div
		class="pet-stage"
		@mousedown="onMouseDown"
		@mouseup="onMouseUp"
	/>
</template>

<style scoped lang="less">
.pet-stage {
	position: fixed;
	inset: 0;
	width: 100vw;
	height: 100vh;
	overflow: visible;
	background: transparent;
	user-select: none;
	cursor: grab;

	&:active {
		cursor: grabbing;
	}
}
</style>