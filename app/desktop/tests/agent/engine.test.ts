import {beforeEach, describe, expect, it, vi} from "vitest"

const MOCKS = vi.hoisted(() => ({
	invoke: vi.fn(),
	listen: vi.fn(),
}))

vi.mock("../../src/services/host/invoke", () => ({invoke: MOCKS.invoke}))
vi.mock("../../src/services/host/event", () => ({listen: MOCKS.listen}))
vi.mock("../../src/services/live2d", () => ({petLive2DController: null}))
vi.mock("../../src/services/mcp", () => ({
	mcpService: {syncToolsWithToolManager: vi.fn(), callTool: vi.fn()},
}))
vi.mock("../../src/services/memory", () => ({
	memoryService: {getRelevantMemories: vi.fn(async () => [])},
}))
vi.mock("../../src/services/emotion", () => ({
	emotionManager: {getState: () => ({type: "neutral", intensity: 0.5}), setEmotion: vi.fn()},
}))
vi.mock("../../src/services/tts", () => ({
	ttsService: {speak: vi.fn(async () => {}), stop: vi.fn()},
}))
vi.mock("../../src/services/proactive", () => ({proactiveService: {addReminder: vi.fn()}}))
vi.mock("../../src/services/config", () => ({
	readStringConfig: vi.fn(async (key: string, fallback: string) => {
		const MAP: Record<string, string> = {
			llm_api_base: "https://api.example.com/v1",
			llm_api_key: "test-key",
			llm_model: "test-model",
		}
		return MAP[key] ?? fallback
	}),
	readBooleanConfig: vi.fn(async (_key: string, fallback: boolean) => fallback),
}))

import {AgentEngine} from "../../src/services/agent/engine"
import type {UnlistenFn} from "../../src/services/host/event"

/** 构造可控的 listen mock: 每次注册返回一个纯记录型 spy 清理函数 */
const SETUP_LISTEN = () => {
	const unlistens: ReturnType<typeof vi.fn>[] = []
	MOCKS.listen.mockImplementation(async () => {
		const UNS = vi.fn()
		unlistens.push(UNS)
		return UNS as unknown as UnlistenFn
	})
	return {UNLISTENS: unlistens}
}

/** 排空微任务队列, 让引擎推进到挂起点 */
const FLUSH = async () => {
	for (let i = 0; i < 6; i++) await Promise.resolve()
	await new Promise((resolve) => setTimeout(resolve, 0))
}

describe("AgentEngine 会话隔离与取消", () => {
	beforeEach(() => {
		MOCKS.invoke.mockReset()
		MOCKS.listen.mockReset()
	})

	it("abort 后旧会话不能清理新会话的监听或状态, 且宿主取消携带正确 session ID", async () => {
		const {UNLISTENS} = SETUP_LISTEN()

		// 第一轮: chat_completion_stream 挂起不返回
		let RELEASE_FIRST: ((value: string) => void) | null = null
		MOCKS.invoke.mockImplementation(async (command: string) => {
			if (command === "chat_completion_stream") {
				return new Promise<string>((resolve) => {
					RELEASE_FIRST = resolve
				})
			}
			if (command === "cancel_agent_session") return true
			return null
		})

		const engine = new AgentEngine()
		const FIRST_RUN = engine.run("第一轮", [], {}, {}).catch(() => undefined)
		await FLUSH()

		const STREAM_CALL = MOCKS.invoke.mock.calls.find(([command]) => command === "chat_completion_stream")
		expect(STREAM_CALL).toBeDefined()
		const SESSION1_ID = STREAM_CALL?.[1]?.sessionId as string
		expect(typeof SESSION1_ID).toBe("string")

		// 中止第一轮: 宿主取消命令携带同一 session ID, 本轮监听各被清理一次
		const FIRST_SET = [...UNLISTENS]
		engine.abort()
		expect(MOCKS.invoke).toHaveBeenCalledWith("cancel_agent_session", {sessionId: SESSION1_ID})
		expect(FIRST_SET.every((u) => u.mock.calls.length === 1)).toBe(true)

		// 第二轮正常完成
		MOCKS.invoke.mockImplementation(async (command: string) => {
			if (command === "chat_completion_stream") {
				return JSON.stringify({type: "message", text: "第二轮完成"})
			}
			if (command === "cancel_agent_session") return true
			return null
		})
		const ON_COMPLETE = vi.fn()
		const ON_STATE = vi.fn()
		await engine.run("第二轮", [], {}, {onComplete: ON_COMPLETE, onStateChange: ON_STATE})

		const SECOND_SET = [...UNLISTENS].slice(FIRST_SET.length)
		expect(engine.getState()).toBe("idle")
		expect(ON_COMPLETE).toHaveBeenCalledTimes(1)
		const STATE_AT_SECOND_DONE = ON_STATE.mock.calls.length
		// 第二轮收尾: 内层 finally 与会话 finally 各调用一次
		expect(SECOND_SET.every((u) => u.mock.calls.length === 2)).toBe(true)
		expect(SECOND_SET).toHaveLength(2)

		// 此时才释放第一轮挂起的请求: 旧会话不得改写全局状态或触发新回调
		RELEASE_FIRST?.("{}")
		await FIRST_RUN
		await FLUSH()

		expect(engine.getState()).toBe("idle")
		expect(ON_COMPLETE).toHaveBeenCalledTimes(1)
		expect(ON_STATE.mock.calls.length).toBe(STATE_AT_SECOND_DONE)
		// 第一轮监听只被它自己的取消与内层 finally 清理, 不触碰第二轮
		expect(FIRST_SET.every((u) => u.mock.calls.length === 2)).toBe(true)
		expect(SECOND_SET.every((u) => u.mock.calls.length === 2)).toBe(true)
	})

	it("宿主取消造成的错误被识别为正常中止而不是 error 状态", async () => {
		SETUP_LISTEN()
		let REJECT_FIRST: ((error: unknown) => void) | null = null
		MOCKS.invoke.mockImplementation(async (command: string) => {
			if (command === "chat_completion_stream") {
				return new Promise<string>((_resolve, reject) => {
					REJECT_FIRST = reject
				})
			}
			if (command === "cancel_agent_session") return true
			return null
		})

		const engine = new AgentEngine()
		const RUN_PROMISE = engine.run("hi", [], {}, {}).catch((error) => ({thrown: error}))
		await FLUSH()

		// 用户中止后, 宿主流以失败告终 —— 引擎必须吞掉该错误并回到 idle
		engine.abort()
		REJECT_FIRST?.(new Error("The operation was canceled."))
		const OUTCOME = await RUN_PROMISE

		expect(OUTCOME).not.toHaveProperty("thrown")
		expect(engine.getState()).toBe("idle")
	})
})
