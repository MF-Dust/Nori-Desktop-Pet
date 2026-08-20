import {invoke} from "../host/invoke"

/**
 * 播放状态
 */
export type AudioPlaybackState = "idle" | "playing" | "paused"

/**
 * 音频队列项
 */
export interface AudioQueueItem {
	id: string
	source: string | Blob | ArrayBuffer
	onStart?: () => void
	onEnd?: () => void
	onError?: (error: Error) => void
}

/**
 * 音频服务接口
 */
export class AudioService {
	private audioContext: AudioContext | null = null
	private currentAudio: HTMLAudioElement | null = null
	private currentSourceNode: AudioBufferSourceNode | null = null
	private gainNode: GainNode | null = null
	private volume = 1.0
	private state: AudioPlaybackState = "idle"
	private queue: AudioQueueItem[] = []
	private isProcessingQueue = false
	private stateListeners: Set<(state: AudioPlaybackState) => void> = new Set()

	constructor() {
		this.initVolume()
	}

	/**
	 * 初始化读取音量配置
	 */
	private async initVolume(): Promise<void> {
		try {
			const SAVED = await invoke<string | null>("get_config", {key: "audio_volume"})
			if (SAVED != null) {
				const NUM = parseFloat(SAVED)
				if (!Number.isNaN(NUM) && NUM >= 0 && NUM <= 1) {
					this.volume = NUM
					if (this.gainNode) this.gainNode.gain.value = NUM
					if (this.currentAudio) this.currentAudio.volume = NUM
				}
			}
		} catch {
			/* 读取失败保持默认音量 1.0 */
		}
	}

	/**
	 * 获取或唤醒 AudioContext
	 */
	private getAudioContext(): AudioContext {
		if (!this.audioContext) {
			const AudioCtx = window.AudioContext || (window as unknown as {webkitAudioContext: typeof AudioContext}).webkitAudioContext
			this.audioContext = new AudioCtx()
			this.gainNode = this.audioContext.createGain()
			this.gainNode.gain.value = this.volume
			this.gainNode.connect(this.audioContext.destination)
		}
		if (this.audioContext.state === "suspended") {
			void this.audioContext.resume()
		}
		return this.audioContext
	}

	/**
	 * 更新并广播状态
	 */
	private setState(newState: AudioPlaybackState): void {
		if (this.state === newState) return
		this.state = newState
		for (const listener of this.stateListeners) {
			try {
				listener(newState)
			} catch (error) {
				console.error("Audio state listener error:", error)
			}
		}
	}

	/**
	 * 监听状态改变
	 */
	public onStateChange(listener: (state: AudioPlaybackState) => void): () => void {
		this.stateListeners.add(listener)
		return () => {
			this.stateListeners.delete(listener)
		}
	}

	/**
	 * 获取当前活跃的 AudioBufferSourceNode (用于口型同步分析)
	 */
	public getActiveSourceNode(): AudioBufferSourceNode | null {
		return this.currentSourceNode
	}

	/**
	 * 获取当前 AudioContext (可能为 null, 用于口型同步分析)
	 */
	public getAudioContextRef(): AudioContext | null {
		return this.audioContext
	}

	/**
	 * 获取当前播放状态
	 */
	public getState(): AudioPlaybackState {
		return this.state
	}

	/**
	 * 设置全局音量 (0.0 ~ 1.0)
	 */
	public setVolume(volume: number): void {
		const CLAMPED = Math.max(0, Math.min(1, volume))
		this.volume = CLAMPED
		if (this.gainNode) {
			this.gainNode.gain.value = CLAMPED
		}
		if (this.currentAudio) {
			this.currentAudio.volume = CLAMPED
		}
		invoke("set_config", {key: "audio_volume", value: String(CLAMPED)}).catch(() => {})
	}

	/**
	 * 获取当前音量
	 */
	public getVolume(): number {
		return this.volume
	}

	/**
	 * 播放单个音频源 (URL, Blob 或 ArrayBuffer)
	 */
	public async play(source: string | Blob | ArrayBuffer): Promise<void> {
		this.stop()
		return this.playInternal(source)
	}

