<script setup lang="ts">
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppChip from "../ui/AppChip.vue"
import AppButton from "../ui/AppButton.vue"
import type {AutomationTaskDto} from "../../services/runtime/types"

const props = defineProps<{
	task: AutomationTaskDto
	approving?: boolean
	rejecting?: boolean
	cancelling?: boolean
}>()

const emit = defineEmits<{
	approve: [taskId: string, requestId?: string]
	reject: [taskId: string, requestId?: string]
	cancel: [taskId: string]
}>()

const TEXT = computed(() => useLanguages().views.main.actionCenter)

// 脱敏短 ID (最多取 8 位)
const shortTaskId = computed(() => {
	const ID = props.task.id || ""
	return ID.length > 8 ? ID.slice(0, 8) : ID
})

// 脱敏展示标题
const displayTitle = computed(() => {
	if (props.task.title && props.task.title.trim().length > 0) {
		return props.task.title
	}
	return `${TEXT.value.card.taskId}: ${shortTaskId.value}`
})

// 状态色与文案
const stateTone = computed<"neutral" | "teal" | "success" | "warning" | "danger">(() => {
	switch (props.task.state) {
		case "running":
			return "teal"
		case "awaiting_approval":
			return "warning"
		case "succeeded":
		case "completed":
			return "success"
		case "failed":
			return "danger"
		case "paused":
		case "queued":
		case "cancelled":
		default:
			return "neutral"
	}
})

const stateLabel = computed(() => {
	const S = props.task.state
	const STATES = TEXT.value.states as Record<string, string>
	return STATES[S] || S
})

// 错误分类可读文案
const failureReasonText = computed(() => {
	if (!props.task.failureCode) return null
	const ERRORS = TEXT.value.errors as Record<string, string>
	return ERRORS[props.task.failureCode] || props.task.failureCode
})

// 格式化时间
const formatTime = (timeStr?: string | null) => {
	if (!timeStr) return null
	try {
		const D = new Date(timeStr)
		if (Number.isNaN(D.getTime())) return timeStr
		const H = String(D.getHours()).padStart(2, "0")
		const M = String(D.getMinutes()).padStart(2, "0")
		const S = String(D.getSeconds()).padStart(2, "0")
		return `${H}:${M}:${S}`
	} catch {
		return timeStr
	}
}

// 动作种类映射
const actionKindLabels = computed(() => {
	const KINDS = props.task.actionKinds || []
	const MAP = TEXT.value.actionKinds as Record<string, string>
	return KINDS.map(k => MAP[k] || MAP.unknown || k)
})

// 进度百分比
const progressPercent = computed(() => {
	if (typeof props.task.progress === "number") {
		const P = props.task.progress
		return Math.min(100, Math.max(0, P <= 1 ? Math.round(P * 100) : Math.round(P)))
	}
	if (props.task.totalSteps && props.task.totalSteps > 0 && props.task.currentStep) {
		return Math.min(100, Math.max(0, Math.round((props.task.currentStep / props.task.totalSteps) * 100)))
	}
	return null
})

// 是否可取消
const canCancel = computed(() => {
	const S = props.task.state
	return S === "running" || S === "queued" || S === "paused"
})

// 是否待审批
const isAwaitingApproval = computed(() => props.task.state === "awaiting_approval")

const onApprove = () => {
	emit("approve", props.task.id, props.task.approvalRequestId)
}

const onReject = () => {
	emit("reject", props.task.id, props.task.approvalRequestId)
}

const onCancel = () => {
	emit("cancel", props.task.id)
}
</script>

