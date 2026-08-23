import {describe, expect, it} from "vitest"
import {NormalizeOperation, REPLAY_OPTIONS, ShouldEnableTelemetry, TELEMETRY_SAMPLING} from "../../src/services/telemetry/policy"

describe("遥测策略", () => {
	it("固定错误优先的采样率和 Replay 脱敏", () => {
		expect(TELEMETRY_SAMPLING).toEqual({traces: 0.25, replaysSession: 0.05, replaysOnError: 1})
		expect(REPLAY_OPTIONS).toEqual({maskAllText: true, maskAllInputs: true, blockAllMedia: true})
	})

	it("没有 DSN 或用户关闭时不启用 transport", () => {
		expect(ShouldEnableTelemetry("", {available: true, enabled: true})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: false, enabled: true})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: true, enabled: false})).toBe(false)
		expect(ShouldEnableTelemetry("https://example", {available: true, enabled: true})).toBe(true)
	})

	it("操作名不携带路径参数或用户文字", () => {
		expect(NormalizeOperation("route:/chat/用户消息")).toBe("route_chat")
		expect(NormalizeOperation("中文")).toBe("operation")
	})
})
