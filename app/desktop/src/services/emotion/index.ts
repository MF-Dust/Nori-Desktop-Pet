import type {EmotionType} from "../agent/protocol"
import {createLive2D} from "../live2d"

/**
 * 情绪状态描述
 */
export interface EmotionState {
	type: EmotionType
	intensity: number
	lastUpdated: number
}

/**
 * 情绪状态管理器
 */
export class EmotionManager {
	private current: EmotionType = "neutral"
	private intensity = 0.5
	private lastUpdated = Date.now()
	private listeners: Set<(state: EmotionState) => void> = new Set()
	private decayInterval: number | null = null

	constructor() {
		this.startDecayLoop()
	}

	/**
	 * 获取当前情绪状态
	 */
	public getState(): EmotionState {
		return {
			type: this.current,
			intensity: this.intensity,
			lastUpdated: this.lastUpdated,
		}
	}

	/**
	 * 更新情绪状态
	 */
	public setEmotion(type: EmotionType, intensity = 0.8): void {
		this.current = type
		this.intensity = Math.max(0, Math.min(1, intensity))
		this.lastUpdated = Date.now()
		this.notify()
		this.applyLive2DEffect(type)
	}

	/**
	 * 订阅情绪变更事件
	 */
	public onChange(listener: (state: EmotionState) => void): () => void {
		this.listeners.add(listener)
		return () => {
			this.listeners.delete(listener)
		}
	}

	/**
	 * 广播状态变更
	 */
	private notify(): void {
		const STATE = this.getState()
		for (const listener of this.listeners) {
			try {
				listener(STATE)
			} catch (error) {
				console.error("Emotion listener error:", error)
			}
		}
	}

	/**
	 * 情绪随时间自然衰减向 neutral (每 20 秒衰减 0.1)
	 */
	private startDecayLoop(): void {
		if (typeof window === "undefined") return
		this.decayInterval = window.setInterval(() => {
			if (this.current !== "neutral") {
				this.intensity -= 0.1
				if (this.intensity <= 0.1) {
					this.current = "neutral"
					this.intensity = 0.5
					this.notify()
				}
			}
		}, 20000)
	}

	/**
	 * 映射情绪到默认 Live2D 表情
	 */
	private applyLive2DEffect(emotion: EmotionType): void {
		const EXP_MAP: Record<EmotionType, string> = {
			neutral: "",
			happy: "Smile",
			sad: "Sad",
			angry: "Angry",
			surprised: "Surprised",
			shy: "Shy",
			sleepy: "Sleepy",
			fond: "Smile",
		}

		const EXP_NAME = EXP_MAP[emotion]
		if (EXP_NAME) {
			try {
				const L2D = createLive2D()
				void L2D.playExpression(EXP_NAME)
			} catch {
				/* 忽略未配置表情 */
			}
		}
	}

	/**
	 * 销毁定时器
	 */
	public destroy(): void {
		if (this.decayInterval !== null) {
			clearInterval(this.decayInterval)
			this.decayInterval = null
		}
		this.listeners.clear()
	}
}

/**
 * 全局情绪状态管理器单例
 */
export const emotionManager = new EmotionManager()