<template>
	<div
		class="surface-card relative flex flex-col gap-3 p-3.5 border transition-all duration-200"
		:class="isAwaitingApproval ? 'border-warning/50 bg-warning/6 shadow-glow' : 'border-line-subtle'"
		role="article"
		:aria-labelledby="`task-title-${task.id}`"
	>
		<!-- 卡片头部: 标题与状态 -->
		<div class="flex items-center justify-between gap-2 min-w-0">
			<div class="flex items-center gap-2 min-w-0">
				<Icon
					:name="isAwaitingApproval ? 'shield' : task.state === 'running' ? 'activity' : 'bot'"
					:size="15"
					:class="isAwaitingApproval ? 'text-warning' : task.state === 'running' ? 'text-nori-teal-bright spin' : 'text-text-muted'"
				/>
				<span
					:id="`task-title-${task.id}`"
					class="text-sm font-600 text-text-primary truncate"
				>
					{{ displayTitle }}
				</span>
			</div>

			<AppChip :tone="stateTone" dot size="sm">
				{{ stateLabel }}
			</AppChip>
		</div>

		<!-- 步骤与进度条 -->
		<div v-if="task.state === 'running' || progressPercent !== null || task.totalSteps" class="flex flex-col gap-1.5">
			<div class="flex items-center justify-between text-xs text-text-faint">
				<span v-if="task.currentStep && task.totalSteps">
					{{ TEXT.card.step }} <span class="mono text-text-primary">{{ task.currentStep }}</span> {{ TEXT.card.of }} <span class="mono">{{ task.totalSteps }}</span>
				</span>
				<span v-else>{{ TEXT.card.progress }}</span>

				<span v-if="progressPercent !== null" class="mono text-text-muted">
					{{ progressPercent }}%
				</span>
			</div>

			<!-- 进度指示条 -->
			<div class="h-1.5 w-full rounded-pill bg-overlay-6 overflow-hidden">
				<div
					v-if="progressPercent !== null"
					class="h-full rounded-pill bg-gradient-to-r from-nori-teal to-nori-teal-bright transition-all duration-300"
					:style="{width: `${progressPercent}%`}"
				/>
				<div
					v-else-if="task.state === 'running'"
					class="h-full w-1/3 rounded-pill bg-nori-teal-bright/70 animate-pulse"
				/>
			</div>
		</div>

		<!-- 审批动作提示与动作标签 -->
		<div v-if="isAwaitingApproval" class="flex flex-col gap-2 p-2.5 rounded-sm bg-overlay-4 border border-warning/30">
			<div class="flex items-center gap-1.5 text-xs font-500 text-warning">
				<Icon name="shield" :size="13"/>
				<span>{{ TEXT.card.approvalRequired }}</span>
			</div>
			<p class="text-xs text-text-muted leading-relaxed m-0">
				{{ TEXT.card.approvalNotice }}
			</p>

			<div v-if="actionKindLabels.length > 0" class="flex flex-wrap gap-1.5 mt-0.5">
				<span
					v-for="kind in actionKindLabels"
					:key="kind"
					class="px-2 py-0.5 rounded-xs text-xs bg-overlay-6 border border-line-subtle text-text-body font-500"
				>
					{{ kind }}
				</span>
			</div>
		</div>

		<!-- 错误原因展示 -->
		<div v-if="failureReasonText" class="flex items-center gap-1.5 px-2.5 py-1.5 rounded-sm bg-danger/10 border border-danger/30 text-xs text-danger-text">
			<Icon name="alert" :size="13" class="shrink-0"/>
			<span class="truncate">{{ TEXT.card.errorCategory }}: {{ failureReasonText }}</span>
		</div>

		<!-- 时间指标 -->
		<div class="flex items-center justify-between text-xs text-text-faint mono">
			<span v-if="task.createdAt">
				{{ TEXT.card.created }}: {{ formatTime(task.createdAt) }}
			</span>
			<span v-if="task.finishedAt">
				{{ TEXT.card.finished }}: {{ formatTime(task.finishedAt) }}
			</span>
		</div>

		<!-- 操作按钮栏 -->
		<div v-if="isAwaitingApproval || canCancel" class="flex items-center justify-end gap-2 pt-1 border-t border-line-subtle/50">
			<template v-if="isAwaitingApproval">
				<AppButton
					variant="ghost"
					size="sm"
					:disabled="approving || rejecting"
					:loading="rejecting"
					@click="onReject"
				>
					{{ TEXT.card.reject }}
				</AppButton>
				<AppButton
					variant="primary"
					size="sm"
					icon="check"
					:disabled="approving || rejecting"
					:loading="approving"
					@click="onApprove"
				>
					{{ TEXT.card.approve }}
				</AppButton>
			</template>

			<template v-else-if="canCancel">
				<AppButton
					variant="danger"
					size="sm"
					icon="close"
					:disabled="cancelling"
					:loading="cancelling"
					@click="onCancel"
				>
					{{ TEXT.card.cancel }}
				</AppButton>
			</template>
		</div>
	</div>
</template>
