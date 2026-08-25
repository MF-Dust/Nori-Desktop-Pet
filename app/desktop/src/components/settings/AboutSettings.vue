<script setup lang="ts">
/**
 * 关于 Nori
 *
 * 版本 / 协议 / 渲染引擎 / 运行模式这些静态信息只属于这一页,
 * 主页与「系统与常规」都不再重复展示。
 */
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppChip from "../ui/AppChip.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import {RUNTIME} from "../../services/runtime"
import {APP_VERSION} from "../../services/version"

const I18N = computed(() => useLanguages().views.main.about)
const SNAPSHOT = computed(() => RUNTIME.snapshot.value)

const APP_VERSION_TEXT = computed(() => SNAPSHOT.value?.app.productVersion ?? SNAPSHOT.value?.app.appVersion ?? APP_VERSION)
const SAFE_MODE = computed(() => SNAPSHOT.value?.app.safeMode ?? false)
const ENGINE_TEXT = computed(() => {
	switch (SNAPSHOT.value?.platform.os) {
		case "windows": return I18N.value.engineWindows
		case "macos": return I18N.value.engineMacos
		case "linux": return I18N.value.engineLinux
		default: return I18N.value.engineUnknown
	}
})

// 环境信息表 (标签 + 值 + 是否高亮)
const ENV_ROWS = computed<{key: string; label: string; value: string; warn?: boolean}[]>(() => [
	{key: "version", label: I18N.value.version, value: APP_VERSION_TEXT.value},
	{key: "license", label: I18N.value.license, value: I18N.value.licenseValue},
	{key: "renderer", label: I18N.value.renderer, value: ENGINE_TEXT.value},
	{
		key: "safeMode",
		label: I18N.value.safeMode,
		value: SAFE_MODE.value ? I18N.value.safeModeEnabled : I18N.value.safeModeDisabled,
		warn: SAFE_MODE.value,
	},
])
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.subtitle"/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 品牌与协议声明 -->
			<section class="relative overflow-hidden flex flex-col items-center gap-3 px-7 py-7 text-center glow-card rounded-lg">
				<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/30 to-transparent pointer-events-none"/>

				<span class="w-14 h-14 rounded-full flex items-center justify-center bg-nori-teal-bright/10 border border-nori-teal-bright/30 text-nori-teal-bright shadow-[0_0_2rem_var(--glow-teal-soft)]">
					<Icon name="sparkles" :size="28"/>
				</span>

				<div class="flex flex-wrap justify-center gap-2">
					<AppChip tone="teal" dot>{{ I18N.licenseNotice }}</AppChip>
					<AppChip>{{ I18N.authors }}</AppChip>
				</div>

				<p class="text-sm text-text-muted leading-relaxed max-w-[38rem]">{{ I18N.desc }}</p>
			</section>

			<!-- 运行环境 -->
			<AppCard :title="I18N.env" icon="info">
				<dl class="flex flex-col">
					<div
						v-for="(row, index) in ENV_ROWS"
						:key="row.key"
						class="flex items-center justify-between gap-3 py-2"
						:class="index < ENV_ROWS.length - 1 ? 'border-b border-line-subtle' : ''"
					>
						<dt class="text-sm text-text-muted">{{ row.label }}</dt>
						<dd class="text-sm mono truncate" :class="row.warn ? 'text-warning' : 'text-text-primary'">{{ row.value }}</dd>
					</div>
				</dl>
			</AppCard>
		</div>
	</div>
</template>
