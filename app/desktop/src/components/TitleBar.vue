<script setup lang="ts">
defineSlots<{
	default: () => unknown
}>()
</script>

<template>
	<div class="titlebar" data-tauri-drag-region>
		<span class="title" data-tauri-drag-region>Nori</span>
		<slot/>
	</div>
</template>

<style scoped lang="less">
.titlebar {
	height: 4.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 1.2rem 0 1.6rem;
	flex-shrink: 0;

	.title {
		color: var(--text-primary);
		font-size: 1.3rem;
		font-weight: 600;
		letter-spacing: 0.05rem;
	}
}
</style>
