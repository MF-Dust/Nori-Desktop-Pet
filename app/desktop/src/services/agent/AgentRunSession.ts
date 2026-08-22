import type {UnlistenFn} from "../host/event"

let SESSION_COUNTER = 0

/**
 * 单轮 Agent 运行会话
 *
 * 每次 AgentEngine.run 创建独立 session, 持有唯一 ID、AbortController、
 * 本轮注册的监听清理函数与取消状态. 旧 session 只能清理自己的监听,
 * 不能改写新 session 的全局状态.
 */
export class AgentRunSession {
	/** 唯一会话 ID, 透传给宿主 cancel_agent_session */
	public readonly id: string

	private readonly _signal: AbortSignal
	private readonly _controller: AbortController
	private readonly _unlisteners: UnlistenFn[] = []
	private _cancelled = false

	constructor() {
		SESSION_COUNTER += 1
		this.id = `agent-${Date.now().toString(36)}-${SESSION_COUNTER}`
		this._controller = new AbortController()
		this._signal = this._controller.signal
	}

	/** 取消信号, 传给工具执行上下文 */
	public get signal(): AbortSignal {
		return this._signal
	}

	/** 是否已被用户取消 */
	public get isCancelled(): boolean {
		return this._cancelled
	}

	/**
	 * 当前轮次的流 ID (同一 session 内多轮工具调用会推进流 ID)
	 */
	public currentStreamId: string | null = null

	/**
	 * 登记一个本轮的宿主事件监听; 返回值原样透传方便链式使用
	 */
	public addListener(unlisten: UnlistenFn): UnlistenFn {
		this._unlisteners.push(unlisten)
		return unlisten
	}

	/**
	 * 仅清理本 session 登记的监听
	 */
	public unlistenAll(): void {
		for (const unlisten of this._unlisteners.splice(0)) {
			try {
				unlisten()
			} catch {
				/* 监听已失效时忽略 */
			}
		}
	}

	/**
	 * 取消本 session: 标记取消态、中止信号并清理自己的监听
	 */
	public cancel(): void {
		if (this._cancelled) return
		this._cancelled = true
		this._controller.abort()
		this.unlistenAll()
	}
}
