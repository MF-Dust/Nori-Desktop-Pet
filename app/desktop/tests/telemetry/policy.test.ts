import {describe, expect, it} from "vitest"
import {NormalizeOperation, REPLAY_OPTIONS, ShouldEnableTelemetry, TELEMETRY_SAMPLING} from "../../src/services/telemetry/policy"
import {scrubReplayEvent, scrubReplayRecordingEvent} from "../../src/services/telemetry"

describe("遥测策略", () => {
	it("固定错误优先的采样率和 Replay 脱敏", () => {
		expect(TELEMETRY_SAMPLING).toEqual({traces: 0.25, replaysSession: 0.05, replaysOnError: 1})
		expect(REPLAY_OPTIONS).toEqual({maskAllText: true, maskAllInputs: true, blockAllMedia: true})
	})

	it("没有 DSN、不可用、用户关闭或未明确同意时不启用 transport", () => {
		expect(ShouldEnableTelemetry("", {available: true, enabled: true, consent: "granted"})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: false, enabled: true, consent: "granted"})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: true, enabled: false, consent: "denied"})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: true, enabled: true, consent: "unset"})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: true, enabled: true, consent: "granted"})).toBe(true)
	})

	it("操作名不携带路径参数或用户文字", () => {
		expect(NormalizeOperation("route:/chat/用户消息")).toBe("route_chat")
		expect(NormalizeOperation("中文")).toBe("operation")
	})

	it("Replay 事件清理路径和查询参数但保留关联 ID", () => {
		const event = scrubReplayEvent({
			type: "replay_event",
			initialUrl: "file:///C:/Users/name/app/index.html?window=main",
			replay_id: "replay-123",
			data: {url: "http://127.0.0.1:1234/secret?token=x"},
		} as never) as unknown as Record<string, unknown>
		expect(event.initialUrl).toBe("app://webview")
		expect(event.replay_id).toBe("replay-123")
		expect((event.data as Record<string, unknown>).url).toBe("app://webview")
	})

	it("Replay recording 自定义事件不保留外部 URL", () => {
		const event = scrubReplayRecordingEvent({href: "https://example.test/path?q=secret"}) as Record<string, unknown>
		expect(event.href).toBe("app://webview")
	})
})
