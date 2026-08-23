<script setup lang="ts">
/**
 * 开关行 (标题 + 说明 + 右侧控件)
 *
 * 设置页里大量重复的「一行一个开关」结构。
 */
defineProps<{
	title: string
	desc?: string
}>()
</script>

<template>
	<div class="flex items-center justify-between gap-4">
		<div class="flex flex-col gap-0.5 min-w-0">
			<span class="text-base text-text-primary font-500">{{ title }}</span>
			<span v-if="desc" class="text-hint">{{ desc }}</span>
		</div>
		<div class="shrink-0">
			<slot/>
		</div>
	</div>
</template>
