export type ModelId = string

export type MotionGroupName = string

export type MotionIndex = number

export type ExpressionName = string

export type EmotionType =
	| "neutral"
	| "happy"
	| "angry"
	| "sad"
	| "surprised"
	| "doubt"
	| "shy"
	| "troubled"
	| "dizzy"
	| "serious"
	| "disgust"
	| "speechless"
	| (string & {})

export interface Live2DParameter {
	id: string
	value: number
	min?: number
	max?: number
	default?: number
}

export type Live2DModelState =
	| "unmounted"
	| "loading"
	| "ready"
	| "missing"
	| "error"

export interface Live2DModelEntry {
	id: ModelId
	name: string
	thumb: string
	installed: boolean
}

export type MotionMap = Record<MotionGroupName, MotionIndex[]>

export interface Live2DEventMap {
	"model:loading": { model: ModelId }
	"model:loaded": { model: ModelId }
	"model:unloaded": { model: ModelId }
	"model:error": { model: ModelId; error: string }
	"motion:start": { group: MotionGroupName; index: MotionIndex }
	"motion:end": { group: MotionGroupName; index: MotionIndex }
	"expression:change": { expression: ExpressionName | null }
	"emotion:change": { emotion: EmotionType; intensity: number }
	"state:change": { state: Live2DModelState }
	"ready": void
	"error": { error: string }
	"speak": { text: string; options: { lang?: string; rate?: number; pitch?: number } }
}

export type Live2DEventName = keyof Live2DEventMap

export type Live2DEListener<K extends Live2DEventName> = (payload: Live2DEventMap[K]) => void
