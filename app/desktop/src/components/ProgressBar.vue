<script setup lang="ts">
defineProps<{
	percent: number
	text?: string
}>()
</script>

<template>
	<div class="progress">
		<div class="progress-track">
			<div class="progress-bar" :style="{width: percent + '%'}"/>
		</div>
		<div v-if="text" class="progress-text">{{ text }}</div>
	</div>
</template>

<style scoped lang="less">
.progress {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.6rem;
}

.progress-track {
	width: 20rem;
	height: 0.4rem;
	border-radius: 0.2rem;
	background: rgba(255, 255, 255, 0.08);
	overflow: hidden;
}

.progress-bar {
	height: 100%;
	border-radius: 0.2rem;
	background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	transition: width 0.4s ease;
}

.progress-text {
	color: var(--text-faint);
	font-size: 1.1rem;
	font-variant-numeric: tabular-nums;
}
</style>
