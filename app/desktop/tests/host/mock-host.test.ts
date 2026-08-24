import {afterEach, describe, expect, it} from "vitest"
import {invoke} from "../../src/services/host/invoke"
import {MockHost} from "../helpers/mockHost"

describe("Mock Host", () => {
	let mock: MockHost

	afterEach(() => {
		mock.restore()
	})

	it("按命令契约记录调用并返回结果", async () => {
		mock = new MockHost({
			clipboard_write_text: () => undefined,
			run_gc_collect: () => ({released_bytes: 42}),
		})
		mock.install()

		await invoke("clipboard_write_text", {text: "hello"})
		const RESULT = await invoke("run_gc_collect")

		expect(RESULT.released_bytes).toBe(42)
		expect(mock.calls).toEqual([
			{command: "clipboard_write_text", args: {text: "hello"}},
			{command: "run_gc_collect", args: undefined},
		])
	})

	it("保留宿主事件的订阅与取消语义", async () => {
		mock = new MockHost({})
		mock.install()
		const EVENTS: unknown[] = []
		const UNLISTEN = mock.host.listen("nori:test", message => EVENTS.push(message.payload))

		mock.dispatch("nori:test", {value: 1})
		UNLISTEN()
		mock.dispatch("nori:test", {value: 2})

		expect(EVENTS).toEqual([{value: 1}])
	})
})
