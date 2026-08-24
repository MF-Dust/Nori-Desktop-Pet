import {createApp, h} from "vue"
import {afterEach, beforeEach, describe, expect, it, vi} from "vitest"
import type {UiSnapshot} from "../../src/services/runtime/types"

const FEEDBACK_ERROR = vi.hoisted(() => vi.fn())

vi.mock("../../src/services/feedback", () => ({
	feedback: {error: FEEDBACK_ERROR},
}))

import {useSnapshotSave, type ManagedSnapshotField} from "../../src/composables/useSnapshotSave"
import {RUNTIME} from "../../src/services/runtime"

interface TestFields {
	first: ManagedSnapshotField<string>
	second: ManagedSnapshotField<string>
}

const SNAPSHOT = {
	general: {language: "initial"},
} as unknown as UiSnapshot

function mountFields(saver: (key: string, value: string) => Promise<void>) {
	const FIELDS = {} as TestFields
	const ROOT = document.createElement("div")
	const APP = createApp({
		setup() {
			const MANAGER = useSnapshotSave({
				onError: (key, error) => FEEDBACK_ERROR(key, error),
			})
			FIELDS.first = MANAGER.defineField("first", snapshot => snapshot.general.language, "initial", value => saver("first", value))
			FIELDS.second = MANAGER.defineField("second", snapshot => snapshot.general.language, "initial", value => saver("second", value))
			return () => h("div")
		},
	})
	APP.mount(ROOT)
	return {APP, ROOT, FIELDS}
}

describe("useSnapshotSave", () => {
	beforeEach(() => {
		vi.useFakeTimers()
		FEEDBACK_ERROR.mockReset()
		RUNTIME.snapshot.value = SNAPSHOT
	})

	afterEach(() => {
		RUNTIME.snapshot.value = null
		vi.useRealTimers()
	})

	it("keeps each field timer independent", async () => {
		const SAVED: string[] = []
		const {APP, FIELDS} = mountFields(async (key, value) => {
			SAVED.push(`${key}:${value}`)
		})

		FIELDS.first.value.value = "one"
		FIELDS.first.save()
		FIELDS.second.value.value = "two"
		FIELDS.second.save()

		await vi.advanceTimersByTimeAsync(400)
		expect(SAVED).toEqual(["first:one", "second:two"])
		expect(FIELDS.first.state.value).toBe("saved")
		expect(FIELDS.second.state.value).toBe("saved")
		APP.unmount()
	})

	it("flushes pending saves when the component unmounts", async () => {
		const SAVED: string[] = []
		const {APP, FIELDS} = mountFields(async (key, value) => {
			SAVED.push(`${key}:${value}`)
		})

		FIELDS.first.value.value = "pending"
		FIELDS.first.save()
		APP.unmount()
		await Promise.resolve()

		expect(SAVED).toEqual(["first:pending"])
	})

	it("resets a failed field and exposes the error", async () => {
		const {APP, FIELDS} = mountFields(async () => {
			throw new Error("write failed")
		})

		FIELDS.first.value.value = "local"
		await FIELDS.first.saveNow()
		await Promise.resolve()

		expect(FIELDS.first.value.value).toBe("initial")
		expect(FIELDS.first.state.value).toBe("error")
		expect(FIELDS.first.error.value).toBe("write failed")
		expect(FEEDBACK_ERROR).toHaveBeenCalledWith("first", expect.any(Error))
		APP.unmount()
	})
})
