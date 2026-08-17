import {invoke} from "@tauri-apps/api/core"
import {i18n} from "../i18n"
import {MODEL_CATALOG} from "./models"
import type {Live2DRenderer, Live2DRendererFactory} from "./Live2DRenderer"
import type {
	EmotionType,
	ExpressionName,
	Live2DEventMap,
	Live2DEventName,
	Live2DEListener,
	Live2DModelEntry,
	Live2DModelState,
	Live2DParameter,
	ModelId,
	MotionGroupName,
	MotionIndex,
} from "./types"

const LOG_TAG = "[Live2D]"
const t = i18n.global.t

async function log(level: "info" | "warn" | "error", message: string): Promise<void> {
	const TEXT = `${LOG_TAG} ${message}`
	try {
		await invoke("write_log", {level, message: TEXT})
	} catch {
	}
	const fn = level === "error" ? console.error : level === "warn" ? console.warn : console.info
	fn(TEXT)
}

function logFire(level: "info" | "warn" | "error", message: string): void {
	void log(level, message)
}

interface BackendModelInfo {
	id: string
	installed: boolean
	model3: string | null
}

interface ControllerState {
	model: ModelId | null
	state: Live2DModelState
	emotion: { type: EmotionType; intensity: number }
	mouseFollow: boolean
	idleActive: boolean
	autoBlink: boolean
	autoBreath: boolean
	physics: boolean
	fps: number
	zoom: number
	anchor: { x: number; y: number }
}

interface SequenceAction {
	type: "motion" | "expression" | "emotion" | "param" | "delay"
	params: Record<string, unknown>
	delay?: number
}

class Live2DController {
	private canvas: HTMLCanvasElement | null = null
	private rendererFactory: Live2DRendererFactory | null = null
	private renderer: Live2DRenderer | null = null
	private currentModel: ModelId | null = null
	private state: Live2DModelState = "unmounted"
	private emotion: { type: EmotionType; intensity: number } = {
		type: "neutral",
		intensity: 0,
	}
	private mouseFollow = false
	private idleActive = false
	private autoBlink = true
	private autoBreath = true
	private physics = true
	private fps = 60
	private zoom = 1
	private anchor = {x: 0.5, y: 0.5}
	private timeScale = 1
	private blinkInterval = 3
	private gravity = {x: 0, y: 1}
	private readonly listeners = new Map<Live2DEventName, Set<Function>>()

	registerRenderer(factory: Live2DRendererFactory): void {
		this.rendererFactory = factory
		logFire("info", t("log.l2d.rendererRegistered", {name: factory.name || "anonymous"}))
		if (this.canvas) {
			void this.applyRenderer()
		}
	}

	hasRenderer(): boolean {
		return this.rendererFactory !== null
	}

	async mount(canvas: HTMLCanvasElement): Promise<void> {
		this.canvas = canvas
		logFire("info", t("log.l2d.canvasMounted"))
		if (this.rendererFactory) {
			await this.applyRenderer()
		} else {
			logFire("warn", t("log.l2d.rendererNotInjected"))
		}
	}

	async unmount(): Promise<void> {
		logFire("info", t("log.l2d.canvasUnmounted"))
		try {
			await this.renderer?.unmount()
		} catch (error) {
			logFire("error", t("log.l2d.rendererUnmountFailed", {error: this.err(error)}))
		}
		this.renderer = null
		this.canvas = null
		this.setState("unmounted")
	}

	async listModels(): Promise<Live2DModelEntry[]> {
		let backend: BackendModelInfo[] = []
		try {
			backend = await invoke<BackendModelInfo[]>("list_live2d_models")
		} catch (error) {
			logFire("error", t("log.l2d.backendListFailed", {error: this.err(error)}))
		}
		const backendMap = new Map(backend.map(m => [m.id, m]))
		const entries: Live2DModelEntry[] = MODEL_CATALOG.map(c => ({
			id: c.id,
			name: c.name,
			thumb: c.thumb,
			installed: backendMap.get(c.id)?.installed ?? false,
		}))
		for (const m of backend) {
			if (!entries.some(e => e.id === m.id)) {
				entries.push({id: m.id, name: m.id, thumb: "", installed: m.installed})
			}
		}
		const INSTALLED = t("components.main.settings.model.installed")
		const NOT_INSTALLED = t("components.main.settings.model.notInstalled")
		const LIST_STR = entries.map(e => `${e.id}(${e.installed ? INSTALLED : NOT_INSTALLED})`).join(", ") || "—"
		logFire("info", t("log.l2d.listModels", {list: LIST_STR}))
		return entries
	}

