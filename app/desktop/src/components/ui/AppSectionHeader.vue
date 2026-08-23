<script setup lang="ts">
/**
 * 页面区块头部 (标题 + 副标题 + 右侧操作)
 */
defineProps<{
	title: string
	subtitle?: string
}>()
</script>

<template>
	<header class="flex items-start justify-between gap-3 shrink-0">
		<div class="flex flex-col gap-1 min-w-0">
			<h2 class="title-lg glow-teal">{{ title }}</h2>
			<p v-if="subtitle" class="text-hint">{{ subtitle }}</p>
		</div>
		<div class="flex items-center gap-2 shrink-0">
			<slot name="actions"/>
		</div>
	</header>
</template>
