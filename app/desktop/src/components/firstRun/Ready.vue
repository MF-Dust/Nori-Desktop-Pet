<script setup lang="ts">
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import logo from "../../assets/images/logo.png"

const I18N = computed(() => useLanguages().components.firstRun.ready)
</script>

<template>
	<section key="ready" class="page page-ready">
		<img class="ready-star" :src="logo" alt="Nori"/>
		<h2 class="ready-title glow-teal">{{ I18N.title }}</h2>
		<p class="ready-desc">{{ I18N.desc }}</p>
		<span class="ready-tip">{{ I18N.initDesc }}</span>
	</section>
</template>

<style scoped lang="less">
.page {
	padding-bottom: 0.6rem;
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.6rem;
}

.ready-star {
	width: 10.4rem;
	height: 10.4rem;
	object-fit: contain;
	animation: breathe 2.6s ease-in-out infinite;
	filter: drop-shadow(0 0 2.2rem rgba(125, 227, 255, 0.6));
}

.ready-title {
	font-size: 2.8rem;
	font-weight: 700;
	color: var(--text-primary);
}

.ready-desc {
	font-size: 1.4rem;
	line-height: 1.8;
	color: var(--text-body);
	text-align: center;
}

.ready-tip {
	font-size: 1.2rem;
	color: var(--text-faint);
	padding: 0.6rem 1.4rem;
	border-radius: 99.9rem;
	background: rgba(255, 255, 255, 0.05);
	border: 0.1rem solid var(--line-subtle);
}
</style>
