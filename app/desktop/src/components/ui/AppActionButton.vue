<script setup lang="ts">
/**
 * 动作按钮 (自带执行状态)
 *
 * 技能 / MCP / 诊断页原本靠右上角 toast 反馈, 与设置字段的内联「已保存」是两套语义。
 * 这里把结果收回到按钮自身: idle → running → done / failed, 1.6s 后回到 idle,
 * 与 useDebouncedSave 的 settleMs 保持一致。失败仍会抛给调用方走 feedback.error。
 */
import {onBeforeUnmount, ref} from "vue"
import AppButton from "./AppButton.vue"
import Icon from "../Icon.vue"
import type {IconName} from "../../services/icon"

type ActionState = "idle" | "running" | "done" | "failed"

/** 与 useDebouncedSave 的 settleMs 同值: 成功态停留多久回落 */
const SETTLE_MS = 1600

const props = withDefaults(defineProps<{
	label: string
	/** 实际执行的动作, 抛错即视为失败 */
	action: () => unknown | Promise<unknown>
	icon?: IconName
	variant?: "primary" | "ghost" | "danger"
	size?: "sm" | "md"
	/** 执行中替换掉 label 的文案 (如「正在导出…」) */
	runningLabel?: string
	/** 成功 / 失败后按钮旁显示的短文案 */
	doneLabel?: string
	failedLabel?: string
	disabled?: boolean
}>(), {
	variant: "ghost",
	size: "md",
	disabled: false,
})

const STATE = ref<ActionState>("idle")
let timer: ReturnType<typeof setTimeout> | null = null

const settle = (next: ActionState) => {
	STATE.value = next
	if (timer) clearTimeout(timer)
	timer = setTimeout(() => {
		STATE.value = "idle"
		timer = null
	}, SETTLE_MS)
}

const run = async () => {
	if (STATE.value === "running" || props.disabled) return
	STATE.value = "running"
	try {
		await props.action()
		settle("done")
	} catch {
		// 具体错误由调用方的 feedback.error 呈现, 这里只标记按钮状态
		settle("failed")
	}
}

onBeforeUnmount(() => {
	if (timer) clearTimeout(timer)
})
</script>

<template>
	<span class="inline-flex items-center gap-2">
		<AppButton
			:variant="variant"
			:size="size"
			:icon="icon"
			:loading="STATE === 'running'"
			:disabled="disabled"
			@click="run"
		>{{ STATE === "running" && runningLabel ? runningLabel : label }}</AppButton>

		<span
			v-if="STATE === 'done' && doneLabel"
			class="inline-flex items-center gap-1 text-xs text-success"
			role="status"
		>
			<Icon name="check" :size="12"/>
			<span>{{ doneLabel }}</span>
		</span>
		<span
			v-else-if="STATE === 'failed' && failedLabel"
			class="inline-flex items-center gap-1 text-xs text-danger-text"
			role="status"
		>
			<Icon name="alert" :size="12"/>
			<span>{{ failedLabel }}</span>
		</span>
	</span>
</template>