	getLoadedModel(): ModelId | null {
		return this.currentModel
	}

	async loadModel(modelId: ModelId): Promise<boolean> {
		logFire("info", t("log.l2d.loadModelRequest", {id: modelId}))
		this.currentModel = modelId
		this.emit("model:loading", {model: modelId})
		this.setState("loading")

		let model3Path: string | null = null
		try {
			model3Path = await invoke<string | null>("resolve_live2d_model_path", {modelId})
		} catch (error) {
			logFire("error", t("log.l2d.resolvePathFailed", {error: this.err(error)}))
			this.emit("model:error", {model: modelId, error: this.err(error)})
			this.setState("error")
			return false
		}

		if (!model3Path) {
			logFire("warn", t("log.l2d.modelNotInstalled", {id: modelId}))
			this.emit("model:error", {model: modelId, error: "model not installed"})
			this.setState("missing")
			return false
		}

		if (!this.renderer) {
			logFire("warn", t("log.l2d.rendererNotReadyLoad", {id: modelId}))
			this.setState("ready")
			this.emit("model:loaded", {model: modelId})
			return true
		}

		try {
			await this.renderer.loadModel(modelId, model3Path)
			this.emit("model:loaded", {model: modelId})
			this.setState("ready")
			this.applyCachedSettings()
			logFire("info", t("log.l2d.modelLoadComplete", {id: modelId}))
			return true
		} catch (error) {
			logFire("error", t("log.l2d.modelLoadFailed", {error: this.err(error)}))
			this.emit("model:error", {model: modelId, error: this.err(error)})
			this.setState("error")
			return false
		}
	}

	async unloadModel(): Promise<void> {
		if (!this.currentModel) {
			logFire("info", t("log.l2d.unloadNoModel"))
			return
		}
		const PREV = this.currentModel
		logFire("info", t("log.l2d.unloadModel", {model: PREV}))
		try {
			await this.renderer?.unloadModel()
		} catch (error) {
			logFire("error", t("log.l2d.unloadModelFailed", {error: this.err(error)}))
		}
		this.currentModel = null
		this.emit("model:unloaded", {model: PREV})
		this.setState(this.canvas ? "ready" : "unmounted")
	}

	async reloadModel(): Promise<void> {
		if (!this.currentModel) {
			logFire("warn", t("log.l2d.reloadNoModel"))
			return
		}
		logFire("info", t("log.l2d.reloadModel", {model: this.currentModel}))
		try {
			await this.renderer?.reloadModel()
		} catch (error) {
			logFire("error", t("log.l2d.reloadFailed", {error: this.err(error)}))
		}
	}

	listMotions(): Record<MotionGroupName, MotionIndex[]> {
		if (!this.assureRenderer("listMotions")) return {}
		return this.renderer!.listMotions()
	}

	async playMotion(group: MotionGroupName, index?: MotionIndex): Promise<boolean> {
		if (index !== undefined) {
			logFire("info", t("log.l2d.playMotion", {group, index}))
		} else {
			logFire("info", t("log.l2d.playMotionRandom", {group}))
		}
		if (!this.assureRenderer("playMotion")) return false
		try {
			const OK = await this.renderer!.playMotion(group, index)
			if (OK) this.emit("motion:start", {group, index: index ?? -1})
			return OK
		} catch (error) {
			logFire("error", t("log.l2d.playMotionFailed", {error: this.err(error)}))
			return false
		}
	}

