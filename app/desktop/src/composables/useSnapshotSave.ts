/**
 * 快照字段状态与防抖持久化组合器
 *
 * 组合 useSnapshotField 与 useDebouncedSave:
 *   1. 每个字段独立防抖计时器 (默认 400ms)
 *   2. 组件卸载时自动 flush 未落地的改动
 *   3. 统一管理 touch / blur / commit / reset 状态机
 *   4. 失败时自动回滚快照值并通过 feedback.error 暴露给用户
 *   5. 暴露 saving / saved / error 状态与错误信息
 */
import {computed, type ComputedRef} from "vue"
import {useDebouncedSave, type SaveState} from "./useDebouncedSave"
import {useSnapshotField, type SnapshotField} from "./useSnapshotField"
import {feedback} from "../services/feedback"
import useLanguages from "../services/i18n/useLanguages"
import type {UiSnapshot} from "../services/runtime"

export interface SnapshotSaveOptions {
	/** 防抖延迟 (ms), 规范默认 400 */
	delay?: number
	/** 「已保存」标记的驻留时间 (ms) */
	settleMs?: number
	/** 错误处理函数 (未提供时使用默认 feedback.error) */
	onError?: (key: string, error: unknown) => void
	/** 默认错误提示文字或按 key 生成函数 */
	defaultErrorMessage?: string | ((key: string) => string)
}

export interface ManagedSnapshotField<T> extends SnapshotField<T> {
	key: string
	state: ComputedRef<SaveState>
	error: ComputedRef<string>
	save: (task?: () => Promise<void>) => void
	saveNow: (task?: () => Promise<void>) => Promise<void>
}

export function useSnapshotSave(options: SnapshotSaveOptions = {}) {
	const SAVE = useDebouncedSave({
		delay: options.delay,
		settleMs: options.settleMs,
		onError: (key, error) => {
			if (options.onError) {
				options.onError(key, error)
			} else {
				// 兜底文案在出错时才取, 这样切换语言后不会停留在旧语言包
				const message = typeof options.defaultErrorMessage === "function"
					? options.defaultErrorMessage(key)
					: (options.defaultErrorMessage ?? useLanguages().components.ui.state.saveFailed)
				feedback.error(message, error)
			}
		},
	})

	/**
	 * 创建受管快照字段
	 *
	 * @param key 字段唯一键 (用于独立防抖与状态跟踪)
	 * @param read 从快照读取字段值
	 * @param fallback 快照未就绪时的回退值
	 * @param saver 默认保存逻辑 (可选, 传入后可直接调用 field.save() / field.saveNow())
	 */
	function defineField<T>(
		key: string,
		read: (snapshot: UiSnapshot) => T,
		fallback: T,
		saver?: (val: T) => Promise<void>,
	): ManagedSnapshotField<T> {
		const field = useSnapshotField(read, fallback)

		const saveDebounced = (task?: () => Promise<void>) => {
			field.touch()
			field.blur()
			SAVE.save(key, async () => {
				try {
					if (task) {
						await task()
					} else if (saver) {
						await saver(field.value.value)
					}
					field.commit()
				} catch (error) {
					field.reset()
					throw error
				}
			})
		}

		const saveImmediate = (task?: () => Promise<void>) => {
			field.touch()
			field.blur()
			return SAVE.saveNow(key, async () => {
				try {
					if (task) {
						await task()
					} else if (saver) {
						await saver(field.value.value)
					}
					field.commit()
				} catch (error) {
					field.reset()
					throw error
				}
			})
		}

		return {
			...field,
			key,
			state: computed(() => SAVE.stateOf(key)),
			error: computed(() => SAVE.errorOf(key)),
			save: saveDebounced,
			saveNow: saveImmediate,
		}
	}

	/**
	 * 对外部字段或未包装的快照字段执行防抖保存
	 */
	function saveField<T extends {touch: () => void; blur?: () => void; commit: () => void; reset: () => void}>(
		key: string,
		field: T | undefined,
		task: () => Promise<void>,
	): void {
		field?.touch()
		field?.blur?.()
		SAVE.save(key, async () => {
			try {
				await task()
				field?.commit()
			} catch (error) {
				field?.reset()
				throw error
			}
		})
	}

	/**
	 * 对外部字段或未包装的快照字段执行立即保存
	 */
	function saveFieldNow<T extends {touch: () => void; blur?: () => void; commit: () => void; reset: () => void}>(
		key: string,
		field: T | undefined,
		task: () => Promise<void>,
	): Promise<void> {
		field?.touch()
		field?.blur?.()
		return SAVE.saveNow(key, async () => {
			try {
				await task()
				field?.commit()
			} catch (error) {
				field?.reset()
				throw error
			}
		})
	}

	return {
		defineField,
		saveField,
		saveFieldNow,
		save: SAVE.save,
		saveNow: SAVE.saveNow,
		flush: SAVE.flush,
		stateOf: SAVE.stateOf,
		errorOf: SAVE.errorOf,
		rawSave: SAVE,
	}
}

export type SnapshotSave = ReturnType<typeof useSnapshotSave>
