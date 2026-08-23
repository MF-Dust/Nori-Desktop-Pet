<script setup lang="ts">
/**
 * 关于 Nori
 *
 * 原来挂在主窗口侧边导航的「声明」页, 现在并入设置的二级列表。
 */
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import AppChip from "../ui/AppChip.vue"
import {RUNTIME} from "../../services/runtime"

const I18N = computed(() => useLanguages().views.main.about)
const GENERAL_I18N = computed(() => useLanguages().views.main.general.about)
const SNAPSHOT = computed(() => RUNTIME.snapshot.value)
</script>

<template>
	<section class="flex-1 min-h-0 flex items-center justify-center p-5">
		<div class="w-full max-w-[50rem] flex flex-col items-center gap-4 px-7 py-8 text-center glow-card rounded-lg relative overflow-hidden">
			<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/30 to-transparent pointer-events-none"/>

			<span class="w-14 h-14 rounded-full flex items-center justify-center bg-nori-teal-bright/10 border border-nori-teal-bright/30 text-nori-teal-bright shadow-[0_0_2rem_var(--glow-teal-soft)]">
				<Icon name="sparkles" :size="28"/>
			</span>

			<h2 class="text-2xl font-700 glow-teal">{{ I18N.title }}</h2>

			<div class="flex flex-wrap justify-center gap-2">
				<AppChip tone="teal" dot>{{ I18N.license }}</AppChip>
				<AppChip>{{ I18N.authors }}</AppChip>
			</div>

			<p class="text-sm text-text-muted leading-relaxed max-w-[38rem]">{{ I18N.desc }}</p>

			<dl class="w-full max-w-[38rem] flex flex-col gap-0 mt-2 p-3.5 rounded-md bg-white/3 border border-line-subtle">
				<div class="flex items-center justify-between py-2 border-b border-line-subtle">
					<dt class="text-sm text-text-muted font-500">{{ GENERAL_I18N.version }}</dt>
					<dd class="text-sm text-text-primary mono font-600">v{{ SNAPSHOT?.app.appVersion ?? "0.1.0" }}</dd>
				</div>
				<div class="flex items-center justify-between py-2 border-b border-line-subtle">
					<dt class="text-sm text-text-muted font-500">{{ GENERAL_I18N.license }}</dt>
					<dd class="text-sm text-text-primary mono">GPL-3.0</dd>
				</div>
				<div class="flex items-center justify-between py-2">
					<dt class="text-sm text-text-muted font-500">{{ GENERAL_I18N.renderer }}</dt>
					<dd class="text-sm text-text-primary font-500">Avalonia UI + NativeWebView</dd>
				</div>
			</dl>
		</div>
	</section>
</template>
