<script setup lang="ts">
import {computed, ref, onMounted} from "vue"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages.ts"
import type {LanguageType} from "../../services/i18n"
import zhCn from "../../assets/images/flags/cn.png"
import enGb from "../../assets/images/flags/gb.png"
import enUs from "../../assets/images/flags/us.png"
import Icon from "../../components/Icon.vue"

const language = useLanguage

const I18N = computed(() => useLanguages().components.firstRun.languageSelect)

// 语言 code → 本地国旗图片 (来自 flagcdn 下载, 存于 src/assets/images/flags)
const FLAG_MAP: Record<string, string> = {
	"zh-CN": zhCn,
	"zh": zhCn,
	"en": enGb,
	"en-US": enUs
}

// 语言 code → 显示名称 (fallback 用 Intl.DisplayNames)
const NAME_MAP: Record<string, {name: string; sub: string}> = {
	"zh-CN": {name: "简体中文", sub: "Chinese (Simplified)"},
	"zh": {name: "简体中文", sub: "Chinese (Simplified)"},
	"en": {name: "English", sub: "English (UK)"},
	"en-US": {name: "English (US)", sub: "American English"},
}

const flagOf = (code: string): string => FLAG_MAP[code] ?? FLAG_MAP[code.split("-")[0]] ?? ""

const nameInfoOf = (code: string): {name: string; sub: string} => {
	if (NAME_MAP[code]) return NAME_MAP[code]
	const autoName = new Intl.DisplayNames([code], {type: "language"}).of(code.split("-")[0]) || code
	return {name: autoName, sub: code}
}

// 可用语言列表
const languages = ref<string[]>([])

// 当前语言
const current = ref<LanguageType>("zh-CN")

// 加载语言列表和当前语言
onMounted(async () => {
	try {
		languages.value = await language.getLanguages()
		current.value = await language.getLanguage()
	} catch (error) {
		console.error("加载语言列表失败:", error)
	}
})

// 切换语言
const select = async (code: string) => {
	current.value = code
	try {
		await language.setLanguage(code)
	} catch (error) {
		console.error("切换语言失败:", error)
	}
}
</script>

<template>
	<div class="lang-page">
		<div class="lang-head">
			<span class="lang-badge">
				<Icon name="noriOS" :size="12"/>
				<span>Language Preference</span>
			</span>
			<h2 class="lang-title glow-teal">{{ I18N.title }}</h2>
			<p class="lang-sub">请选择您希望与 Nori 交互及阅读界面的主要语言</p>
		</div>

		<div class="lang-grid">
			<button
				v-for="code in languages"
				:key="code"
				class="lang-card"
				:class="{active: current === code}"
				@click="select(code)"
			>
				<div class="flag-wrap">
					<img v-if="flagOf(code)" class="lang-flag" :src="flagOf(code)" :alt="nameInfoOf(code).name"/>
					<span v-else class="lang-flag lang-flag-empty"></span>
				</div>

				<div class="lang-info">
					<span class="lang-name">{{ nameInfoOf(code).name }}</span>
					<span class="lang-subname">{{ nameInfoOf(code).sub }}</span>
				</div>

				<div class="lang-check-pill">
					<Icon name="check" :size="12"/>
				</div>
			</button>

			<p v-if="languages.length === 0" class="lang-empty">{{ I18N.langEmpty }}</p>
		</div>
	</div>
</template>

<style scoped lang="less">
.lang-page {
	width: 100%;
	height: 100%;
	padding: 1.6rem 5.6rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 2.2rem;
}

.lang-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.6rem;
	text-align: center;
}

.lang-badge {
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

.lang-title {
	font-size: 2.4rem;
	font-weight: 700;
	color: var(--text-primary);
}

.lang-sub {
	font-size: 1.25rem;
	color: var(--text-faint);
}

.lang-grid {
	width: 100%;
	max-width: 48rem;
	display: grid;
	grid-template-columns: 1fr 1fr;
	gap: 1.4rem;
}

.lang-card {
	padding: 1.4rem 1.6rem;
	display: flex;
	align-items: center;
	gap: 1.2rem;
	border: 0.15rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-primary);
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
	position: relative;
	overflow: hidden;

	&::before {
		content: "";
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: radial-gradient(circle at 10% 20%, rgba(125, 227, 255, 0.1) 0%, transparent 60%);
		opacity: 0;
		transition: opacity 0.25s ease;
	}

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		transform: translateY(-0.2rem);
		box-shadow: 0 0.6rem 2rem rgba(0, 0, 0, 0.3), 0 0 1.2rem var(--glow-teal-soft);

		&::before {
			opacity: 1;
		}
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.12);
		box-shadow: 0 0.6rem 2rem rgba(0, 0, 0, 0.4), 0 0 1.6rem var(--glow-teal);

		.lang-check-pill {
			opacity: 1;
			transform: scale(1);
			background: var(--nori-teal);
			color: #05121a;
		}

		.lang-name {
			color: var(--nori-teal-bright);
			font-weight: 600;
		}
	}
}

.flag-wrap {
	width: 3.8rem;
	height: 2.6rem;
	border-radius: 0.4rem;
	overflow: hidden;
	flex-shrink: 0;
	box-shadow: 0 0.2rem 0.8rem rgba(0, 0, 0, 0.4);
	border: 0.1rem solid rgba(255, 255, 255, 0.1);
}

.lang-flag {
	width: 100%;
	height: 100%;
	object-fit: cover;
	display: block;

	&.lang-flag-empty {
		background-color: rgba(255, 255, 255, 0.1);
	}
}

.lang-info {
	flex: 1;
	display: flex;
	flex-direction: column;
	gap: 0.2rem;
	min-width: 0;
}

.lang-name {
	font-size: 1.35rem;
	font-weight: 500;
	color: var(--text-primary);
	white-space: nowrap;
}

.lang-subname {
	font-size: 1.05rem;
	color: var(--text-faint);
	white-space: nowrap;
}

.lang-check-pill {
	width: 2rem;
	height: 2rem;
	border-radius: 50%;
	display: flex;
	align-items: center;
	justify-content: center;
	opacity: 0;
	transform: scale(0.6);
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	flex-shrink: 0;
}

.lang-empty {
	grid-column: 1 / -1;
	font-size: 1.2rem;
	color: var(--text-faint);
	text-align: center;
	padding: 2rem 0;
}
</style>

