<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import {PhysicalPosition, PhysicalSize} from "../services/host/window"
import {listen, type UnlistenFn} from "../services/host/event"
import {getCurrentWindow} from "../services/host/window"
import {showWindow, hideWindow} from "../services/window"
import useLanguages from "../services/i18n/useLanguages.ts"
import Icon from "../components/Icon.vue"
import {createLive2D, setPetLive2DController, type MotionGroup} from "../services/live2d"
import {
	L2D_CONFIG_KEYS,
	parseExpressionList,
	parseNumber,
	readModelConfig,
	readBehaviorConfig,
	resolveModelFileBase,
	type L2DConfigKey,
} from "../services/live2d/config"
import {lipSyncAnalyzer} from "../services/live2d/lipSync"
import {audioService} from "../services/audio"

const L2D = createLive2D()
setPetLive2DController(L2D)

const modelName = ref("arg-nori")

// ---- L2D 显示配置 (由模型管理页修改, 这里只应用) ----
const scale = ref(1)
const opacity = ref(1)
const expressionList = ref<string[]>([])

// 模型动作组数组 (挂载后读取, 供 AI / 主窗口调用)
const motionGroups = ref<MotionGroup[]>([])

// 行为配置 (缓存, 热更新)
const behaviorConfig = ref<Record<string, string | number | boolean>>({})

