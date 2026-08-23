/**
 * 字段级防抖保存
 *
 * 规范要求「每个字段独立计时器」——共享计时器会静默丢掉前一个字段的值。
 * 这里在此之上补两件事:
 *   1. 暴露 saving/saved/error 状态, 让界面能给出「已保存」反馈 (原来失败只进 console)
 *   2. 卸载时 flush 未落地的写入, 免得用户改完立刻切页导致丢失
 */
import {onBeforeUnmount, ref} from "vue"

/** 单字段保存状态 */
export type SaveState = "idle" | "saving" | "saved" | "error"

/** 保存执行体: 返回 Promise, 抛错即失败 */
export type SaveTask = () => Promise<void>

export interface DebouncedSaveOptions {
	/** 防抖延迟 (ms), 规范默认 400 */
	delay?: number
	/** 「已保存」标记的驻留时间 (ms) */
	settleMs?: number
	/** 失败回调 (通常接到反馈层做 toast) */
	onError?: (key: string, error: unknown) => void
}

/**
 * 创建一组按 key 独立防抖的保存器
 */
export function useDebouncedSave(options: DebouncedSaveOptions = {}) {
	const DELAY = options.delay ?? 400
	const SETTLE = options.settleMs ?? 1600

	const states = ref<Record<string, SaveState>>({})
	const errors = ref<Record<string, string>>({})

	const timers = new Map<string, ReturnType<typeof setTimeout>>()
	const settleTimers = new Map<string, ReturnType<typeof setTimeout>>()
	const pending = new Map<string, SaveTask>()

	const setState = (key: string, state: SaveState) => {
		states.value = {...states.value, [key]: state}
	}

	const run = async (key: string, task: SaveTask): Promise<void> => {
		setState(key, "saving")
		try {
			await task()
			errors.value = {...errors.value, [key]: ""}
			setState(key, "saved")
			const SETTLE_TIMER = settleTimers.get(key)
			if (SETTLE_TIMER) clearTimeout(SETTLE_TIMER)
			settleTimers.set(key, setTimeout(() => {
				settleTimers.delete(key)
				if (states.value[key] === "saved") setState(key, "idle")
			}, SETTLE))
		} catch (error) {
			errors.value = {...errors.value, [key]: error instanceof Error ? error.message : String(error)}
			setState(key, "error")
			options.onError?.(key, error)
		}
	}

	/** 排一次防抖保存 (同 key 覆盖前一次) */
	const save = (key: string, task: SaveTask): void => {
		const EXISTING = timers.get(key)
		if (EXISTING) clearTimeout(EXISTING)
		pending.set(key, task)
		timers.set(key, setTimeout(() => {
			timers.delete(key)
			const TASK = pending.get(key)
			pending.delete(key)
			if (TASK) void run(key, TASK)
		}, DELAY))
	}

	/** 立即保存 (开关一类不需要防抖的交互) */
	const saveNow = (key: string, task: SaveTask): Promise<void> => {
		const EXISTING = timers.get(key)
		if (EXISTING) clearTimeout(EXISTING)
		timers.delete(key)
		pending.delete(key)
		return run(key, task)
	}

	/** 把所有还在防抖窗口里的写入立即执行 */
	const flush = (): void => {
		for (const [key, timer] of timers) {
			clearTimeout(timer)
			const TASK = pending.get(key)
			pending.delete(key)
			if (TASK) void run(key, TASK)
		}
		timers.clear()
	}

	const stateOf = (key: string): SaveState => states.value[key] ?? "idle"
	const errorOf = (key: string): string => errors.value[key] ?? ""

	onBeforeUnmount(() => {
		flush()
		for (const timer of settleTimers.values()) clearTimeout(timer)
		settleTimers.clear()
	})

	return {states, errors, save, saveNow, flush, stateOf, errorOf}
}

export type DebouncedSave = ReturnType<typeof useDebouncedSave>
