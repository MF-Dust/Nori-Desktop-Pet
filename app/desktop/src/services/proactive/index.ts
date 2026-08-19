import {invoke} from "../host/invoke"
import {createLive2D} from "../live2d"
import {ttsService} from "../tts"

/**
 * 提醒事项
 */
export interface ReminderItem {
	id: string
	content: string
	triggerTime: number
	timerId?: number
	repeatDaily?: boolean
}

/**
 * 主动交互与定时提醒服务
 */
export class ProactiveService {
	private reminders: Map<string, ReminderItem> = new Map()
	private idleTimer: number | null = null
	private lastActiveTime = Date.now()
	private idleThresholdMs = 15 * 60 * 1000 // 默认 15 分钟无操作视为挂机
	private scheduledCheckInterval: number | null = null
	private firedDailyGreetings: Set<string> = new Set()

	constructor() {
		this.initActivityListeners()
		this.startDailyScheduler()
	}

	/**
	 * 初始化用户活动监听 (鼠标/键盘交互重置挂机计时)
	 */
	private initActivityListeners(): void {
		if (typeof window === "undefined") return

		const resetActivity = () => {
			this.lastActiveTime = Date.now()
		}

		window.addEventListener("mousemove", resetActivity, {passive: true})
		window.addEventListener("keydown", resetActivity, {passive: true})
		window.addEventListener("click", resetActivity, {passive: true})

		// 每分钟检查一次是否超时
		this.idleTimer = window.setInterval(() => {
			const IDLE_DURATION = Date.now() - this.lastActiveTime
			if (IDLE_DURATION >= this.idleThresholdMs) {
				this.onIdleTimeout()
				this.lastActiveTime = Date.now() // 触发后重置，防止频繁刷屏
			}
		}, 60000)
	}

	/**
	 * 挂机超时触发关怀动作与台词
	 */
	private async onIdleTimeout(): Promise<void> {
		const CANDIDATE_ACTIONS = [
			{text: "主人已经好久没有理 Nori 啦...", motion: "think", expression: "Sad"},
			{text: "伸个懒腰~ 工作辛苦啦，记得休息一下眼睛哦！", motion: "smile", expression: "Smile"},
			{text: "呼啊... 好困呀，主人在忙什么呢？", motion: "wave", expression: "Sleepy"},
		]

		const PICKED = CANDIDATE_ACTIONS[Math.floor(Math.random() * CANDIDATE_ACTIONS.length)]
		if (!PICKED) return

		try {
			const L2D = createLive2D()
			if (PICKED.motion) await L2D.playMotionByName(PICKED.motion)
			if (PICKED.expression) await L2D.playExpression(PICKED.expression)
			await invoke("write_log", {level: "info", message: `触发挂机主动关怀: ${PICKED.text}`})
		} catch {
			/* 忽略错误 */
		}
	}

	/**
	 * 日常时段问候调度器 (早安 / 午餐 / 喝水 / 晚安)
	 */
	private startDailyScheduler(): void {
		if (typeof window === "undefined") return

		this.scheduledCheckInterval = window.setInterval(async () => {
			const NOW = new Date()
			const HOUR = NOW.getHours()
			const MINUTE = NOW.getMinutes()
			const DATE_STR = NOW.toISOString().split("T")[0]

			// 8:30 晨间问候
			if (HOUR === 8 && MINUTE >= 30 && !this.firedDailyGreetings.has(`${DATE_STR}-morning`)) {
				this.firedDailyGreetings.add(`${DATE_STR}-morning`)
				await this.sayProactive("早安主人！新的一天也要元气满满哦~", "wave", "Smile")
			}
			// 12:00 午餐提醒
			else if (HOUR === 12 && MINUTE <= 15 && !this.firedDailyGreetings.has(`${DATE_STR}-lunch`)) {
				this.firedDailyGreetings.add(`${DATE_STR}-lunch`)
				await this.sayProactive("到午饭时间啦！不要饿肚子，去吃点好吃的吧~", "smile", "Smile")
			}
			// 23:00 晚安提醒
			else if (HOUR === 23 && MINUTE >= 0 && !this.firedDailyGreetings.has(`${DATE_STR}-night`)) {
				this.firedDailyGreetings.add(`${DATE_STR}-night`)
				await this.sayProactive("夜深了，工作再忙也要注意身体，早点休息吧主人~", "think", "Sleepy")
			}
		}, 30000)
	}

	/**
	 * 主动发声与动作表演
	 */
	public async sayProactive(text: string, motionName?: string, expressionName?: string): Promise<void> {
		const L2D = createLive2D()
		if (motionName) {
			try {
				await L2D.playMotionByName(motionName)
			} catch {
				/* 动作未匹配时忽略 */
			}
		}
		if (expressionName) {
			try {
				await L2D.playExpression(expressionName)
			} catch {
				/* 表情未匹配时忽略 */
			}
		}

		try {
			const AUTO_TTS = await invoke<string | null>("get_config", {key: "tts_auto_play"})
			if (AUTO_TTS === "true" || AUTO_TTS === "1") {
				void ttsService.speak(text)
			}
		} catch {
			/* 忽略错误 */
		}
	}

	/**
	 * 设置一个提醒 (如 30 分钟后提醒喝水)
	 */
	public addReminder(content: string, delayMinutes: number): string {
		const ID = `reminder-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`
		const TRIGGER_TIME = Date.now() + delayMinutes * 60 * 1000

		const TIMER_ID = window.setTimeout(async () => {
			this.reminders.delete(ID)
			await this.sayProactive(`主人！提醒时间到了：${content}`, "wave", "Surprised")
		}, delayMinutes * 60 * 1000)

		this.reminders.set(ID, {
			id: ID,
			content,
			triggerTime: TRIGGER_TIME,
			timerId: TIMER_ID,
		})

		return ID
	}

	/**
	 * 获取所有当前排队的提醒
	 */
	public listReminders(): ReminderItem[] {
		return Array.from(this.reminders.values())
	}

	/**
	 * 取消提醒
	 */
	public cancelReminder(id: string): boolean {
		const REMINDER = this.reminders.get(id)
		if (!REMINDER) return false

		if (REMINDER.timerId !== undefined) {
			clearTimeout(REMINDER.timerId)
		}
		this.reminders.delete(id)
		return true
	}

	/**
	 * 销毁定时器
	 */
	public destroy(): void {
		if (this.idleTimer !== null) {
			clearInterval(this.idleTimer)
			this.idleTimer = null
		}
		if (this.scheduledCheckInterval !== null) {
			clearInterval(this.scheduledCheckInterval)
			this.scheduledCheckInterval = null
		}
		for (const reminder of this.reminders.values()) {
			if (reminder.timerId !== undefined) clearTimeout(reminder.timerId)
		}
		this.reminders.clear()
	}
}

/**
 * 全局主动交互服务单例
 */
export const proactiveService = new ProactiveService()
