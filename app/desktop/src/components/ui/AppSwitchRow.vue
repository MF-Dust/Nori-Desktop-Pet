<script setup lang="ts">
/**
 * 开关行 (标题 + 说明 + 右侧控件)
 *
 * 设置页里大量重复的「一行一个开关」结构。
 * 常规用法是自闭合并绑 v-model —— 此时内部直接渲染 AppSwitch, 并把 title
 * 作为无障碍名称接上去 (标题文字与开关之间原本没有关联, 读屏读不出这是什么开关)。
 * 需要放别的控件 (滑块、按钮组) 时再用默认插槽。
 * boxed 是"一行一块"的排布 (行为设置页): 行外自带描边与底纹, 悬停亮起。
 */
import AppSwitch from "./AppSwitch.vue"

defineProps<{
	title: string
	desc?: string
	/** 自闭合用法: 绑 v-model 即渲染内建开关 */
	modelValue?: boolean
	disabled?: boolean
	loading?: boolean
	/** 独立成块: 行外带描边与底纹, 用于整页都是独立开关行的排布 */
	boxed?: boolean
}>()

const EMIT = defineEmits<{(event: "update:modelValue", value: boolean): void}>()
</script>

<template>
	<div
		class="flex items-center justify-between gap-4"
		:class="boxed ? 'surface-inset px-3 py-[0.9rem] transition-all duration-200 hover:(bg-nori-teal-bright/6 border-nori-teal-soft)' : ''"
	>
		<div class="flex flex-col gap-0.5 min-w-0">
			<span class="text-base text-text-primary font-500">{{ title }}</span>
			<span v-if="desc" class="text-hint">{{ desc }}</span>
		</div>
		<div class="shrink-0">
			<slot>
				<AppSwitch
					:model-value="modelValue ?? false"
					:label="title"
					:disabled="disabled"
					:loading="loading"
					@update:model-value="EMIT('update:modelValue', $event)"
				/>
			</slot>
		</div>
	</div>
</template>
