<script setup lang="ts">
/**
 * 骨架占位 (异步面板加载中)
 */
withDefaults(defineProps<{
	rows?: number
}>(), {
	rows: 3,
})
</script>

<template>
	<div class="flex flex-col gap-3 p-4" aria-hidden="true">
		<div
			v-for="row in rows"
			:key="row"
			class="h-8 w-full rounded-sm bg-overlay-4 animate-pulse"
		/>
	</div>
</template>
