import {invoke} from "../host/invoke"
import {audioService} from "../audio"

/**
 * TTS 合成选项
 */
export interface TtsSynthesizeOptions {
	voice?: string
	speed?: number
	pitch?: number
	volume?: number
}

/**
 * TTS 适配器接口
 */
export interface ITtsProvider {
	name: string
	synthesize(text: string, options?: TtsSynthesizeOptions): Promise<ArrayBuffer | Blob | string>
}

/**
 * 1. Web Speech API 浏览器内置合成适配器
 */
export class WebSpeechTtsProvider implements ITtsProvider {
	public readonly name = "web_speech"

	public async synthesize(text: string, options: TtsSynthesizeOptions = {}): Promise<string> {
		if (typeof window === "undefined" || !("speechSynthesis" in window)) {
			throw new Error("当前环境不支持 Web Speech API")
		}

		return new Promise((resolve, reject) => {
			const UTTERANCE = new SpeechSynthesisUtterance(text)
			UTTERANCE.lang = "zh-CN"
			if (options.speed) UTTERANCE.rate = options.speed
			if (options.pitch) UTTERANCE.pitch = options.pitch
			if (options.volume) UTTERANCE.volume = options.volume

			// 匹配指定 voice
			if (options.voice) {
				const VOICES = window.speechSynthesis.getVoices()
				const MATCH = VOICES.find((v) => v.name === options.voice)
				if (MATCH) UTTERANCE.voice = MATCH
			}

			UTTERANCE.onstart = () => {
				// 开始朗读
			}
			UTTERANCE.onend = () => {
				resolve("")
			}
			UTTERANCE.onerror = (err) => {
				reject(new Error(`Web Speech 合成失败: ${err.error}`))
			}

			window.speechSynthesis.speak(UTTERANCE)
		})
	}
}

/**
 * 2. OpenAI 兼容 TTS 适配器 (/v1/audio/speech)
 */
export class OpenAiTtsProvider implements ITtsProvider {
	public readonly name = "openai"

	public async synthesize(text: string, options: TtsSynthesizeOptions = {}): Promise<ArrayBuffer> {
		const [BASE_URL, API_KEY] = await Promise.all([
			invoke<string | null>("get_config", {key: "tts_base_url"}),
			invoke<string | null>("get_config", {key: "tts_api_key"}),
		])

		let endpoint = (BASE_URL || "https://api.openai.com/v1").trim().replace(/\/+$/, "")
		if (!endpoint.endsWith("/audio/speech")) {
			endpoint = `${endpoint}/audio/speech`
		}

		const PAYLOAD = {
			model: "tts-1",
			input: text,
			voice: options.voice || "nova",
			speed: options.speed || 1.0,
		}

		const RES = await fetch(endpoint, {
			method: "POST",
			headers: {
				"Content-Type": "application/json",
				Authorization: `Bearer ${API_KEY || ""}`,
			},
			body: JSON.stringify(PAYLOAD),
		})

		if (!RES.ok) {
			const ERR_TEXT = await RES.text().catch(() => "")
			throw new Error(`OpenAI TTS 请求失败: HTTP ${RES.status} ${ERR_TEXT}`)
		}

		return RES.arrayBuffer()
	}
}

/**
 * 3. 自定义 HTTP TTS 适配器
 */
export class CustomHttpTtsProvider implements ITtsProvider {
	public readonly name = "custom"

	public async synthesize(text: string, options: TtsSynthesizeOptions = {}): Promise<ArrayBuffer> {
		const ENDPOINT = await invoke<string | null>("get_config", {key: "tts_base_url"})
		if (!ENDPOINT) throw new Error("未配置自定义 TTS 请求端点 URL")

		const RES = await fetch(ENDPOINT, {
			method: "POST",
			headers: {
				"Content-Type": "application/json",
			},
			body: JSON.stringify({
				text,
				voice: options.voice,
				speed: options.speed,
				pitch: options.pitch,
			}),
		})

		if (!RES.ok) {
			throw new Error(`自定义 TTS 请求失败: HTTP ${RES.status}`)
		}

		return RES.arrayBuffer()
	}
}

/**
 * 全局 TTS 服务
 */
export class TtsService {
	private providers: Map<string, ITtsProvider> = new Map()

	constructor() {
		this.register(new WebSpeechTtsProvider())
		this.register(new OpenAiTtsProvider())
		this.register(new CustomHttpTtsProvider())
	}

	/**
	 * 注册 Provider
	 */
	public register(provider: ITtsProvider): void {
		this.providers.set(provider.name, provider)
	}

	/**
	 * 获取指定 Provider
	 */
	public getProvider(name: string): ITtsProvider | undefined {
		return this.providers.get(name)
	}

	/**
	 * 朗读文本
	 */
	public async speak(text: string, options?: TtsSynthesizeOptions): Promise<void> {
		if (!text.trim()) return

		// 读取 TTS 配置
		const PROVIDER_NAME = (await invoke<string | null>("get_config", {key: "tts_provider"})) || "web_speech"
		const PROVIDER = this.providers.get(PROVIDER_NAME) || this.providers.get("web_speech")
		if (!PROVIDER) throw new Error(`未找到 TTS 提供商: ${PROVIDER_NAME}`)

		const [SAVED_VOICE, SAVED_SPEED] = await Promise.all([
			invoke<string | null>("get_config", {key: "tts_voice"}),
			invoke<string | null>("get_config", {key: "tts_speed"}),
		])

		const MERGED_OPTIONS: TtsSynthesizeOptions = {
			voice: options?.voice ?? SAVED_VOICE ?? undefined,
			speed: options?.speed ?? (SAVED_SPEED ? parseFloat(SAVED_SPEED) : 1.0),
			pitch: options?.pitch ?? 1.0,
			volume: options?.volume ?? audioService.getVolume(),
		}

		// Web Speech 已经由浏览器内部调度
		if (PROVIDER.name === "web_speech") {
			await PROVIDER.synthesize(text, MERGED_OPTIONS)
			return
		}

		// 其他 Provider 产出 ArrayBuffer 推入 AudioService 播放队列
		const BUFFER = await PROVIDER.synthesize(text, MERGED_OPTIONS)
		audioService.enqueue({
			source: BUFFER,
		})
	}

	/**
	 * 停止朗读
	 */
	public stop(): void {
		if (typeof window !== "undefined" && "speechSynthesis" in window) {
			window.speechSynthesis.cancel()
		}
		audioService.stop()
	}
}

/**
 * 全局 TTS 服务单例
 */
export const ttsService = new TtsService()
