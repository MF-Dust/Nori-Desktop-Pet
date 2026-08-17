import type {
	ExpressionName,
	Live2DParameter,
	ModelId,
	MotionGroupName,
	MotionIndex,
} from "./types"

export interface Live2DRenderer {
	readonly id: string

	mount(canvas: HTMLCanvasElement): Promise<void>
	unmount(): Promise<void>
	destroy(): void

	loadModel(modelId: ModelId, model3Path: string): Promise<void>
	unloadModel(): Promise<void>
	reloadModel(): Promise<void>

	listMotions(): Record<MotionGroupName, MotionIndex[]>
	playMotion(group: MotionGroupName, index?: MotionIndex): Promise<boolean>
	stopMotion(): Promise<void>
	isMotionPlaying(): boolean

	listExpressions(): ExpressionName[]
	setExpression(name: ExpressionName): Promise<boolean>
	clearExpression(): Promise<void>
	getCurrentExpression(): ExpressionName | null

	getParameter(id: string): number | null
	setParameter(id: string, value: number): void
	getAllParameters(): Live2DParameter[]
	resetParameters(): void

	startIdle(): void
	stopIdle(): void
	isIdleActive(): boolean

	enableMouseFollow(): void
	disableMouseFollow(): void
	isMouseFollowEnabled(): boolean
	setLookAt(x: number, y: number): void

	resize(width: number, height: number): void
	setZoom(scale: number): void
	setAnchor(x: number, y: number): void
	focus(): void

	setAutoBlink(enabled: boolean): void
	setAutoBreath(enabled: boolean): void
	setPhysics(enabled: boolean): void
	setFps(fps: number): void

	setLipSync(value: number): void

	isReady(): boolean

	setEyeOpen(open: number): void
	setBlinkInterval(seconds: number): void
	setHeadPose(x: number, y: number, z: number): void
	setBodyPose(x: number, y: number, z: number): void
	setMouthOpen(open: number): void
	setEyebrowState(left: number, right: number): void
	setHandGesture(name: string): void
	getPartIds(): string[]
	setPartOpacity(id: string, opacity: number): void
	getPartOpacity(id: string): number
	setGravity(x: number, y: number): void
	setTimeScale(scale: number): void
	captureScreenshot(): Promise<string | null>
	setAccessoryVisible(name: string, visible: boolean): void
}

export type Live2DRendererFactory = (canvas: HTMLCanvasElement) => Live2DRenderer
