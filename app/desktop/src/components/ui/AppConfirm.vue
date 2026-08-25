<script setup lang="ts">
/**
 * 确认对话框 (替代 n-popconfirm)
 *
 * 复用 AppModal 的焦点陷阱 / Esc 关闭 / 焦点归还, 破坏性操作一律走这里,
 * 不再用气泡确认 —— 气泡会被滚动容器裁切, 且键盘可达性弱。
 */
import AppModal from "./AppModal.vue"
import AppButton from "./AppButton.vue"
import Icon from "../Icon.vue"

withDefaults(defineProps<{
	show: boolean
	title: string
	/** 说明文字: 说清楚这次操作会影响什么 */
	desc?: string
	confirmLabel: string
	cancelLabel: string
	closeLabel: string
	/** 破坏性操作用 danger, 确认按钮转红 */
	tone?: "primary" | "danger"
	loading?: boolean
}>(), {
	tone: "primary",
	loading: false,
})

const EMIT = defineEmits<{
	(event: "update:show", value: boolean): void
	(event: "confirm"): void
}>()
</script>

<template>
	<AppModal
		:show="show"
		:title="title"
		:close-label="closeLabel"
		panel-class="w-full max-w-[42rem]"
		@update:show="EMIT('update:show', $event)"
	>
		<div class="flex items-start gap-3">
			<span
				class="flex shrink-0 items-center justify-center w-8 h-8 rounded-full"
				:class="tone === 'danger' ? 'bg-danger/12 text-danger-text' : 'bg-nori-teal-bright/12 text-nori-teal-bright'"
			>
				<Icon :name="tone === 'danger' ? 'alert' : 'info'" :size="18"/>
			</span>
			<p v-if="desc" class="m-0 text-base text-text-body">{{ desc }}</p>
		</div>

		<template #footer>
			<AppButton :disabled="loading" @click="EMIT('update:show', false)">{{ cancelLabel }}</AppButton>
			<AppButton
				:variant="tone === 'danger' ? 'danger' : 'primary'"
				:loading="loading"
				@click="EMIT('confirm')"
			>{{ confirmLabel }}</AppButton>
		</template>
	</AppModal>
</template>
