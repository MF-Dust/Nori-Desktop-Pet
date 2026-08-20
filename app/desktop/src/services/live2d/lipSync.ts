/**
 * Live2D 声音口型同步 (Lip-Sync) 分析器
 *
 * 通过 Web Audio AnalyserNode 实时获取音频能量并驱动 Live2D 模型嘴形张合
 */
export class LipSyncAnalyzer {
	private analyserNode: AnalyserNode | null = null
	private animationFrameId: number | null = null
	private isRunning = false
	private currentMouthOpen = 0
	private onMouthUpdate?: (value: number) => void

	/**
	 * 初始化分析器并绑定到 AudioContext
	 */
	public attach(audioContext: AudioContext, sourceNode: AudioNode, onMouthUpdate?: (value: number) => void): void {
		this.detach()
		this.onMouthUpdate = onMouthUpdate

		this.analyserNode = audioContext.createAnalyser()
		this.analyserNode.fftSize = 256
		this.analyserNode.smoothingTimeConstant = 0.2

		sourceNode.connect(this.analyserNode)
		this.startAnalysis()
	}

	/**
	 * 开始采样分析循环
	 */
	private startAnalysis(): void {
		if (this.isRunning) return
		this.isRunning = true

		const BUFFER_LENGTH = this.analyserNode ? this.analyserNode.frequencyBinCount : 0
		const DATA_ARRAY = new Uint8Array(BUFFER_LENGTH)

		const tick = () => {
			if (!this.isRunning || !this.analyserNode) return

			this.analyserNode.getByteTimeDomainData(DATA_ARRAY)

			// 计算音量能量 (RMS)
			let sum = 0
			for (let i = 0; i < BUFFER_LENGTH; i++) {
				const V = (DATA_ARRAY[i] - 128) / 128
				sum += V * V
			}
			const RMS = Math.sqrt(sum / BUFFER_LENGTH)

			// 映射到嘴形张开度 0.0 ~ 1.0 (灵敏度放大)
			const TARGET = Math.min(1, Math.max(0, RMS * 3.5))

			// 平滑插值 (避免嘴部瞬时闪烁抖动)
			this.currentMouthOpen += (TARGET - this.currentMouthOpen) * 0.45
			if (this.currentMouthOpen < 0.02) this.currentMouthOpen = 0

			if (this.onMouthUpdate) {
				this.onMouthUpdate(this.currentMouthOpen)
			}

			this.animationFrameId = requestAnimationFrame(tick)
		}

		this.animationFrameId = requestAnimationFrame(tick)
	}

	/**
	 * 停止分析并平滑闭嘴
	 */
	public detach(): void {
		this.isRunning = false
		if (this.animationFrameId !== null) {
			cancelAnimationFrame(this.animationFrameId)
			this.animationFrameId = null
		}
		if (this.analyserNode) {
			try {
				this.analyserNode.disconnect()
			} catch {
				/* 忽略已断开 */
			}
			this.analyserNode = null
		}
		this.currentMouthOpen = 0
		if (this.onMouthUpdate) {
			this.onMouthUpdate(0)
		}
	}

	/**
	 * 获取当前嘴形张开度 (0.0 ~ 1.0)
	 */
	public getMouthOpen(): number {
		return this.currentMouthOpen
	}
}

/**
 * 全局口型分析器单例
 */
export const lipSyncAnalyzer = new LipSyncAnalyzer()

/**
 * 将 LipSyncAnalyzer 连接到 Live2D 控制器
 * @param setMouthOpen 控制器 setMouthOpen 方法
 * @param setNowSpeaking 控制器 setNowSpeaking 方法
 */
export const connectLipSyncToController = (
	setMouthOpen: (value: number) => void,
	setNowSpeaking: (speaking: boolean) => void,
): LipSyncAnalyzer["attach"] => {
	return (audioContext: AudioContext, sourceNode: AudioNode) => {
		lipSyncAnalyzer.attach(audioContext, sourceNode, (value) => {
			setMouthOpen(value)
			setNowSpeaking(value > 0.02)
		})
	}
}