	async stopMotion(): Promise<void> {
		logFire("info", t("log.l2d.stopMotion"))
		if (!this.assureRenderer("stopMotion")) return
		try {
			await this.renderer!.stopMotion()
		} catch (error) {
			logFire("error", t("log.l2d.stopMotionFailed", {error: this.err(error)}))
		}
	}

	isMotionPlaying(): boolean {
		if (!this.renderer) return false
		return this.renderer.isMotionPlaying()
	}

	listExpressions(): ExpressionName[] {
		if (!this.assureRenderer("listExpressions")) return []
		return this.renderer!.listExpressions()
	}

	async setExpression(name: ExpressionName): Promise<boolean> {
		logFire("info", t("log.l2d.setExpression", {name}))
		if (!this.assureRenderer("setExpression")) return false
		try {
			const OK = await this.renderer!.setExpression(name)
			if (OK) this.emit("expression:change", {expression: name})
			return OK
		} catch (error) {
			logFire("error", t("log.l2d.setExpressionFailed", {error: this.err(error)}))
			return false
		}
	}

	async clearExpression(): Promise<void> {
		logFire("info", t("log.l2d.clearExpression"))
		if (!this.assureRenderer("clearExpression")) return
		try {
			await this.renderer!.clearExpression()
			this.emit("expression:change", {expression: null})
		} catch (error) {
			logFire("error", t("log.l2d.clearExpressionFailed", {error: this.err(error)}))
		}
	}

	getCurrentExpression(): ExpressionName | null {
		if (!this.renderer) return null
		return this.renderer.getCurrentExpression()
	}

	setEmotion(emotion: EmotionType, intensity = 1): void {
		const CLAMPED = Math.max(0, Math.min(1, intensity))
		logFire("info", t("log.l2d.setEmotion", {emotion, intensity: CLAMPED}))
		this.emotion = {type: emotion, intensity: CLAMPED}
		this.emit("emotion:change", {emotion, intensity: CLAMPED})
		const MAPPED = this.emotionToExpression(emotion)
		if (MAPPED) {
			void this.setExpression(MAPPED)
		}
	}

	getEmotion(): {type: EmotionType; intensity: number} {
		return {...this.emotion}
	}

	clearEmotion(): void {
		logFire("info", t("log.l2d.clearEmotion"))
		this.emotion = {type: "neutral", intensity: 0}
		this.emit("emotion:change", {emotion: this.emotion.type, intensity: this.emotion.intensity})
		void this.clearExpression()
	}

	decayEmotion(ratio = 0.9): void {
		const NEXT = this.emotion.intensity * ratio
		this.emotion = {type: this.emotion.type, intensity: NEXT}
		logFire("info", t("log.l2d.decayEmotion", {intensity: NEXT.toFixed(3)}))
		this.emit("emotion:change", {emotion: this.emotion.type, intensity: this.emotion.intensity})
	}

	private emotionToExpression(emotion: EmotionType): ExpressionName | null {
		const MAP: Record<string, ExpressionName> = {
			happy: "13_Happy",
			angry: "03_Angry",
			sad: "08_Tears",
			surprised: "14_Surprised",
			doubt: "10_Doubt",
			shy: "04_Shy",
			troubled: "09_Troubled",
			dizzy: "02_Dizzy",
			serious: "12_Serious",
			disgust: "11_Disgust",
			speechless: "06_Speechless",
			neutral: "00_Default",
		}
		return MAP[emotion] ?? null
	}

	getParameter(id: string): number | null {
		if (!this.assureRenderer("getParameter")) return null
		return this.renderer!.getParameter(id)
	}

