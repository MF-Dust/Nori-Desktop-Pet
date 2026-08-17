<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages.ts"
import type {LanguageType} from "../../services/i18n"
import {i18n} from "../../services/i18n"
import Icon from "../Icon.vue"
import zhCn from "../../assets/images/flags/cn.png"
import enGb from "../../assets/images/flags/gb.png"
import enUs from "../../assets/images/flags/us.png"

const language = useLanguage

const I18N = computed(() => useLanguages().components.main.settings.language)

// 语言 code → 国旗
const FLAG_MAP: Record<string, string> = {
	"zh-CN": zhCn,
	"zh": zhCn,
	"en": enGb,
	"en-US": enUs,
}

// 语言 code → 显示名
const NAME_MAP: Record<string, string> = {
	"zh-CN": "简体中文",
	"zh": "简体中文",
	"en": "English",
	"en-US": "English (US)",
}

const flagOf = (code: string): string => FLAG_MAP[code] ?? FLAG_MAP[code.split("-")[0]] ?? ""
const nameOf = (code: string): string => NAME_MAP[code] ?? new Intl.DisplayNames([code], {type: "language"}).of(code.split("-")[0]) ?? code

const languages = ref<string[]>([])
const current = ref<LanguageType>("zh-CN")

onMounted(async () => {
	try {
		languages.value = await language.getLanguages()
		current.value = await language.getLanguage()
	} catch (error) {
		console.error(i18n.global.t("log.firstRun.languageListFailed", {error: String(error)}))
	}
})

const select = async (code: string) => {
	if (code === current.value) return
	current.value = code
	try {
		await language.setLanguage(code)
	} catch (error) {
		console.error(i18n.global.t("log.firstRun.languageSwitchFailed", {error: String(error)}))
	}
}
</script>

<template>
	<div class="setting-block">
		<div class="block-head">
			<h3 class="block-title">{{ I18N.title }}</h3>
			<p class="block-sub">{{ I18N.sub }}</p>
		</div>
		<div class="lang-grid">
			<button
				v-for="code in languages"
				:key="code"
				class="lang-card"
				:class="{active: current === code}"
				@click="select(code)"
			>
				<img v-if="flagOf(code)" class="lang-flag" :src="flagOf(code)" :alt="nameOf(code)" draggable="false"/>
				<span v-else class="lang-flag lang-flag-empty"></span>
				<span class="lang-name">{{ nameOf(code) }}</span>
				<span class="lang-check"><icon name="check"/></span>
			</button>
			<p v-if="languages.length === 0" class="lang-empty">{{ I18N.current }}: {{ current }}</p>
		</div>
	</div>
</template>

<style scoped lang="less">
.setting-block {
	display: flex;
	flex-direction: column;
	gap: 1rem;
}

.block-head {
	display: flex;
	flex-direction: column;
	gap: 0.2rem;
}

.block-title {
	font-size: 1.5rem;
	font-weight: 600;
	color: var(--text-primary);
}

.block-sub {
	font-size: 1.1rem;
	color: var(--text-muted);
	line-height: 1.5;
}

.lang-grid {
	display: grid;
	grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
	gap: 0.8rem;
}

.lang-card {
	padding: 0.8rem 1.2rem;
	display: flex;
	align-items: center;
	gap: 1rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background-color: rgba(255, 255, 255, 0.03);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s ease;

	&:hover {
		background-color: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: var(--nori-teal);
		background-color: rgba(125, 227, 255, 0.1);
		box-shadow: 0 0 1rem var(--glow-teal-soft);
	}
}

.lang-flag {
	width: 2.6rem;
	height: 1.7rem;
	object-fit: cover;
	border-radius: 0.2rem;
	flex-shrink: 0;
	box-shadow: 0 0 0.4rem rgba(0, 0, 0, 0.3);

	&.lang-flag-empty {
		background-color: rgba(255, 255, 255, 0.1);
	}
}

.lang-name {
	flex: 1;
}

.lang-check {
	color: var(--nori-teal);
	display: inline-flex;
	align-items: center;
	opacity: 0;
	transition: opacity 0.2s ease;

	:deep(svg) {
		width: 1.4rem;
		height: 1rem;
	}

	.active & {
		opacity: 1;
	}
}

.lang-empty {
	font-size: 1.2rem;
	color: var(--text-faint);
}
</style>
