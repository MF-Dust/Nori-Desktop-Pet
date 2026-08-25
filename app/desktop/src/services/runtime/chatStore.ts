/**
 * 聊天视图状态机
 *
 * 订阅后端 Agent 事件并维护气泡列表/状态/指标/待决授权队列。
 * 业务真相在后端: 这里只做事件到 UI 投影的转换, 不落库、不解析历史。
 */
import {ref} from "vue"
import {RUNTIME, type ApprovalRequestDto, type AgentEventPayload, type AgentState, type UsageMetrics} from "./index"

/** 聊天气泡 */
export interface ChatBubble {
	key: string
	role: "user" | "assistant"
	content: string
}

/** 待决授权项 (队列驱动逐个弹窗) */
export interface PendingApproval {
	request: ApprovalRequestDto
	resolve: (approved: boolean) => void
	remainingSeconds: number
}

/**
 * 创建聊天会话 store (每个 ChatView 实例独立)
 *
 * @param options.watchdogMs 一轮会话无任何事件的容忍上限, 超时后复位并报错 (0 = 关闭)
 */
export function createChatStore(options: {watchdogMs?: number; approvalTimeoutMs?: number} = {}) {
	const WATCHDOG_MS = options.watchdogMs ?? 120_000
	const APPROVAL_TIMEOUT_MS = options.approvalTimeoutMs ?? 30_000
	const bubbles = ref<ChatBubble[]>([])
	const sending = ref(false)
	const agentState = ref<AgentState>("idle")
	const executingTool = ref("")
	const metrics = ref<UsageMetrics | null>(null)
	const errorMsg = ref("")
	const failedInput = ref("")
	const statusCode = ref<"idle" | "cancelled" | "timeout" | "approval-timeout">("idle")
	const pendingApprovals = ref<PendingApproval[]>([])
	const hasMoreHistory = ref(false)
	const loadingHistory = ref(false)

	let activeSessionId: string | null = null
	let activeInput = ""
	let oldestLoadedId = 0
	let placeholderKey = ""
	let unlisten: (() => void) | null = null
	let watchdogTimer: ReturnType<typeof setTimeout> | null = null
	const approvalTimers = new Map<string, ReturnType<typeof setInterval>>()

	/** 看门狗: 后端事件丢了时不能让输入框永久锁在“发送中” */
	function armWatchdog(): void {
		if (WATCHDOG_MS <= 0) return
		clearWatchdog()
		watchdogTimer = setTimeout(() => {
			watchdogTimer = null
			if (!sending.value) return
			errorMsg.value = "会话超时: 未收到后端响应"
			statusCode.value = "timeout"
			const SESSION = activeSessionId
			finishTurn(true, true)
			if (SESSION) void RUNTIME.cancelChat(SESSION).catch(() => {})
		}, WATCHDOG_MS)
	}

	function clearWatchdog(): void {
		if (watchdogTimer) clearTimeout(watchdogTimer)
		watchdogTimer = null
	}

	/** 首屏加载最近一页历史 (服务端已规范化) */
	async function loadRecent(pageSize = 50): Promise<void> {
		loadingHistory.value = true
		try {
			const page = await RUNTIME.historyPage(pageSize)
			bubbles.value = page.map((row) => ({key: String(row.id), role: row.role, content: row.content}))
			hasMoreHistory.value = page.length >= pageSize
			oldestLoadedId = page.length > 0 ? page[0].id : 0
		} catch (error) {
			console.error("加载聊天历史失败:", error)
			errorMsg.value = String(error)
			throw error
		} finally {
			loadingHistory.value = false
		}
	}

	/** 向上翻页加载更早的历史 */
	async function loadOlder(pageSize = 50): Promise<ChatBubble[]> {
		if (!hasMoreHistory.value || oldestLoadedId <= 0 || loadingHistory.value) return []
		loadingHistory.value = true
		try {
			const page = await RUNTIME.historyPage(pageSize, oldestLoadedId)
			if (page.length === 0) {
				hasMoreHistory.value = false
				return []
			}
			oldestLoadedId = page[0].id
			hasMoreHistory.value = page.length >= pageSize
			const older = page.map((row) => ({key: String(row.id), role: row.role, content: row.content}))
			bubbles.value = [...older, ...bubbles.value]
			return older
		} finally {
			loadingHistory.value = false
		}
	}

	/** 发送一条用户消息 */
	async function send(text: string): Promise<void> {
		const trimmed = text.trim()
		if (!trimmed || sending.value) return

		errorMsg.value = ""
		statusCode.value = "idle"
		failedInput.value = ""
		activeInput = trimmed
		sending.value = true
		placeholderKey = `pending-${Date.now()}`
		bubbles.value.push({key: `user-${Date.now()}`, role: "user", content: trimmed})
		bubbles.value.push({key: placeholderKey, role: "assistant", content: ""})

		try {
			activeSessionId = await RUNTIME.startChat(trimmed)
			armWatchdog()
		} catch (error) {
			errorMsg.value = String(error)
			failedInput.value = trimmed
			removePlaceholderIfEmpty()
			sending.value = false
			activeSessionId = null
			activeInput = ""
		}
	}

	/** 重试最近一次失败或被取消的输入 */
	async function retryLast(): Promise<void> {
		if (!failedInput.value || sending.value) return
		await send(failedInput.value)
	}

	/** 中止当前会话 */
	async function abort(): Promise<void> {
		if (!activeSessionId) return
		try {
			await RUNTIME.cancelChat(activeSessionId)
		} catch (error) {
			errorMsg.value = String(error)
			throw error
		}
	}

	/** 清空对话: 后端成功后才清空本地投影 */
	async function clear(): Promise<void> {
		await RUNTIME.clearChat()
		bubbles.value = []
		metrics.value = null
		errorMsg.value = ""
		failedInput.value = ""
		statusCode.value = "idle"
		oldestLoadedId = 0
		hasMoreHistory.value = false
	}

	function clearApprovalTimer(requestId: string): void {
		const TIMER = approvalTimers.get(requestId)
		if (TIMER) clearInterval(TIMER)
		approvalTimers.delete(requestId)
	}

	/** 对授权请求作出决定 */
	async function decideApproval(requestId: string, approved: boolean, timedOut = false): Promise<void> {
		const ITEM = pendingApprovals.value.find(item => item.request.requestId === requestId)
		if (!ITEM) return
		clearApprovalTimer(requestId)
		pendingApprovals.value = pendingApprovals.value.filter(item => item.request.requestId !== requestId)
		if (timedOut) statusCode.value = "approval-timeout"
		try {
			await RUNTIME.respondApproval(requestId, approved)
		} catch (error) {
			errorMsg.value = String(error)
			throw error
		}
	}

	/** 延长一条待决授权的倒计时 (读长参数时 30 秒往往不够) */
	function extendApproval(requestId: string, seconds = Math.max(1, Math.ceil(APPROVAL_TIMEOUT_MS / 1000))): void {
		const ITEM = pendingApprovals.value.find(item => item.request.requestId === requestId)
		if (!ITEM) return
		ITEM.remainingSeconds += seconds
	}

	function removePlaceholderIfEmpty(): void {
		const index = bubbles.value.findIndex(bubble => bubble.key === placeholderKey)
		if (index >= 0 && bubbles.value[index].content === "") {
			bubbles.value.splice(index, 1)
		}
	}

	/** 处理一条后端 Agent 事件 */
	function handleEvent(payload: AgentEventPayload): void {
		// 任何属于当前会话的事件都重新上弦看门狗
		if (sending.value) armWatchdog()
		switch (payload.type) {
			case "chunk": {
				if (payload.sessionId !== activeSessionId) return
				const target = bubbles.value.find(bubble => bubble.key === placeholderKey)
				if (target) target.content += payload.chunk
				break
			}
			case "state": {
				// speaking 状态事件可能不带 sessionId (自动朗读阶段), 归属于当前活动会话或直接展示
				if (payload.sessionId != null && payload.sessionId !== activeSessionId) return
				agentState.value = payload.state
				break
			}
			case "tool-executing": {
				if (payload.sessionId !== activeSessionId) return
				executingTool.value = payload.toolName
				break
			}
			case "tool-executed": {
				if (payload.sessionId !== activeSessionId) return
				executingTool.value = ""
				if (!payload.success && payload.error) errorMsg.value = payload.error
				break
			}
			case "usage": {
				if (payload.sessionId !== activeSessionId) return
				const {type: _type, sessionId: _sessionId, ...usage} = payload
				metrics.value = usage
				break
			}
			case "approval-request": {
				if (payload.sessionId !== activeSessionId) return
				if (pendingApprovals.value.some(item => item.request.requestId === payload.requestId)) return
				const ITEM: PendingApproval = {
					request: payload,
					resolve: (approved) => void decideApproval(payload.requestId, approved),
					remainingSeconds: Math.max(1, Math.ceil(APPROVAL_TIMEOUT_MS / 1000)),
				}
				pendingApprovals.value.push(ITEM)
				const TIMER = setInterval(() => {
					const NEXT = ITEM.remainingSeconds - 1
					ITEM.remainingSeconds = Math.max(0, NEXT)
					if (NEXT <= 0) {
						clearApprovalTimer(payload.requestId)
						void decideApproval(payload.requestId, false, true).catch(error => console.error("工具授权超时响应失败:", error))
					}
				}, 1000)
				approvalTimers.set(payload.requestId, TIMER)
				agentState.value = "waiting_approval"
				break
			}
			case "approval-result":
				break
			case "complete": {
				if (payload.sessionId !== activeSessionId) return
				const target = bubbles.value.find(bubble => bubble.key === placeholderKey)
				if (target && payload.message.text) target.content = payload.message.text
				finishTurn()
				break
			}
			case "cancelled": {
				if (payload.sessionId !== activeSessionId) return
				statusCode.value = "cancelled"
				finishTurn(true, true)
				break
			}
			case "error": {
				if (payload.sessionId !== activeSessionId) return
				errorMsg.value = payload.error
				finishTurn(true, true)
				break
			}
		}
	}

	function finishTurn(cancelled = false, restoreInput = false): void {
		clearWatchdog()
		removePlaceholderIfEmpty()
		if (restoreInput && activeInput) failedInput.value = activeInput
		if (cancelled) agentState.value = "idle"
		sending.value = false
		executingTool.value = ""
		activeSessionId = null
		activeInput = ""
	}

	/** 订阅后端事件; 组件卸载时调用返回的清理函数 */
	async function connect(): Promise<() => void> {
		unlisten = await RUNTIME.onAgentEvent(handleEvent)
		return () => {
			unlisten?.()
			unlisten = null
		}
	}

	function dispose(): void {
		clearWatchdog()
		if (activeSessionId) {
			void RUNTIME.cancelChat(activeSessionId).catch(() => {})
			activeSessionId = null
		}
		for (const pending of pendingApprovals.value) {
			clearApprovalTimer(pending.request.requestId)
			void RUNTIME.respondApproval(pending.request.requestId, false).catch(() => {})
		}
		pendingApprovals.value = []
		unlisten?.()
		unlisten = null
	}

	return {
		bubbles,
		sending,
		agentState,
		executingTool,
		metrics,
		errorMsg,
		failedInput,
		statusCode,
		pendingApprovals,
		hasMoreHistory,
		loadingHistory,
		loadRecent,
		loadOlder,
		send,
		retryLast,
		abort,
		clear,
		decideApproval,
		extendApproval,
		handleEvent,
		connect,
		dispose,
	}
}

export type ChatStore = ReturnType<typeof createChatStore>