	setParameter(id: string, value: number): void {
		logFire("info", t("log.l2d.setParameter", {id, value}))
		if (!this.assureRenderer("setParameter")) return
		try {
			this.renderer!.setParameter(id, value)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	getAllParameters(): Live2DParameter[] {
		if (!this.assureRenderer("getAllParameters")) return []
		return this.renderer!.getAllParameters()
	}

	resetParameters(): void {
		logFire("info", t("log.l2d.resetParameters"))
		if (!this.assureRenderer("resetParameters")) return
		try {
			this.renderer!.resetParameters()
		} catch (error) {
			logFire("error", t("log.l2d.resetParametersFailed", {error: this.err(error)}))
		}
	}

	startIdle(): void {
		logFire("info", t("log.l2d.startIdle"))
		this.idleActive = true
		this.renderer?.startIdle()
	}

	stopIdle(): void {
		logFire("info", t("log.l2d.stopIdle"))
		this.idleActive = false
		this.renderer?.stopIdle()
	}

	isIdleActive(): boolean {
		return this.idleActive
	}

	enableMouseFollow(): void {
		logFire("info", t("log.l2d.enableMouseFollow"))
		this.mouseFollow = true
		this.renderer?.enableMouseFollow()
	}

	disableMouseFollow(): void {
		logFire("info", t("log.l2d.disableMouseFollow"))
		this.mouseFollow = false
		this.renderer?.disableMouseFollow()
	}

	isMouseFollowEnabled(): boolean {
		return this.mouseFollow
	}

	setLookAt(x: number, y: number): void {
		if (!this.assureRenderer("setLookAt")) return
		this.renderer!.setLookAt(x, y)
	}

	resize(width: number, height: number): void {
		if (!this.canvas) return
		logFire("info", t("log.l2d.resize", {width, height}))
		this.renderer?.resize(width, height)
	}

	setZoom(scale: number): void {
		logFire("info", t("log.l2d.setZoom", {scale}))
		this.zoom = scale
		this.renderer?.setZoom(scale)
	}

	setAnchor(x: number, y: number): void {
		logFire("info", t("log.l2d.setAnchor", {x, y}))
		this.anchor = {x, y}
		this.renderer?.setAnchor(x, y)
	}

	focus(): void {
		this.renderer?.focus()
	}

	setAutoBlink(enabled: boolean): void {
		logFire("info", t("log.l2d.setAutoBlink", {enabled}))
		this.autoBlink = enabled
		this.renderer?.setAutoBlink(enabled)
	}

	setAutoBreath(enabled: boolean): void {
		logFire("info", t("log.l2d.setAutoBreath", {enabled}))
		this.autoBreath = enabled
		this.renderer?.setAutoBreath(enabled)
	}

	setPhysics(enabled: boolean): void {
		logFire("info", t("log.l2d.setPhysics", {enabled}))
		this.physics = enabled
		this.renderer?.setPhysics(enabled)
	}

	setFps(fps: number): void {
		logFire("info", t("log.l2d.setFps", {fps}))
		this.fps = fps
		this.renderer?.setFps(fps)
	}

	setLipSync(value: number): void {
		const CLAMPED = Math.max(0, Math.min(1, value))
		if (this.renderer) {
			this.renderer.setLipSync(CLAMPED)
		} else {
			logFire("info", t("log.l2d.setLipSync", {value: CLAMPED}))
		}
	}

	setEyeOpen(open: number): void {
		const CLAMPED = Math.max(0, Math.min(1, open))
		logFire("info", t("log.l2d.setEyeOpen", {open: CLAMPED}))
		if (!this.assureRenderer("setEyeOpen")) return
		try {
			this.renderer!.setEyeOpen(CLAMPED)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setBlinkInterval(seconds: number): void {
		const CLAMPED = Math.max(0.1, Math.min(30, seconds))
		logFire("info", t("log.l2d.setBlinkInterval", {seconds: CLAMPED}))
		this.blinkInterval = CLAMPED
		if (!this.assureRenderer("setBlinkInterval")) return
		try {
			this.renderer!.setBlinkInterval(CLAMPED)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setHeadPose(x: number, y: number, z: number): void {
		logFire("info", t("log.l2d.setHeadPose", {x, y, z}))
		if (!this.assureRenderer("setHeadPose")) return
		try {
			this.renderer!.setHeadPose(x, y, z)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setBodyPose(x: number, y: number, z: number): void {
		logFire("info", t("log.l2d.setBodyPose", {x, y, z}))
		if (!this.assureRenderer("setBodyPose")) return
		try {
			this.renderer!.setBodyPose(x, y, z)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setMouthOpen(open: number): void {
		const CLAMPED = Math.max(0, Math.min(1, open))
		logFire("info", t("log.l2d.setMouthOpen", {open: CLAMPED}))
		if (!this.assureRenderer("setMouthOpen")) return
		try {
			this.renderer!.setMouthOpen(CLAMPED)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setEyebrowState(left: number, right: number): void {
		const L = Math.max(-1, Math.min(1, left))
		const R = Math.max(-1, Math.min(1, right))
		logFire("info", t("log.l2d.setEyebrowState", {left: L, right: R}))
		if (!this.assureRenderer("setEyebrowState")) return
		try {
			this.renderer!.setEyebrowState(L, R)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setHandGesture(name: string): void {
		logFire("info", t("log.l2d.setHandGesture", {name}))
		if (!this.assureRenderer("setHandGesture")) return
		try {
			this.renderer!.setHandGesture(name)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	getPartIds(): string[] {
		if (!this.assureRenderer("getPartIds")) return []
		logFire("info", t("log.l2d.getPartIds"))
		try {
			return this.renderer!.getPartIds()
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
			return []
		}
	}

	setPartOpacity(id: string, opacity: number): void {
		const CLAMPED = Math.max(0, Math.min(1, opacity))
		logFire("info", t("log.l2d.setPartOpacity", {id, opacity: CLAMPED}))
		if (!this.assureRenderer("setPartOpacity")) return
		try {
			this.renderer!.setPartOpacity(id, CLAMPED)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	getPartOpacity(id: string): number {
		if (!this.assureRenderer("getPartOpacity")) return 1
		logFire("info", t("log.l2d.getPartOpacity", {id}))
		try {
			return this.renderer!.getPartOpacity(id)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
			return 1
		}
	}

	setGravity(x: number, y: number): void {
		logFire("info", t("log.l2d.setGravity", {x, y}))
		this.gravity = {x, y}
		if (!this.assureRenderer("setGravity")) return
		try {
			this.renderer!.setGravity(x, y)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	setTimeScale(scale: number): void {
		const CLAMPED = Math.max(0, Math.min(10, scale))
		logFire("info", t("log.l2d.setTimeScale", {scale: CLAMPED}))
		this.timeScale = CLAMPED
		if (!this.assureRenderer("setTimeScale")) return
		try {
			this.renderer!.setTimeScale(CLAMPED)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	async captureScreenshot(): Promise<string | null> {
		logFire("info", t("log.l2d.captureScreenshot"))
		if (!this.assureRenderer("captureScreenshot")) return null
		try {
			return await this.renderer!.captureScreenshot()
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
			return null
		}
	}

	setAccessoryVisible(name: string, visible: boolean): void {
		logFire("info", t("log.l2d.setAccessoryVisible", {name, visible}))
		if (!this.assureRenderer("setAccessoryVisible")) return
		try {
			this.renderer!.setAccessoryVisible(name, visible)
		} catch (error) {
			logFire("error", t("log.l2d.setParameterFailed", {error: this.err(error)}))
		}
	}

	speak(text: string, options?: {lang?: string; rate?: number; pitch?: number}): void {
		logFire("info", t("log.l2d.speak", {text: text.slice(0, 50)}))
		this.emit("speak", {text, options: options ?? {}} as never)
	}

	getControllerState(): ControllerState {
		logFire("info", t("log.l2d.getControllerState"))
		return {
			model: this.currentModel,
			state: this.state,
			emotion: {...this.emotion},
			mouseFollow: this.mouseFollow,
			idleActive: this.idleActive,
			autoBlink: this.autoBlink,
			autoBreath: this.autoBreath,
			physics: this.physics,
			fps: this.fps,
			zoom: this.zoom,
			anchor: {...this.anchor},
		}
	}

	async playSequence(actions: SequenceAction[]): Promise<void> {
		logFire("info", t("log.l2d.playSequence", {count: actions.length}))
		for (const action of actions) {
			if (action.delay) {
				await new Promise<void>(resolve => setTimeout(resolve, action.delay))
			}
			switch (action.type) {
				case "motion":
					await this.playMotion(action.params.group as string, action.params.index as number | undefined)
					break
				case "expression":
					await this.setExpression(action.params.name as string)
					break
				case "emotion":
					this.setEmotion(action.params.emotion as EmotionType, action.params.intensity as number | undefined)
					break
				case "param":
					this.setParameter(action.params.id as string, action.params.value as number)
					break
				case "delay":
					await new Promise<void>(resolve => setTimeout(resolve, (action.params.ms as number) || 100))
					break
			}
		}
	}

	getState(): Live2DModelState {
		return this.state
	}

	isReady(): boolean {
		return this.state === "ready"
	}

	on<K extends Live2DEventName>(event: K, cb: Live2DEListener<K>): () => void {
		let set = this.listeners.get(event)
		if (!set) {
			set = new Set()
			this.listeners.set(event, set)
		}
		set.add(cb as Function)
		return () => this.off(event, cb)
	}

	once<K extends Live2DEventName>(event: K, cb: Live2DEListener<K>): () => void {
		const wrapper = (payload: any) => {
			this.off(event, wrapper as any)
			cb(payload)
		}
		return this.on(event, wrapper as any)
	}

	off<K extends Live2DEventName>(event: K, cb: Live2DEListener<K>): void {
		this.listeners.get(event)?.delete(cb as Function)
	}

	destroy(): void {
		logFire("info", t("log.l2d.destroy"))
		try {
			this.renderer?.destroy()
		} catch (error) {
			logFire("error", t("log.l2d.destroyFailed", {error: this.err(error)}))
		}
		this.renderer = null
		this.canvas = null
		this.rendererFactory = null
		this.currentModel = null
		this.listeners.clear()
		this.setState("unmounted")
	}

	private async applyRenderer(): Promise<void> {
		if (!this.canvas || !this.rendererFactory) return
		try {
			this.renderer = this.rendererFactory(this.canvas)
			await this.renderer.mount(this.canvas)
			logFire("info", t("log.l2d.rendererMounted", {id: this.renderer.id}))
			if (this.currentModel) {
				const M = this.currentModel
				await this.loadModel(M)
			}
			this.applyCachedSettings()
			this.emit("ready", undefined as never)
		} catch (error) {
			logFire("error", t("log.l2d.rendererMountFailed", {error: this.err(error)}))
			this.setState("error")
		}
	}

	private applyCachedSettings(): void {
		const R = this.renderer
		if (!R) return
		try {
			R.setAutoBlink(this.autoBlink)
			R.setAutoBreath(this.autoBreath)
			R.setPhysics(this.physics)
			R.setFps(this.fps)
			R.setZoom(this.zoom)
			R.setAnchor(this.anchor.x, this.anchor.y)
			if (this.mouseFollow) R.enableMouseFollow()
			if (this.idleActive) R.startIdle()
			R.setBlinkInterval(this.blinkInterval)
			R.setGravity(this.gravity.x, this.gravity.y)
			R.setTimeScale(this.timeScale)
		} catch (error) {
			logFire("warn", t("log.l2d.applyCachedFailed", {error: this.err(error)}))
		}
	}

	private assureRenderer(caller: string): boolean {
		if (!this.renderer) {
			logFire("warn", t("log.l2d.assureRendererFailed", {caller}))
			return false
		}
		return true
	}

	private setState(next: Live2DModelState): void {
		if (this.state === next) return
		logFire("info", t("log.l2d.stateChange", {prev: this.state, next}))
		this.state = next
		this.emit("state:change", {state: next})
	}

	private emit<K extends Live2DEventName>(event: K, payload: Live2DEventMap[K]): void {
		const set = this.listeners.get(event)
		if (!set) return
		for (const cb of set) {
			try {
				cb(payload)
			} catch (error) {
				logFire("error", t("log.l2d.eventListenerError", {event, error: this.err(error)}))
			}
		}
	}

	private err(error: unknown): string {
		if (error instanceof Error) return error.message
		return String(error)
	}
}

export const live2dController = new Live2DController()
