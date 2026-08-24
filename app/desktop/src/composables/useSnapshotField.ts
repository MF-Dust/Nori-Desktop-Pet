/**
 * 快照字段编辑态
 *
 * 设置页原来的 `synced` 一次性同步有两个毛病:
 *   1. 其他窗口改了配置, 本页永远看不到 (快照更新被忽略)
 *   2. 快照一变就整体覆盖, 会把用户正在输入的内容吞掉
 *
 * 这里的规则是: 快照变化时只同步「用户没在编辑」的字段;
 * 字段一旦获得焦点或本地改过 (dirty), 就以本地值为准, 直到显式 commit/reset。
 */
import {ref, watch, type Ref} from "vue"
import {RUNTIME} from "../services/runtime"
import type {UiSnapshot} from "../services/runtime"

/**
 * 绑定一个快照派生字段
 *
 * @param read 从快照读值
 */
export function useSnapshotField<T>(read: (snapshot: UiSnapshot) => T, fallback: T) {
	const value = ref(RUNTIME.snapshot.value ? read(RUNTIME.snapshot.value) : fallback) as Ref<T>
	const dirty = ref(false)
	const editing = ref(false)

	watch(RUNTIME.snapshot, (snapshot) => {
		if (!snapshot) return
		if (dirty.value || editing.value) return
		value.value = read(snapshot)
	}, {immediate: true})

	return {
		value,
		dirty,
		editing,
		/** 标记进入编辑 (input focus) */
		focus(): void {
			editing.value = true
		},
		/** 结束编辑; 未提交的本地改动仍算 dirty */
		blur(): void {
			editing.value = false
		},
		/** 本地改动 */
		touch(): void {
			dirty.value = true
		},
		/** 已提交给后端: 交还给快照托管 */
		commit(): void {
			dirty.value = false
		},
		/** 丢弃本地改动, 回到快照值 */
		reset(): void {
			dirty.value = false
			editing.value = false
			if (RUNTIME.snapshot.value) value.value = read(RUNTIME.snapshot.value)
		},
	}
}

export type SnapshotField<T> = ReturnType<typeof useSnapshotField<T>>
