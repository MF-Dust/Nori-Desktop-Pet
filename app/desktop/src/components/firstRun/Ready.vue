<script setup lang="ts">
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import logo from "../../assets/images/logo.png"
import Icon from "../Icon.vue"

const I18N = computed(() => useLanguages().components.firstRun.ready)
</script>

<template>
	<section key="ready" class="page page-ready">
		<div class="ready-art-wrap">
			<div class="ready-halo-pulse"></div>
			<img class="ready-star" :src="logo" alt="Nori"/>
		</div>

		<div class="ready-head">
			<span class="ready-badge">
				<Icon name="sparkles" :size="12"/>
				<span>All Set & Ready</span>
			</span>
			<h2 class="ready-title glow-teal">{{ I18N.title }}</h2>
			<p class="ready-desc">{{ I18N.desc }}</p>
		</div>

		<div class="ready-summary-card">
			<div class="summary-item">
				<div class="summary-icon-wrap">
					<Icon name="noriOS" :size="14"/>
				</div>
				<div class="summary-text">
					<span class="summary-label">界面语言</span>
					<span class="summary-val">已偏好配置</span>
				</div>
			</div>

			<div class="summary-divider"/>

			<div class="summary-item">
				<div class="summary-icon-wrap">
					<Icon name="package" :size="14"/>
				</div>
				<div class="summary-text">
					<span class="summary-label">桌宠形象</span>
					<span class="summary-val">Live2D 载入完毕</span>
				</div>
			</div>

			<div class="summary-divider"/>

			<div class="summary-item">
				<div class="summary-icon-wrap">
					<Icon name="cpu" :size="14"/>
				</div>
				<div class="summary-text">
					<span class="summary-label">AI 大脑与设置</span>
					<span class="summary-val">可在主界面扩展</span>
				</div>
			</div>
		</div>

		<div class="ready-tip-row">
			<Icon name="info" :size="13" class="tip-icon"/>
			<span>{{ I18N.initDesc }}</span>
		</div>
	</section>
</template>

<style scoped lang="less">
.page {
	width: 100%;
	height: 100%;
	padding: 1.6rem 5.6rem 1rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.4rem;
	text-align: center;
}

.ready-art-wrap {
	position: relative;
	width: 11rem;
	height: 11rem;
	display: flex;
	align-items: center;
	justify-content: center;
}

.ready-halo-pulse {
	position: absolute;
	inset: -1.2rem;
	border-radius: 50%;
	background: radial-gradient(circle, rgba(94, 234, 212, 0.28) 0%, rgba(125, 227, 255, 0.08) 55%, transparent 70%);
	animation: glow-pulse 3s ease-in-out infinite;
	pointer-events: none;
}

.ready-star {
	width: 9.6rem;
	height: 9.6rem;
	object-fit: contain;
	animation: breathe 2.6s ease-in-out infinite;
	position: relative;
	z-index: 1;
}

.ready-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.5rem;
}

.ready-badge {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.3rem 0.9rem;
	border-radius: var(--radius-pill);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal);
	font-size: 1.1rem;
}

.ready-title {
	font-size: 2.6rem;
	font-weight: 700;
	color: var(--text-primary);
}

.ready-desc {
	font-size: 1.3rem;
	line-height: 1.5;
	color: var(--text-body);
	max-width: 38rem;
}

.ready-summary-card {
	display: flex;
	align-items: center;
	justify-content: space-around;
	padding: 1rem 1.6rem;
	width: 100%;
	max-width: 44rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	backdrop-filter: blur(0.8rem);
	box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.25);
}

.summary-item {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	text-align: left;
}

.summary-icon-wrap {
	width: 2.8rem;
	height: 2.8rem;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	display: flex;
	align-items: center;
	justify-content: center;
	color: var(--nori-teal-bright);
}

.summary-text {
	display: flex;
	flex-direction: column;
	gap: 0.15rem;
}

.summary-label {
	font-size: 1.05rem;
	color: var(--text-faint);
}

.summary-val {
	font-size: 1.15rem;
	color: var(--text-primary);
	font-weight: 500;
}

.summary-divider {
	width: 0.1rem;
	height: 2.4rem;
	background: var(--line-subtle);
}

.ready-tip-row {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	font-size: 1.15rem;
	color: var(--text-muted);
	padding: 0.4rem 1.2rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);

	.tip-icon {
		color: var(--nori-teal-soft);
	}
}
</style>

