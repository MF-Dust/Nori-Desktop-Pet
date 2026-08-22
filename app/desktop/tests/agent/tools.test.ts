import {beforeEach, describe, expect, it, vi} from "vitest"

const MOCKS = vi.hoisted(() => ({
	invoke: vi.fn(),
}))

vi.mock("../../src/services/host/invoke", () => ({invoke: MOCKS.invoke}))
vi.mock("../../src/services/live2d", () => ({petLive2DController: null}))
vi.mock("../../src/services/emotion", () => ({
	emotionManager: {getState: () => ({type: "neutral"}), setEmotion: vi.fn()},
}))
vi.mock("../../src/services/proactive", () => ({proactiveService: {addReminder: vi.fn()}}))
vi.mock("../../src/services/memory", () => ({memoryService: {getRelevantMemories: vi.fn(), add: vi.fn()}}))

import {toolManager, type AgentTool} from "../../src/services/agent/tools"
import type {ToolApprovalRequest} from "../../src/services/agent/protocol"

/** 注册一个临时测试工具, 返回注销函数 */
const WITH_TOOL = (tool: AgentTool) => {
	toolManager.register(tool)
	return () => toolManager.unregister(tool.name)
}

describe("ToolManager 逐调用授权", () => {
	beforeEach(() => {
		MOCKS.invoke.mockReset()
	})

	it("safe 工具直接执行, 不需要授权", async () => {
		const EXEC = vi.fn(() => "done")
		const REMOVE = WITH_TOOL({
			name: "test-safe",
			description: "safe",
			parameters: {type: "object"},
			permissionLevel: "safe",
			execute: EXEC,
		})

		const RES = await toolManager.execute("test-safe", {})
		expect(RES.result).toBe("done")
		expect(EXEC).toHaveBeenCalledTimes(1)
		REMOVE()
	})

	it("confirm 工具缺少授权回调时 fail-closed", async () => {
		const EXEC = vi.fn(() => "should-not-run")
		const REMOVE = WITH_TOOL({
			name: "test-confirm",
			description: "needs confirm",
			parameters: {type: "object"},
			permissionLevel: "confirm",
			execute: EXEC,
		})

		const RES = await toolManager.execute("test-confirm", {})
		expect(RES.error).toContain("已拒绝执行")
		expect(EXEC).not.toHaveBeenCalled()
		REMOVE()
	})

	it("用户批准后执行一次", async () => {
		const EXEC = vi.fn(() => "ran")
		const REMOVE = WITH_TOOL({
			name: "test-approve",
			description: "approved path",
			parameters: {type: "object"},
			permissionLevel: "dangerous",
			execute: EXEC,
		})
		const REQUESTS: ToolApprovalRequest[] = []

		const RES = await toolManager.execute("test-approve", {x: 1}, {
			requestToolApproval: async (request) => {
				REQUESTS.push(request)
				return "approved"
			},
		})

		expect(RES.result).toBe("ran")
		expect(EXEC).toHaveBeenCalledTimes(1)
		expect(REQUESTS).toHaveLength(1)
		expect(REQUESTS[0].permissionLevel).toBe("dangerous")
		expect(REQUESTS[0].arguments).toEqual({x: 1})
		REMOVE()
	})

	it("用户拒绝时不执行且错误可序列化", async () => {
		const EXEC = vi.fn()
		const REMOVE = WITH_TOOL({
			name: "test-deny",
			description: "denied path",
			parameters: {type: "object"},
			permissionLevel: "confirm",
			execute: EXEC,
		})

		const RES = await toolManager.execute("test-deny", {}, {requestToolApproval: async () => "denied"})
		expect(RES.error).toContain("用户拒绝执行工具: test-deny")
		expect(EXEC).not.toHaveBeenCalled()
		REMOVE()
	})

	it("授权通道异常视为拒绝", async () => {
		const EXEC = vi.fn()
		const REMOVE = WITH_TOOL({
			name: "test-crash",
			description: "approval throws",
			parameters: {type: "object"},
			permissionLevel: "confirm",
			execute: EXEC,
		})

		const RES = await toolManager.execute("test-crash", {}, {
			requestToolApproval: async () => {
				throw new Error("dialog gone")
			},
		})
		expect(RES.error).toContain("已拒绝执行")
		expect(EXEC).not.toHaveBeenCalled()
		REMOVE()
	})

	it("等待授权期间会话取消则不再执行", async () => {
		const EXEC = vi.fn()
		const REMOVE = WITH_TOOL({
			name: "test-abort",
			description: "abort during approval",
			parameters: {type: "object"},
			permissionLevel: "confirm",
			execute: EXEC,
		})
		const CONTROLLER = new AbortController()

		const PENDING = toolManager.execute("test-abort", {}, {
			signal: CONTROLLER.signal,
			requestToolApproval: async () => {
				CONTROLLER.abort()
				return "approved"
			},
		})
		const RES = await PENDING

		expect(RES.error).toContain("不再执行")
		expect(EXEC).not.toHaveBeenCalled()
		REMOVE()
	})

	it("MCP 动态工具默认标记为 confirm 并透传 sessionId", async () => {
		MOCKS.invoke.mockImplementation(async (command: string) => {
			if (command === "mcp_get_servers") {
				return [{
					serverId: "srv1",
					name: "测试服务",
					status: "connected",
					enabled: true,
					tools: [{name: "ping", description: "ping", inputSchema: {type: "object"}}],
				}]
			}
			if (command === "mcp_call_tool") {
				return {isError: false, content: [{type: "text", text: "pong"}]}
			}
			return null
		})

		const {mcpService} = await import("../../src/services/mcp")
		await mcpService.syncToolsWithToolManager()

		const TOOL = toolManager.get("mcp__srv1__ping")
		expect(TOOL?.permissionLevel).toBe("confirm")

		// 无授权回调 → 拒绝; 有回调 → 调用携带 sessionId
		const DENIED = await toolManager.execute("mcp__srv1__ping", {})
		expect(DENIED.error).toBeTruthy()

		const APPROVED = await toolManager.execute("mcp__srv1__ping", {}, {
			sessionId: "session-9",
			requestToolApproval: async () => "approved",
		})
		expect(APPROVED.result).toBeDefined()
		const CALL = MOCKS.invoke.mock.calls.find(([command]) => command === "mcp_call_tool")
		expect(CALL?.[1]).toMatchObject({serverId: "srv1", toolName: "ping", sessionId: "session-9"})
	})
})
