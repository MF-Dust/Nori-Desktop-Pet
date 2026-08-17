<script setup lang="ts">
import {ref, onMounted} from "vue"
import useLanguage from "../../services/i18n"
import type {LanguageType} from "../../services/i18n"
import zhCn from "../../assets/images/flags/cn.png"
import enGb from "../../assets/images/flags/gb.png"
import enUs from "../../assets/images/flags/us.png"

const language = useLanguage

// 语言 code → 本地国旗图片 (来自 flagcdn 下载, 存于 src/assets/images/flags)
const FLAG_MAP: Record<string, string> = {
	"zh-CN": zhCn,
	"zh": zhCn,
	"en": enGb,
	"en-US": enUs
}

// 语言 code → 显示名称 (fallback 用 Intl.DisplayNames)
const NAME_MAP: Record<string, string> = {
	"zh-CN": "简体中文",
	"zh": "简体中文",
	"en": "English",
	"en-US": "English (US)",
}

//
const flagOf = (code: string): string => FLAG_MAP[code] ?? FLAG_MAP[code.split("-")[0]] ?? ""

const nameOf = (code: string): string =>
	NAME_MAP[code] ?? new Intl.DisplayNames([code], {type: "language"}).of(code.split("-")[0]) ?? code

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
	<div class="lang">
		<div class="lang-head">
			<h3 class="lang-title glow-teal">选择语言</h3>
			<p class="lang-sub">{{ current }}</p>
		</div>
		<div class="lang-list">
			<button
				v-for="code in languages"
				:key="code"
				class="lang-item"
				:class="{active: current === code}"
				@click="select(code)"
			>
				<img v-if="flagOf(code)" class="lang-flag" :src="flagOf(code)" :alt="nameOf(code)"/>
				<span v-else class="lang-flag lang-flag-empty"></span>
				<span class="lang-name">{{ nameOf(code) }}</span>
				<span class="lang-check">✓</span>
			</button>
			<p v-if="languages.length === 0" class="lang-empty">暂无可用语言</p>
		</div>
	</div>
</template>

<style scoped lang="less">
.lang {
	display: flex;
	flex-direction: column;
	gap: 10px;
	width: 100%;
}

.lang-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 2px;
}

.lang-title {
	font-size: 16px;
	font-weight: 600;
	color: var(--text-primary);
}

.lang-sub {
	font-size: 11px;
	color: var(--text-muted);
}

// 可向下滚动的语言列表
.lang-list {
	display: flex;
	flex-direction: column;
	gap: 6px;
	max-height: 240px;
	overflow-y: auto;
	width: 100%;
	padding: 2px;
}

.lang-item {
	display: flex;
	align-items: center;
	gap: 10px;
	padding: 8px 12px;
	border: 1px solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 13px;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.12);
		box-shadow: 0 0 10px var(--glow-teal-soft);
	}
}

.lang-flag {
	width: 26px;
	height: 17px;
	object-fit: cover;
	border-radius: 2px;
	flex-shrink: 0;
	box-shadow: 0 0 4px rgba(0, 0, 0, 0.3);

	&.lang-flag-empty {
		background: rgba(255, 255, 255, 0.1);
	}
}

.lang-name {
	flex: 1;
}

.lang-check {
	color: var(--nori-teal);
	font-size: 12px;
	opacity: 0;
	transition: opacity 0.2s ease;

	.active & {
		opacity: 1;
	}
}

.lang-empty {
	font-size: 12px;
	color: var(--text-faint);
	text-align: center;
}
</style>