// ---- 窗口尺寸动态适配: 固定基础尺寸 × 缩放系数, 始终保持窗口中心不动 ----
const applyWindowSize = async (): Promise<void> => {
	const WEBVIEW = getCurrentWindow()
	try {
		const BASE = L2D.getBaseSize()
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		const OLD_POS = await WEBVIEW.outerPosition()
		const OLD_SIZE = await WEBVIEW.outerSize()
		const NEW_W = Math.max(80, Math.round(BASE.width * scale.value * SCALE_FACTOR))
		const NEW_H = Math.max(80, Math.round(BASE.height * scale.value * SCALE_FACTOR))
		await WEBVIEW.setSize(new PhysicalSize(NEW_W, NEW_H))
		const CENTER_X = OLD_POS.x + OLD_SIZE.width / 2
		const CENTER_Y = OLD_POS.y + OLD_SIZE.height / 2
		await WEBVIEW.setPosition(new PhysicalPosition(Math.round(CENTER_X - NEW_W / 2), Math.round(CENTER_Y - NEW_H / 2)))
		await L2D.resize()
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

// 读取全部行为配置并应用到控制器
const loadBehaviorConfigs = async (): Promise<void> => {
	behaviorConfig.value = {}
	const keys = [
		"l2d_click_interaction",
		"l2d_auto_blink",
		"l2d_eye_tracking",
		"l2d_idle_eye_animation",
		"l2d_idle_animation",
		"l2d_expression_enabled",
		"l2d_lip_sync",
		"l2d_shadow",
		"l2d_render_scale",
		"l2d_max_fps",
		"l2d_beat_sync",
	]
	for (const key of keys) {
		behaviorConfig.value[key] = await readBehaviorConfig(key as any)
	}
	L2D.applyConfig({
		autoBlink: behaviorConfig.value.l2d_auto_blink === true,
		eyeTracking: behaviorConfig.value.l2d_eye_tracking !== false,
		idleEyeAnimation: behaviorConfig.value.l2d_idle_eye_animation !== false,
		idleAnimation: behaviorConfig.value.l2d_idle_animation !== false,
		expressionEnabled: behaviorConfig.value.l2d_expression_enabled !== false,
		shadowEnabled: behaviorConfig.value.l2d_shadow !== false,
		lipSyncEnabled: behaviorConfig.value.l2d_lip_sync !== false,
		beatSyncEnabled: behaviorConfig.value.l2d_beat_sync === true,
		clickInteraction: behaviorConfig.value.l2d_click_interaction !== false,
		renderScale: typeof behaviorConfig.value.l2d_render_scale === "number" ? behaviorConfig.value.l2d_render_scale : 2,
		maxFps: typeof behaviorConfig.value.l2d_max_fps === "number" ? behaviorConfig.value.l2d_max_fps : 0,
		userScale: scale.value,
	})
}

const I18N = computed(() => useLanguages().views.pet)

// ---- 右键菜单状态 ----
const contextMenuVisible = ref(false)
const menuPos = ref({x: 0, y: 0})

// ---- 窗口位置持久化 ----
const saveWindowPosition = async () => {
	try {
		const WEBVIEW = getCurrentWindow()
		const POS = await WEBVIEW.outerPosition()
		await invoke("set_config", {key: "pet_window_x", value: String(POS.x)})
		await invoke("set_config", {key: "pet_window_y", value: String(POS.y)})
	} catch (error) {
		console.error("保存桌宠窗口位置失败:", error)
	}
}

const restoreWindowPosition = async () => {
	try {
		const [posXStr, posYStr] = await Promise.all([
			invoke<string | null>("get_config", {key: "pet_window_x"}),
			invoke<string | null>("get_config", {key: "pet_window_y"}),
		])
		if (posXStr != null && posYStr != null) {
			const x = parseInt(posXStr, 10)
			const y = parseInt(posYStr, 10)
			if (!Number.isNaN(x) && !Number.isNaN(y)) {
				const WEBVIEW = getCurrentWindow()
				await WEBVIEW.setPosition(new PhysicalPosition(x, y))
			}
		}
	} catch (error) {
		console.error("恢复桌宠窗口位置失败:", error)
	}
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

// ---- 口型同步桥接 ----
let lipSyncInterval: number | null = null
let lastLipSyncNode: AudioBufferSourceNode | null = null

const wireLipSync = () => {
	if (lipSyncInterval != null) return
	lipSyncInterval = window.setInterval(() => {
		if (behaviorConfig.value.l2d_lip_sync === false) {
			if (lastLipSyncNode != null) {
				lipSyncAnalyzer.detach()
				lastLipSyncNode = null
				L2D.setNowSpeaking(false)
			}
			return
		}
		const node = audioService.getActiveSourceNode()
		const ctx = audioService.getAudioContextRef()
		if (node && ctx && node !== lastLipSyncNode) {
			lipSyncAnalyzer.detach()
			lipSyncAnalyzer.attach(ctx, node, (value) => {
				L2D.setMouthOpen(value)
				L2D.setNowSpeaking(value > 0.02)
			})
			lastLipSyncNode = node
		} else if (!node && lastLipSyncNode != null) {
			lipSyncAnalyzer.detach()
			lastLipSyncNode = null
			L2D.setNowSpeaking(false)
		}
	}, 250)
}

// ---- 全局头部跟踪与平滑拖动 ----
let tracking = false
let trackRafId: number | null = null
const DRAG_THRESHOLD = 4

// 拖拽状态
let dragPending = false
let isDragging = false
let dragStartCursorX = 0
let dragStartCursorY = 0
let dragStartWindowX = 0
let dragStartWindowY = 0
let hasDragged = false
let suppressNextClick = false
let pointerDown = false
let activePointerId: number | null = null
let lastCursorX = 0
let lastCursorY = 0

const finishDrag = () => {
	if (isDragging) {
		suppressNextClick = hasDragged
		if (hasDragged) {
			void saveWindowPosition()
		}
	}
	dragPending = false
	isDragging = false
	pointerDown = false
	const POINTER_ID = activePointerId
	activePointerId = null
	const CANVAS = L2D.canvas()
	if (CANVAS) {
		if (POINTER_ID != null && CANVAS.hasPointerCapture(POINTER_ID)) {
			try {
				CANVAS.releasePointerCapture(POINTER_ID)
			} catch {
				/* 某些 WebView 在窗口移动后会拒绝释放捕获 */
			}
		}
		CANVAS.style.cursor = "default"
	}
}

const trackCursor = async () => {
	if (!tracking) return
	try {
		let CURSOR_INFO: [number, number, boolean]
		try {
			const NATIVE_CURSOR = await invoke<[number, number, boolean]>("get_cursor_pos")
			if (Number.isFinite(NATIVE_CURSOR[0]) && Number.isFinite(NATIVE_CURSOR[1])) {
				lastCursorX = NATIVE_CURSOR[0]
				lastCursorY = NATIVE_CURSOR[1]
			}
			CURSOR_INFO = [lastCursorX, lastCursorY, Boolean(NATIVE_CURSOR[2])]
		} catch {
			// WebView 桥暂时不可用时仍允许已捕获的指针继续拖动
			CURSOR_INFO = [lastCursorX, lastCursorY, pointerDown]
		}
		const CURSOR_X = CURSOR_INFO[0]
		const CURSOR_Y = CURSOR_INFO[1]
		const IS_LEFT_DOWN = pointerDown || CURSOR_INFO[2]

		const WEBVIEW = getCurrentWindow()

		// 全局检测按键释放: 如果鼠标按键已松开, 立即停止拖动
		if ((dragPending || isDragging) && !IS_LEFT_DOWN) finishDrag()

		// 正在拖动时同步窗口位置
		if (isDragging && IS_LEFT_DOWN) {
			const dx = CURSOR_X - dragStartCursorX
			const dy = CURSOR_Y - dragStartCursorY
			if (hasDragged || Math.hypot(dx, dy) >= DRAG_THRESHOLD) {
				hasDragged = true
				await WEBVIEW.setPosition(new PhysicalPosition(
					Math.round(dragStartWindowX + dx),
					Math.round(dragStartWindowY + dy)
				))
			}
		}

		const POS = await WEBVIEW.outerPosition()
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		// 光标追踪: 传给控制器的坐标必须是 webview 内的 client 坐标
		L2D.lookAt((CURSOR_X - POS.x) / SCALE_FACTOR, (CURSOR_Y - POS.y) / SCALE_FACTOR)
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
let disposed = false

const afterMount = async () => {
	await applyWindowSize()
	L2D.resize()
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
	const CANVAS = L2D.canvas()
	if (CANVAS) {
		CANVAS.style.cursor = "default"
		CANVAS.style.touchAction = "none"
		CANVAS.style.userSelect = "none"
		CANVAS.style.opacity = String(opacity.value)
		CANVAS.addEventListener("click", onCanvasClick, true)
		CANVAS.addEventListener("pointermove", onCanvasPointerMove)
		CANVAS.addEventListener("pointerleave", onCanvasPointerLeave)
		CANVAS.addEventListener("pointerdown", onCanvasPointerDown)
	}
	await applyExpressions(expressionList.value)
}

const ensureResourceInstalled = async (name: string): Promise<boolean> => {
	try {
		return await invoke<boolean>("check_resource", {resourceType: "live2d", name})
	} catch (error) {
		console.error(`检查桌宠资源失败: ${name}`, error)
		return false
	}
}

const mountModel = async () => {
	if (mountedOnce) return
	mountedOnce = true
	await loadModelConfigs()
	await loadBehaviorConfigs()
	try {
		const INSTALLED = await ensureResourceInstalled(modelName.value)
		if (!INSTALLED) {
			await invoke("write_log", {level: "warn", message: `桌宠检测到模型 ${modelName.value} 未安装`})
			mountedOnce = false
			return
		}
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
		L2D.setUserScale(scale.value)
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
	await loadBehaviorConfigs()
	try {
		await ensureResourceInstalled(modelName.value)
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
		L2D.setUserScale(scale.value)
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

// 行为配置键热更新
const applyBehaviorConfigKey = async (key: string) => {
	if (!key.startsWith("l2d_")) return
	const VALUE = await readBehaviorConfig(key as any)
	behaviorConfig.value[key] = VALUE
	L2D.applyConfig({
		autoBlink: key === "l2d_auto_blink" ? VALUE === true : undefined,
		eyeTracking: key === "l2d_eye_tracking" ? VALUE !== false : undefined,
		idleEyeAnimation: key === "l2d_idle_eye_animation" ? VALUE !== false : undefined,
		idleAnimation: key === "l2d_idle_animation" ? VALUE !== false : undefined,
		expressionEnabled: key === "l2d_expression_enabled" ? VALUE !== false : undefined,
		shadowEnabled: key === "l2d_shadow" ? VALUE !== false : undefined,
		lipSyncEnabled: key === "l2d_lip_sync" ? VALUE !== false : undefined,
		beatSyncEnabled: key === "l2d_beat_sync" ? VALUE === true : undefined,
		clickInteraction: key === "l2d_click_interaction" ? VALUE !== false : undefined,
		renderScale: key === "l2d_render_scale" && typeof VALUE === "number" ? VALUE : undefined,
		maxFps: key === "l2d_max_fps" && typeof VALUE === "number" ? VALUE : undefined,
	})
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
			L2D.setUserScale(NUM)
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
		if (key.startsWith("l2d_")) {
			void applyBehaviorConfigKey(key)
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

	// 窗口 resize 后重新测量容器
	window.addEventListener("resize", onWindowResize)

	// 释放事件放在 window, 兼容指针拖出 WebView 后的释放
	window.addEventListener("pointerup", onPointerUp)
	window.addEventListener("pointercancel", onPointerCancel)
	window.addEventListener("blur", onWindowBlur)

	// 恢复上次保存的窗口位置
	await restoreWindowPosition()

	// 口型同步桥接
	wireLipSync()

	// 兜底: pet-start 事件可能在监听注册前就发出 (用户点唤出时桌宠 webview 尚未就绪),
	// 轮询窗口可见性补挂载, 最多 30 次 × 500ms
	void ensurePetMounted()
})

const onWindowResize = () => {
	L2D.resize()
}

// ---- 右键菜单操作 ----
const closeContextMenu = () => {
	contextMenuVisible.value = false
}

const onContextMenu = (event: MouseEvent) => {
	event.preventDefault()
	menuPos.value = {
		x: Math.max(8, Math.min(event.clientX, window.innerWidth - 150)),
		y: Math.max(8, Math.min(event.clientY, window.innerHeight - 210)),
	}
	contextMenuVisible.value = true
}

const openMainWindow = async () => {
	closeContextMenu()
	await showWindow("main")
}

const triggerRandomMotion = async () => {
	closeContextMenu()
	if (motionGroups.value.length > 0) {
		const randomGroup = motionGroups.value[Math.floor(Math.random() * motionGroups.value.length)]
		if (randomGroup && randomGroup.names.length > 0) {
			const randomMotionName = randomGroup.names[Math.floor(Math.random() * randomGroup.names.length)]
			if (randomMotionName) {
				void L2D.playMotionByName(randomMotionName)
				return
			}
		}
	}
	if (expressionList.value.length > 0) {
		const randomExp = expressionList.value[Math.floor(Math.random() * expressionList.value.length)]
		void L2D.playExpression(randomExp)
	}
}

const resetWindowPosition = async () => {
	closeContextMenu()
	try {
		const WEBVIEW = getCurrentWindow()
		await WEBVIEW.setPosition(new PhysicalPosition(120, 120))
		await saveWindowPosition()
	} catch (error) {
		console.error("重置桌宠位置失败:", error)
	}
}

const hidePet = async () => {
	closeContextMenu()
	await hideWindow("pet")
}

const exitApp = () => {
	closeContextMenu()
	invoke("exit_app")
}

// 鼠标拖动与点击交互
const updateCanvasCursor = (clientX: number, clientY: number) => {
	const CANVAS = L2D.canvas()
	if (!CANVAS || isDragging) return
	CANVAS.style.cursor = L2D.isPointOnModel(clientX, clientY) ? "grab" : "default"
}

const onCanvasPointerMove = (event: PointerEvent) => updateCanvasCursor(event.clientX, event.clientY)

const onCanvasPointerLeave = () => {
	const CANVAS = L2D.canvas()
	if (CANVAS && !isDragging) CANVAS.style.cursor = "default"
}

const onCanvasPointerDown = (event: PointerEvent) => {
	const CANVAS = L2D.canvas()
	if (!CANVAS || event.button !== 0 || dragPending || isDragging || event.target !== CANVAS) return
	if (contextMenuVisible.value) closeContextMenu()
	pointerDown = true
	activePointerId = event.pointerId
	lastCursorX = event.clientX
	lastCursorY = event.clientY
	dragPending = true
	hasDragged = false
	suppressNextClick = false
	try {
		CANVAS.setPointerCapture(event.pointerId)
	} catch {
		/* 不支持 pointer capture 时由 window 的释放兜底 */
	}
	void beginDrag(event)
}

const onCanvasClick = (event: MouseEvent) => {
	if (contextMenuVisible.value) {
		closeContextMenu()
		return
	}
	const SHOULD_SUPPRESS = suppressNextClick
	suppressNextClick = false
	if (SHOULD_SUPPRESS) {
		event.preventDefault()
		event.stopImmediatePropagation()
		return
	}
	if (!L2D.isPointOnModel(event.clientX, event.clientY)) {
		event.preventDefault()
		event.stopImmediatePropagation()
		return
	}
	// 点击交互: 由控制器内部的 hit-area 处理
	L2D.tapAt(event.clientX, event.clientY)
}

const beginDrag = async (event: PointerEvent) => {
	try {
		const WEBVIEW = getCurrentWindow()
		const POS = await WEBVIEW.outerPosition()
		const SCALE_FACTOR = await WEBVIEW.scaleFactor()
		let CURSOR_X = POS.x + event.clientX * SCALE_FACTOR
		let CURSOR_Y = POS.y + event.clientY * SCALE_FACTOR
		try {
			const CURSOR_INFO = await invoke<[number, number, boolean]>("get_cursor_pos")
			if (Number.isFinite(CURSOR_INFO[0]) && Number.isFinite(CURSOR_INFO[1])) {
				CURSOR_X = CURSOR_INFO[0]
				CURSOR_Y = CURSOR_INFO[1]
			}
		} catch {
			/* 用事件坐标作为设备兼容回退 */
		}
		if (!dragPending || !pointerDown || activePointerId !== event.pointerId) {
			dragPending = false
			return
		}
		lastCursorX = CURSOR_X
		lastCursorY = CURSOR_Y
		dragStartCursorX = CURSOR_X
		dragStartCursorY = CURSOR_Y
		dragStartWindowX = POS.x
		dragStartWindowY = POS.y
		isDragging = true
		const CANVAS = L2D.canvas()
		if (CANVAS) CANVAS.style.cursor = "grabbing"
	} catch (error) {
		dragPending = false
		console.error("初始化拖拽位置失败:", error)
	}
}

const onPointerUp = (event: PointerEvent) => {
	if (activePointerId != null && event.pointerId !== activePointerId) return
	if (event.button !== 0 && event.type === "pointerup") return
	const WAS_DRAGGING = isDragging || dragPending
	const DID_DRAG = hasDragged
	pointerDown = false
	finishDrag()
	if (!WAS_DRAGGING) return
	suppressNextClick = DID_DRAG
	updateCanvasCursor(event.clientX, event.clientY)
}

const onPointerCancel = (event: PointerEvent) => onPointerUp(event)

const onWindowBlur = () => {
	if (isDragging || dragPending) finishDrag()
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

onBeforeUnmount(() => {
	disposed = true
	tracking = false
	window.removeEventListener("resize", onWindowResize)
	window.removeEventListener("pointerup", onPointerUp)
	window.removeEventListener("pointercancel", onPointerCancel)
	window.removeEventListener("blur", onWindowBlur)
	const CANVAS = L2D.canvas()
	if (CANVAS) {
		CANVAS.removeEventListener("click", onCanvasClick, true)
		CANVAS.removeEventListener("pointermove", onCanvasPointerMove)
		CANVAS.removeEventListener("pointerleave", onCanvasPointerLeave)
		CANVAS.removeEventListener("pointerdown", onCanvasPointerDown)
	}
	finishDrag()
	if (trackRafId != null) cancelAnimationFrame(trackRafId)
	if (lipSyncInterval != null) {
		clearInterval(lipSyncInterval)
		lipSyncInterval = null
	}
	lipSyncAnalyzer.detach()
	void L2D.destroy()
	setPetLive2DController(null)
	if (unlistenPetStart) unlistenPetStart()
	if (unlistenConfigChanged) unlistenConfigChanged()
	if (unlistenPlayMotion) unlistenPlayMotion()
})
</script>

<template>
	<div class="pet-stage" @contextmenu="onContextMenu" @click="closeContextMenu">
		<!-- 右键桌面菜单 -->
		<Transition name="menu-fade">
			<div
				v-if="contextMenuVisible"
				class="pet-context-menu"
				:style="{left: `${menuPos.x}px`, top: `${menuPos.y}px`}"
				@click.stop
			>
				<button class="menu-item" @click="openMainWindow">
					<Icon name="noriOS" class="menu-icon"/>
					<span>{{ I18N.contextMenu?.openMain }}</span>
				</button>
				<button class="menu-item" @click="triggerRandomMotion">
					<Icon name="sparkles" class="menu-icon"/>
					<span>{{ I18N.contextMenu?.playMotion }}</span>
				</button>
				<button class="menu-item" @click="resetWindowPosition">
					<Icon name="settings" class="menu-icon"/>
					<span>{{ I18N.contextMenu?.resetPos }}</span>
				</button>
				<div class="menu-divider"/>
				<button class="menu-item" @click="hidePet">
					<Icon name="minus" class="menu-icon"/>
					<span>{{ I18N.contextMenu?.hidePet }}</span>
				</button>
				<button class="menu-item danger" @click="exitApp">
					<Icon name="close" class="menu-icon"/>
					<span>{{ I18N.contextMenu?.exitApp }}</span>
				</button>
			</div>
		</Transition>
	</div>
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
	cursor: default;
}

.pet-context-menu {
	position: fixed;
	z-index: 9999;
	min-width: 13.6rem;
	padding: 0.6rem;
	background: linear-gradient(160deg, rgba(16, 48, 75, 0.95) 0%, rgba(2, 10, 18, 0.98) 100%);
	backdrop-filter: blur(12px);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.5), 0 0 1rem var(--glow-teal-soft);
	display: flex;
	flex-direction: column;
	gap: 0.3rem;
}

.menu-item {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	padding: 0.6rem 0.9rem;
	border: none;
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-body);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.15s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.15);
		color: var(--nori-teal-bright);
	}

	&.danger:hover {
		background: rgba(255, 80, 80, 0.18);
		color: #ff6b6b;
	}
}

.menu-icon {
	width: 1.4rem;
	height: 1.4rem;
	flex-shrink: 0;
}

.menu-divider {
	height: 0.1rem;
	background: var(--line-subtle);
	margin: 0.2rem 0.4rem;
}

.menu-fade-enter-active,
.menu-fade-leave-active {
	transition: opacity 0.15s ease, transform 0.15s ease;
}

.menu-fade-enter-from,
.menu-fade-leave-to {
	opacity: 0;
	transform: scale(0.95);
}
</style>