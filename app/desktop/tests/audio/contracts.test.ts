import {describe, expect, it} from "vitest"
import {
	chooseRecordingMime,
	isSupportedAudioMime,
	normalizeAudioMime,
	recordingFileNameForMime,
} from "../../src/services/audio"

describe("音频 MIME 合同", () => {
	it("保留 MediaRecorder 的 codecs 参数", () => {
		expect(normalizeAudioMime("audio/webm;codecs=opus")).toBe("audio/webm;codecs=opus")
		expect(isSupportedAudioMime("audio/webm;codecs=opus")).toBe(true)
	})

	it("拒绝空值和非音频 MIME", () => {
		expect(isSupportedAudioMime("")).toBe(false)
		expect(isSupportedAudioMime("application/octet-stream")).toBe(false)
		expect(() => normalizeAudioMime("text/plain")).toThrow()
	})

	it("从浏览器能力中选择真实录音类型", () => {
		const MEDIA_RECORDER = {
			isTypeSupported: (mime: string) => mime === "audio/ogg;codecs=opus",
		} as typeof MediaRecorder
		expect(chooseRecordingMime(MEDIA_RECORDER)).toBe("audio/ogg;codecs=opus")
		expect(recordingFileNameForMime("audio/webm;codecs=opus")).toBe("speech.webm")
	})
})
