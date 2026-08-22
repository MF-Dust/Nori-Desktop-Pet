import {beforeEach, describe, expect, it, vi} from "vitest"
import {createChatStore} from "../../src/services/runtime/chatStore"
import type {ApprovalRequestDto} from "../../src/services/runtime"

const {invokeMock} = vi.hoisted(() => ({
	invokeMock: vi.fn(async (cmd: string) => {
		if (cmd === "chat_start") return "session-a"
		if (cmd === "approval_respond") return true
		return null
	}),
}))

vi.mock("../../src/services/host/invoke", () => ({
	invoke: invokeMock,
}))

describe("runtime 聊天状态投影", () => {
	beforeEach(() => invokeMock.mockClear())

	it("只接收当前会话的流式事件并在完成时结束发送", async () => {
		const store = createChatStore()
		await store.send("你好")
		store.handleEvent({type: "chunk", sessionId: "other", chunk: "忽略"})
		store.handleEvent({type: "chunk", sessionId: "session-a", chunk: "你好"})
		expect(store.bubbles.value.at(-1)?.content).toBe("你好")
		store.handleEvent({
			type: "complete",
			sessionId: "session-a",
			message: {text: "你好呀"},
		})
		expect(store.bubbles.value.at(-1)?.content).toBe("你好呀")
		expect(store.sending.value).toBe(false)
		store.dispose()
	})

	it("授权请求进入队列, 决定后只回传请求 ID", async () => {
		const store = createChatStore()
		const request: ApprovalRequestDto = {
			type: "approval-request",
			sessionId: "session-a",
			requestId: "approval-1",
			toolName: "searchWeb",
			permissionLevel: "confirm",
		}
		await store.send("需要搜索")
		store.handleEvent(request)
		expect(store.pendingApprovals.value).toHaveLength(1)
		await store.decideApproval("approval-1", true)
		expect(store.pendingApprovals.value).toHaveLength(0)
		expect(invokeMock).toHaveBeenCalledWith("approval_respond", {requestId: "approval-1", approved: true})
		store.dispose()
	})
})
