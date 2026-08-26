import {describe, expect, it, beforeEach, vi} from "vitest"
import {RUNTIME, type BrowserActionDto, type BrowserTaskResultDto, type AutomationAuditRecordDto} from "../../src/services/runtime"

describe("Browser Automation Runtime & Contracts", () => {
	const mockCalls: {command: string; args: any}[] = []

	beforeEach(() => {
		mockCalls.length = 0
		vi.restoreAllMocks()
		;(window as any).__nori = {
			assetBase: "/nori-assets/",
			label: "main",
			invoke: async (command: string, args: any) => {
				mockCalls.push({command, args})
				if (command === "automation_browser_start_task") {
					return {taskId: "browser-task-123", state: "queued"}
				}
				if (command === "automation_browser_get_result") {
					return {
						taskId: args.taskId,
						success: true,
						summary: "已提取5条相关摘要",
						finishedAt: "2025-01-15T10:00:00Z",
					} satisfies BrowserTaskResultDto
				}
				if (command === "automation_browser_stop_task") {
					return undefined
				}
				if (command === "automation_audit_list") {
					return [
						{
							id: "audit-001",
							taskId: "task-001",
							timestamp: "2025-01-15T09:00:00Z",
							taskKind: "browser",
							actionCategory: "navigate",
							outcome: "succeeded",
						},
					] satisfies AutomationAuditRecordDto[]
				}
				return null
			},
			emit: () => {},
			listen: () => () => {},
			dispatch: () => {},
		}
	})

	it("dispatches automation_browser_start_task with structured actions payload", async () => {
		const ACTIONS: BrowserActionDto[] = [
			{type: "open_url", description: "打开受控页面"},
			{type: "dom_query", description: "查找目标信息"},
		]

		const RESULT = await RUNTIME.automationBrowserStartTask(ACTIONS)
		expect(mockCalls).toHaveLength(1)
		expect(mockCalls[0].command).toBe("automation_browser_start_task")
		expect(mockCalls[0].args).toEqual({actions: ACTIONS})
		expect(RESULT.taskId).toBe("browser-task-123")
	})

	it("dispatches automation_browser_get_result with taskId and returns bounded result", async () => {
		const RESULT = await RUNTIME.automationBrowserGetResult("browser-task-123")
		expect(mockCalls).toHaveLength(1)
		expect(mockCalls[0].command).toBe("automation_browser_get_result")
		expect(mockCalls[0].args).toEqual({taskId: "browser-task-123"})
		expect(RESULT?.success).toBe(true)
		expect(RESULT?.summary).toBe("已提取5条相关摘要")
	})

	it("dispatches automation_browser_stop_task with taskId", async () => {
		await RUNTIME.automationBrowserStopTask("browser-task-123")
		expect(mockCalls).toHaveLength(1)
		expect(mockCalls[0].command).toBe("automation_browser_stop_task")
		expect(mockCalls[0].args).toEqual({taskId: "browser-task-123"})
	})

	it("dispatches automation_audit_list with limit argument", async () => {
		const RECORDS = await RUNTIME.automationAuditList(20)
		expect(mockCalls).toHaveLength(1)
		expect(mockCalls[0].command).toBe("automation_audit_list")
		expect(mockCalls[0].args).toEqual({limit: 20})
		expect(RECORDS).toHaveLength(1)
		expect(RECORDS[0].taskKind).toBe("browser")
	})
})
