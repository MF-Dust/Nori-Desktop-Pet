import {beforeEach, describe, expect, it, vi} from "vitest"
import type {UiSnapshot} from "../../src/services/runtime/types"

const HOST_INVOKE = vi.hoisted(() => vi.fn())

vi.mock("../../src/services/host/invoke", () => ({invoke: HOST_INVOKE}))

import {RUNTIME} from "../../src/services/runtime"

const SNAPSHOT = {
	version: 12,
	app: {
		appVersion: "1.2.3",
		productVersion: "1.2.3",
		platform: "windows",
		debugCrashTestsAvailable: false,
		safeMode: true,
	},
	general: {language: "zh-CN"},
} as UiSnapshot

describe("运行时快照", () => {
	beforeEach(() => {
		HOST_INVOKE.mockReset()
		HOST_INVOKE.mockResolvedValue(SNAPSHOT)
	})

	it("读取后端 productVersion、appVersion 与 safeMode 字段", async () => {
		await RUNTIME.refresh()

		expect(HOST_INVOKE).toHaveBeenCalledWith("ui_get_snapshot")
		expect(RUNTIME.snapshot.value?.app.productVersion).toBe("1.2.3")
		expect(RUNTIME.snapshot.value?.app.appVersion).toBe("1.2.3")
		expect(RUNTIME.snapshot.value?.app.safeMode).toBe(true)
	})
})
