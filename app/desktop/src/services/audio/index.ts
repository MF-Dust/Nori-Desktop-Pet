/**
 * 前端音频宿主
 *
 * 音频从后端下沉到 WebView: 三平台共用一套 WebAudio / MediaRecorder 实现,
 * 不再依赖 NAudio (只有 Windows)。
 *
 * 数据流:
 *   TTS   宿主 → nori:audio-play {url}  → fetch → AudioBuffer → 播放
 *                每 ~60ms 回传 RMS (audio_level) 驱动桌宠口型
 *                播完/失败回报 audio_playback_finished
 *   录音  宿主 → nori:audio-record-start {uploadUrl} → MediaRecorder
 *                nori:audio-record-stop → POST 音频到 uploadUrl
 *
 * 只在 main 窗口装载 (关闭只隐藏, 生命周期内始终存在)。
 */
import {invoke} from "../host/invoke"
import {listen, type UnlistenFn} from "../host/event"

/** 音量采样间隔 (ms), 与原生后端一致 */
const LEVEL_INTERVAL_MS = 60

interface PlayPayload {
	token: string
	url: string
	mime: string
	volume: number
}

interface RecordStartPayload {
	token: string
	uploadUrl: string
}

let context: AudioContext | null = null
let gain: GainNode | null = null
let analyser: AnalyserNode | null = null
let source: AudioBufferSourceNode | null = null
let levelTimer: ReturnType<typeof setInterval> | null = null
let currentToken = ""

let recorder: MediaRecorder | null = null
let recorderChunks: Blob[] = []
let recorderStream: MediaStream | null = null
let recordUploadUrl = ""

const ensureContext = (): AudioContext => {
	if (!context) {
		context = new AudioContext()
		gain = context.createGain()
		analyser = context.createAnalyser()
		analyser.fftSize = 1024
		gain.connect(analyser)
		analyser.connect(context.destination)
	}
	return context
}

/** 当前 RMS 音量 (0~1) */
const readLevel = (): number => {
	if (!analyser) return 0
	const BUFFER = new Float32Array(analyser.fftSize)
	analyser.getFloatTimeDomainData(BUFFER)
	let sum = 0
	for (const sample of BUFFER) sum += sample * sample
	// RMS 落在 0~0.3 区间居多, 乘 3 拉到桌宠口型可用的动态范围
	return Math.min(1, Math.sqrt(sum / BUFFER.length) * 3)
}

const stopLevelTimer = () => {
	if (levelTimer) clearInterval(levelTimer)
	levelTimer = null
}

const reportFinished = (token: string, error?: string) => {
	if (!token) return
	void invoke("audio_playback_finished", error ? {token, error} : {token}).catch(() => {
		/* 宿主已退出时忽略 */
	})
}

/** 停止当前播放 (不回报, 由调用方决定) */
const stopPlayback = () => {
	stopLevelTimer()
	if (source) {
		source.onended = null
		try {
			source.stop()
		} catch {
			/* 已经停了 */
		}
		source.disconnect()
		source = null
	}
	void invoke("audio_level", {level: 0}).catch(() => {})
}

const play = async (payload: PlayPayload): Promise<void> => {
	stopPlayback()
	currentToken = payload.token

	try {
		const CONTEXT = ensureContext()
		// 自动播放策略: 用户没交互过时上下文是 suspended
		if (CONTEXT.state === "suspended") await CONTEXT.resume()

		const RESPONSE = await fetch(payload.url, {cache: "no-store"})
		if (!RESPONSE.ok) throw new Error(`音频下载失败: HTTP ${RESPONSE.status}`)
		const BUFFER = await CONTEXT.decodeAudioData(await RESPONSE.arrayBuffer())

		if (gain) gain.gain.value = Math.min(1, Math.max(0, payload.volume))

		const NODE = CONTEXT.createBufferSource()
		NODE.buffer = BUFFER
		NODE.connect(gain as GainNode)
		source = NODE

		const TOKEN = payload.token
		NODE.onended = () => {
			if (currentToken !== TOKEN) return
			stopLevelTimer()
			source = null
			currentToken = ""
			void invoke("audio_level", {level: 0}).catch(() => {})
			reportFinished(TOKEN)
		}

		levelTimer = setInterval(() => {
			void invoke("audio_level", {level: readLevel()}).catch(() => {})
		}, LEVEL_INTERVAL_MS)

		NODE.start()
	} catch (error) {
		const TOKEN = payload.token
		currentToken = ""
		stopPlayback()
		reportFinished(TOKEN, error instanceof Error ? error.message : String(error))
	}
}

const startRecording = async (payload: RecordStartPayload): Promise<void> => {
	recordUploadUrl = payload.uploadUrl
	recorderChunks = []
	try {
		recorderStream = await navigator.mediaDevices.getUserMedia({audio: true})
		recorder = new MediaRecorder(recorderStream)
		recorder.ondataavailable = (event) => {
			if (event.data.size > 0) recorderChunks.push(event.data)
		}
		recorder.start()
	} catch (error) {
		recorder = null
		recorderStream = null
		console.error("启动录音失败:", error)
	}
}

const stopRecording = async (): Promise<void> => {
	const ACTIVE = recorder
	const URL_TARGET = recordUploadUrl
	recorder = null
	recordUploadUrl = ""
	if (!ACTIVE || !URL_TARGET) return

	// 等 MediaRecorder 真正停下来, 否则最后一段数据会丢
	await new Promise<void>((resolve) => {
		ACTIVE.onstop = () => resolve()
		try {
			ACTIVE.stop()
		} catch {
			resolve()
		}
	})

	recorderStream?.getTracks().forEach(track => track.stop())
	recorderStream = null

	const BLOB = new Blob(recorderChunks, {type: recorderChunks[0]?.type || "audio/webm"})
	recorderChunks = []
	try {
		await fetch(URL_TARGET, {method: "POST", body: BLOB})
	} catch (error) {
		console.error("上传录音失败:", error)
	}
}

let unlisteners: UnlistenFn[] = []

/**
 * 安装音频宿主 (仅 main 窗口调用)
 */
export const installAudioHost = async (): Promise<void> => {
	if (unlisteners.length > 0) return
	unlisteners = await Promise.all([
		listen<PlayPayload>("nori:audio-play", ({payload}) => void play(payload)),
		listen("nori:audio-stop", () => {
			currentToken = ""
			stopPlayback()
		}),
		listen<RecordStartPayload>("nori:audio-record-start", ({payload}) => void startRecording(payload)),
		listen("nori:audio-record-stop", () => void stopRecording()),
	])
}

/**
 * 卸载音频宿主
 */
export const uninstallAudioHost = (): void => {
	for (const unlisten of unlisteners) unlisten()
	unlisteners = []
	currentToken = ""
	stopPlayback()
	void stopRecording()
}

/** 供测试使用的纯函数: 由时域样本算 RMS 电平 */
export const computeLevel = (samples: ArrayLike<number>): number => {
	let sum = 0
	for (let index = 0; index < samples.length; index += 1) sum += samples[index] * samples[index]
	if (samples.length === 0) return 0
	return Math.min(1, Math.sqrt(sum / samples.length) * 3)
}
