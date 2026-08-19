import {invoke} from "../host/invoke"

/**
 * 语音识别回调
 */
export interface SttListeningCallbacks {
	onInterim?: (text: string) => void
	onFinal?: (text: string) => void
	onError?: (error: Error) => void
}

/**
 * STT 适配器接口
 */
export interface ISttProvider {
	name: string
	start(callbacks: SttListeningCallbacks): Promise<void>
	stop(): Promise<string>
	isListening(): boolean
}

/**
 * 1. Web Speech API 浏览器内置语音识别适配器
 */
export class WebSpeechSttProvider implements ISttProvider {
	public readonly name = "web_speech"
	private recognition: any = null
	private listening = false
	private finalResult = ""

	public async start(callbacks: SttListeningCallbacks): Promise<void> {
		if (typeof window === "undefined") {
			throw new Error("当前环境不支持语音识别")
		}

		const SpeechRecognitionConstructor =
			(window as any).SpeechRecognition ||
			(window as any).webkitSpeechRecognition

		if (!SpeechRecognitionConstructor) {
			throw new Error("浏览器不支持 Web Speech 语音识别 API")
		}

		this.finalResult = ""
		this.recognition = new SpeechRecognitionConstructor()
		this.recognition.lang = "zh-CN"
		this.recognition.continuous = true
		this.recognition.interimResults = true

		this.recognition.onstart = () => {
			this.listening = true
		}

		this.recognition.onresult = (event: any) => {
			let interimText = ""
			for (let i = event.resultIndex; i < event.results.length; i++) {
				const RESULT = event.results[i]
				const TRANSCRIPT = RESULT[0]?.transcript || ""
				if (RESULT.isFinal) {
					this.finalResult += TRANSCRIPT
					if (callbacks.onFinal) callbacks.onFinal(this.finalResult)
				} else {
					interimText += TRANSCRIPT
					if (callbacks.onInterim) callbacks.onInterim(interimText)
				}
			}
		}

		this.recognition.onerror = (event: any) => {
			this.listening = false
			if (callbacks.onError) {
				callbacks.onError(new Error(`语音识别错误: ${event.error}`))
			}
		}

		this.recognition.onend = () => {
			this.listening = false
		}

		this.recognition.start()
	}

	public async stop(): Promise<string> {
		if (this.recognition && this.listening) {
			this.recognition.stop()
		}
		this.listening = false
		return this.finalResult
	}

	public isListening(): boolean {
		return this.listening
	}
}

/**
 * 2. OpenAI Whisper 录音识别适配器 (/v1/audio/transcriptions)
 */
export class WhisperSttProvider implements ISttProvider {
	public readonly name = "whisper"
	private mediaRecorder: MediaRecorder | null = null
	private audioChunks: Blob[] = []
	private listening = false
	private stream: MediaStream | null = null

	public async start(callbacks: SttListeningCallbacks): Promise<void> {
		if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
			throw new Error("浏览器不支持麦克风录音访问")
		}

		this.audioChunks = []
		this.stream = await navigator.mediaDevices.getUserMedia({audio: true})
		this.mediaRecorder = new MediaRecorder(this.stream)

		this.mediaRecorder.ondataavailable = (event) => {
			if (event.data.size > 0) {
				this.audioChunks.push(event.data)
			}
		}

		this.mediaRecorder.onerror = (event) => {
			this.listening = false
			if (callbacks.onError) {
				callbacks.onError(new Error(`录音错误: ${event}`))
			}
		}

		this.mediaRecorder.start(200)
		this.listening = true
	}

	public async stop(): Promise<string> {
		if (!this.mediaRecorder || !this.listening) return ""

		return new Promise((resolve, reject) => {
			if (!this.mediaRecorder) {
				resolve("")
				return
			}

			this.mediaRecorder.onstop = async () => {
				this.listening = false
				if (this.stream) {
					this.stream.getTracks().forEach((t) => t.stop())
					this.stream = null
				}

				try {
					const AUDIO_BLOB = new Blob(this.audioChunks, {type: "audio/webm"})
					const [BASE_URL, API_KEY] = await Promise.all([
						invoke<string | null>("get_config", {key: "stt_base_url"}),
						invoke<string | null>("get_config", {key: "stt_api_key"}),
					])

					let endpoint = (BASE_URL || "https://api.openai.com/v1").trim()
					if (endpoint.endsWith("/")) endpoint = endpoint.slice(0, -1)
					if (!endpoint.endsWith("/audio/transcriptions")) {
						endpoint = `${endpoint}/audio/transcriptions`
					}

					const FORM = new FormData()
					FORM.append("file", AUDIO_BLOB, "speech.webm")
					FORM.append("model", "whisper-1")
					FORM.append("language", "zh")

					const RES = await fetch(endpoint, {
						method: "POST",
						headers: {
							Authorization: `Bearer ${API_KEY || ""}`,
						},
						body: FORM,
					})

					if (!RES.ok) {
						const ERR_TEXT = await RES.text().catch(() => "")
						throw new Error(`Whisper 识别失败: HTTP ${RES.status} ${ERR_TEXT}`)
					}

					const DATA = (await RES.json()) as {text?: string}
					resolve(DATA.text || "")
				} catch (error) {
					reject(error)
				}
			}

			this.mediaRecorder.stop()
		})
	}

	public isListening(): boolean {
		return this.listening
	}
}

/**
 * 全局 STT 语音识别服务
 */
export class SttService {
	private providers: Map<string, ISttProvider> = new Map()
	private activeProvider: ISttProvider | null = null

	constructor() {
		this.register(new WebSpeechSttProvider())
		this.register(new WhisperSttProvider())
	}

	public register(provider: ISttProvider): void {
		this.providers.set(provider.name, provider)
	}

	/**
	 * 开始语音识别
	 */
	public async startListening(callbacks: SttListeningCallbacks = {}): Promise<void> {
		const PROVIDER_NAME = (await invoke<string | null>("get_config", {key: "stt_provider"})) || "web_speech"
		const PROVIDER = this.providers.get(PROVIDER_NAME) || this.providers.get("web_speech")
		if (!PROVIDER) throw new Error(`未找到语音识别提供商: ${PROVIDER_NAME}`)

		this.activeProvider = PROVIDER
		await PROVIDER.start(callbacks)
	}

	/**
	 * 停止语音识别并返回最终文本
	 */
	public async stopListening(): Promise<string> {
		if (!this.activeProvider) return ""
		const RESULT = await this.activeProvider.stop()
		this.activeProvider = null
		return RESULT
	}

	/**
	 * 是否正在监听
	 */
	public isListening(): boolean {
		return this.activeProvider?.isListening() ?? false
	}
}

/**
 * 全局 STT 语音识别单例
 */
export const sttService = new SttService()
