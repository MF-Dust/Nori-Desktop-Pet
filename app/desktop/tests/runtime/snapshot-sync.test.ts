import {beforeEach, describe, expect, it, vi} from "vitest"
import type {UiSnapshot} from "../../src/services/runtime/types"

type StateChangedHandler = (message: {payload: {version: number; topics: string[]}}) => void

const HOST_INVOKE = vi.hoisted(() => vi.fn())
const HOST_LISTEN = vi.hoisted(() => vi.fn())
const UNLISTEN = vi.hoisted(() => vi.fn())

vi.mock("../../src/services/host/invoke", () => ({invoke: HOST_INVOKE}))
vi.mock("../../src/services/host/event", () => ({listen: HOST_LISTEN}))

const createDeferred = <T,>() => {
	let resolve!: (value: T | PromiseLike<T>) => void
	let reject!: (reason?: unknown) => void
	const promise = new Promise<T>((resolvePromise, rejectPromise) => {
		resolve = resolvePromise
		reject = rejectPromise
	})
	return {promise, resolve, reject}
}

const loadRuntime = async () => {
	vi.resetModules()
	return (await import("../../src/services/runtime")).RUNTIME
}

const SNAPSHOT_ONE = {version: 1, general: {language: "zh-CN"}} as UiSnapshot
const SNAPSHOT_TWO = {version: 2, general: {language: "zh-CN"}} as UiSnapshot
const SNAPSHOT_THREE = {version: 3, general: {language: "zh-CN"}} as UiSnapshot

let stateChangedHandler: StateChangedHandler | null

beforeEach(() => {
	HOST_INVOKE.mockReset()
	HOST_LISTEN.mockReset()
	UNLISTEN.mockReset()
	stateChangedHandler = null
	HOST_LISTEN.mockImplementation(async (_event: string, handler: StateChangedHandler) => {
		stateChangedHandler = handler
		return UNLISTEN
	})
})

describe("运行时快照同步", () => {
	it("先订阅再获取首份快照，并补齐订阅期间的状态广播", async () => {
		const RUNTIME = await loadRuntime()
		const FIRST = createDeferred<UiSnapshot>()
		const SECOND = createDeferred<UiSnapshot>()
		const FIRST_INVOKED = createDeferred<void>()
		const SECOND_INVOKED = createDeferred<void>()
		const ORDER: string[] = []
		let invocationCount = 0

		HOST_LISTEN.mockImplementation(async (_event: string, handler: StateChangedHandler) => {
			ORDER.push("listen")
			stateChangedHandler = handler
			return UNLISTEN
		})
		HOST_INVOKE.mockImplementation(async () => {
			ORDER.push("invoke")
			invocationCount += 1
			if (invocationCount === 1) {
				FIRST_INVOKED.resolve()
				return FIRST.promise
			}
			SECOND_INVOKED.resolve()
			return SECOND.promise
		})

		const INITIALIZATION = RUNTIME.init()
		await FIRST_INVOKED.promise

		expect(ORDER).toEqual(["listen", "invoke"])
		expect(stateChangedHandler).not.toBeNull()
		stateChangedHandler?.({payload: {version: 1, topics: ["config"]}})
		FIRST.resolve(SNAPSHOT_ONE)
		await SECOND_INVOKED.promise
		SECOND.resolve(SNAPSHOT_TWO)
		await INITIALIZATION

		expect(HOST_INVOKE).toHaveBeenCalledTimes(2)
		expect(RUNTIME.snapshot.value).toEqual(SNAPSHOT_TWO)
	})

	it("引导失败会撤销本次监听，并保留 retryInit 的重试能力", async () => {
		const RUNTIME = await loadRuntime()
		const FAILURE = new Error("快照失败")
		HOST_INVOKE.mockRejectedValueOnce(FAILURE).mockResolvedValue(SNAPSHOT_ONE)

		await expect(RUNTIME.init()).rejects.toBe(FAILURE)
		expect(UNLISTEN).toHaveBeenCalledOnce()
		expect(RUNTIME.bootstrapError.value).toBe(FAILURE)

		await RUNTIME.retryInit()

		expect(HOST_LISTEN).toHaveBeenCalledTimes(2)
		expect(HOST_INVOKE).toHaveBeenCalledTimes(2)
		expect(UNLISTEN).toHaveBeenCalledOnce()
		expect(RUNTIME.snapshot.value).toEqual(SNAPSHOT_ONE)
	})

	it("尾刷新期间的第三次调用会继续排空，并让所有等待者一致完成", async () => {
		const RUNTIME = await loadRuntime()
		const FIRST = createDeferred<UiSnapshot>()
		const SECOND = createDeferred<UiSnapshot>()
		const THIRD = createDeferred<UiSnapshot>()
		const FIRST_INVOKED = createDeferred<void>()
		const SECOND_INVOKED = createDeferred<void>()
		const THIRD_INVOKED = createDeferred<void>()
		let invocationCount = 0

		HOST_INVOKE.mockImplementation(async () => {
			invocationCount += 1
			if (invocationCount === 1) {
				FIRST_INVOKED.resolve()
				return FIRST.promise
			}
			if (invocationCount === 2) {
				SECOND_INVOKED.resolve()
				return SECOND.promise
			}
			THIRD_INVOKED.resolve()
			return THIRD.promise
		})

		const FIRST_REFRESH = RUNTIME.refresh()
		await FIRST_INVOKED.promise
		const SECOND_REFRESH = RUNTIME.refresh()
		expect(SECOND_REFRESH).toBe(FIRST_REFRESH)

		FIRST.resolve(SNAPSHOT_ONE)
		await SECOND_INVOKED.promise
		const THIRD_REFRESH = RUNTIME.refresh()
		expect(THIRD_REFRESH).toBe(FIRST_REFRESH)

		SECOND.resolve(SNAPSHOT_TWO)
		await THIRD_INVOKED.promise
		THIRD.resolve(SNAPSHOT_THREE)
		await expect(Promise.all([FIRST_REFRESH, SECOND_REFRESH, THIRD_REFRESH])).resolves.toEqual([undefined, undefined, undefined])

		expect(HOST_INVOKE).toHaveBeenCalledTimes(3)
		expect(RUNTIME.snapshot.value).toEqual(SNAPSHOT_THREE)
	})
})
