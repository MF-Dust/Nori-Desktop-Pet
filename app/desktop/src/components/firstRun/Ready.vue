<script setup lang="ts">
</script>

<template>
	<section key="ready" class="page page-ready">
		<span class="ready-star">✨</span>
		<h2 class="ready-title glow-teal">准备好了吗</h2>
		<p class="ready-desc">点击「开始」完成初始化, Nori 即将与你见面。</p>
		<span class="ready-tip">🐾 初始化大约只需几秒钟</span>
	</section>
</template>

<style scoped lang="less">
.page {
	position: absolute;
	inset: 0;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 16px;
	padding-bottom: 6px;
}

.ready-star {
	font-size: 56px;
	animation: star-pop 2.4s ease-in-out infinite;
	filter: drop-shadow(0 0 22px rgba(125, 227, 255, 0.6));
}

@keyframes star-pop {
	0%, 100% {
		transform: scale(1) rotate(0deg);
	}
	50% {
		transform: scale(1.12) rotate(8deg);
	}
}

.ready-title {
	font-size: 28px;
	font-weight: 700;
	color: var(--text-primary);
}

.ready-desc {
	font-size: 14px;
	line-height: 1.8;
	color: var(--text-body);
	text-align: center;
}

.ready-tip {
	font-size: 12px;
	color: var(--text-faint);
	padding: 6px 14px;
	border-radius: 999px;
	background: rgba(255, 255, 255, 0.05);
	border: 1px solid var(--line-subtle);
}
</style>