	/**
	 * 内部播放逻辑
	 */
	private async playInternal(source: string | Blob | ArrayBuffer): Promise<void> {
		if (typeof source === "string") {
			return new Promise((resolve, reject) => {
				const AUDIO = new Audio(source)
				AUDIO.volume = this.volume
				this.currentAudio = AUDIO

				AUDIO.onplay = () => this.setState("playing")
				AUDIO.onended = () => {
					this.currentAudio = null
					this.setState("idle")
					resolve()
				}
				AUDIO.onerror = () => {
					this.currentAudio = null
					this.setState("idle")
					reject(new Error("音频加载或播放失败"))
				}
				AUDIO.play().catch(reject)
			})
		}

		const CTX = this.getAudioContext()
		let buffer: ArrayBuffer

		if (source instanceof Blob) {
			buffer = await source.arrayBuffer()
		} else {
			buffer = source
		}

		const AUDIO_BUFFER = await CTX.decodeAudioData(buffer.slice(0))
		return new Promise((resolve) => {
			const SOURCE_NODE = CTX.createBufferSource()
			SOURCE_NODE.buffer = AUDIO_BUFFER
			if (this.gainNode) {
				SOURCE_NODE.connect(this.gainNode)
			} else {
				SOURCE_NODE.connect(CTX.destination)
			}
			this.currentSourceNode = SOURCE_NODE
			this.setState("playing")

			SOURCE_NODE.onended = () => {
				if (this.currentSourceNode === SOURCE_NODE) {
					this.currentSourceNode = null
					this.setState("idle")
				}
				resolve()
			}
			SOURCE_NODE.start(0)
		})
	}

	/**
	 * 将音频推入播放队列 (按顺序播放)
	 */
	public enqueue(item: Omit<AudioQueueItem, "id"> & {id?: string}): string {
		const ID = item.id || `audio-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
		this.queue.push({
			id: ID,
			source: item.source,
			onStart: item.onStart,
			onEnd: item.onEnd,
			onError: item.onError,
		})
		void this.processQueue()
		return ID
	}

	/**
	 * 批量添加队列
	 */
	public enqueueAll(items: (Omit<AudioQueueItem, "id"> & {id?: string})[]): string[] {
		return items.map((item) => this.enqueue(item))
	}

	/**
	 * 处理音频队列
	 */
	private async processQueue(): Promise<void> {
		if (this.isProcessingQueue) return
		this.isProcessingQueue = true

		while (this.queue.length > 0) {
			const ITEM = this.queue.shift()
			if (!ITEM) break

			try {
				if (ITEM.onStart) ITEM.onStart()
				await this.playInternal(ITEM.source)
				if (ITEM.onEnd) ITEM.onEnd()
			} catch (error) {
				const ERR = error instanceof Error ? error : new Error(String(error))
				if (ITEM.onError) ITEM.onError(ERR)
			}
		}

		this.isProcessingQueue = false
		if (this.queue.length === 0 && this.state !== "paused") {
			this.setState("idle")
		}
	}

	/**
	 * 暂停当前播放
	 */
	public pause(): void {
		if (this.currentAudio && !this.currentAudio.paused) {
			this.currentAudio.pause()
			this.setState("paused")
		} else if (this.audioContext && this.audioContext.state === "running") {
			void this.audioContext.suspend()
			this.setState("paused")
		}
	}

	/**
	 * 恢复播放
	 */
	public resume(): void {
		if (this.currentAudio && this.currentAudio.paused) {
			void this.currentAudio.play()
			this.setState("playing")
		} else if (this.audioContext && this.audioContext.state === "suspended") {
			void this.audioContext.resume()
			this.setState("playing")
		}
	}

	/**
	 * 停止播放并清空队列
	 */
	public stop(): void {
		this.queue = []
		if (this.currentAudio) {
			this.currentAudio.pause()
			this.currentAudio.currentTime = 0
			this.currentAudio = null
		}
		if (this.currentSourceNode) {
			try {
				this.currentSourceNode.stop()
				this.currentSourceNode.disconnect()
			} catch {
				/* 已停止时忽略 */
			}
			this.currentSourceNode = null
		}
		this.setState("idle")
	}

	/**
	 * 清空队列 (不影响当前正在播放的音频)
	 */
	public clearQueue(): void {
		this.queue = []
	}

	/**
	 * 获取队列长度
	 */
	public getQueueLength(): number {
		return this.queue.length
	}
}

/**
 * 全局单例
 */
export const audioService = new AudioService()
