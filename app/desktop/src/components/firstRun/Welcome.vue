<script setup lang="ts">
import {computed, onBeforeUnmount, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {APP_VERSION} from "../../services/version"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../../components/Icon.vue"
import {feedback} from "../../services/feedback"
import type {IconMode, IconName} from "../../services/icon"
import logo from "../../assets/images/logo.png"

const I18N = computed(() => useLanguages().components.firstRun.welcome)
const VERSION = computed(() => RUNTIME.snapshot.value?.app.productVersion ?? RUNTIME.snapshot.value?.app.appVersion ?? APP_VERSION)

// 推广链接
interface Link {
	key: string
	label: string
	sub: string
	url?: string
	qq?: string
	mode?: IconMode
	icon: IconName
}

// 复制状态提示
const copiedQq = ref(false)
let copyTimer: ReturnType<typeof setTimeout> | null = null

onBeforeUnmount(() => {
	if (copyTimer) clearTimeout(copyTimer)
})

// 推广链接 (响应式: 随语言重算)
const links = computed<Link[]>(() => [
	{
		key: "steam",
		label: I18N.value.links.steam.label,
		sub: I18N.value.links.steam.sub,
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
		mode: "fill",
		icon: "steam",
	},
	{
		key: "noriOS",
		label: I18N.value.links.noriOS.label,
		sub: I18N.value.links.noriOS.sub,
		url: "https://os.inori.ai/landing",
		mode: "stroke",
		icon: "noriOS",
	},
	{
		key: "qq",
		label: copiedQq.value ? I18N.value.links.qq.copiedLabel : I18N.value.links.qq.label,
		sub: copiedQq.value ? I18N.value.links.qq.copiedSub : I18N.value.links.qq.sub,
		qq: "1041616195",
		mode: "fill",
		icon: "qq",
	},
	{
		key: "bilibili",
		label: I18N.value.links.bilibili.label,
		sub: I18N.value.links.bilibili.sub,
		url: "https://space.bilibili.com/326505494",
		mode: "fill",
		icon: "bilibili",
	},
])

// 特性标签
const FEATURES = computed<{icon: IconName; text: string}[]>(() => [
	{icon: "sparkles", text: I18N.value.features.live2d},
	{icon: "cpu", text: I18N.value.features.ai},
	{icon: "package", text: I18N.value.features.local},
])

// 点击链接卡片: 有 qq 属性则复制群号, 否则打开网页
const handleLink = async (link: Link) => {
	if (link.qq) {
		try {
			await RUNTIME.copyText(link.qq)
			copiedQq.value = true
			if (copyTimer) clearTimeout(copyTimer)
			copyTimer = setTimeout(() => {
				copiedQq.value = false
			}, 2500)
			await RUNTIME.writeLog("info", `复制 QQ 群号 ${link.qq} 成功`)
		} catch (error) {
			feedback.error(I18N.value.links.qq.copyFailed, error)
			await RUNTIME.writeLog("error", `复制 QQ 群号 ${link.qq} 失败`)
		}
		return
	}
	if (link.url) {
		try {
			await RUNTIME.openUrl(link.url)
		} catch (error) {
			feedback.error(I18N.value.openFailed, error)
		}
	}
}
</script>

<template>
	<section key="welcome" data-first-run-step="welcome" class="w-full min-h-full flex flex-row items-center gap-9 px-12 pt-3 pb-2.5">
		<div class="flex-1 min-w-0 flex flex-col items-start gap-2.5">
			<div class="flex items-center gap-1.5">
				<span class="chip-teal">
					<Icon name="sparkles" :size="12"/>
					<span>Live2D Cyber Pet</span>
				</span>
				<span class="chip mono">{{ VERSION }}</span>
			</div>

			<h1 class="text-3xl font-800 glow-teal">{{ I18N.title }}</h1>
			<p class="text-base text-text-body leading-relaxed max-w-[42rem]">{{ I18N.subtitle }}</p>

			<div class="flex flex-wrap items-center gap-2">
				<span v-for="item in FEATURES" :key="item.text" class="chip">
					<Icon :name="item.icon" :size="11"/>
					<span>{{ item.text }}</span>
				</span>
			</div>

			<div class="grid grid-cols-2 gap-2.5 w-full mt-1">
				<button
					v-for="link in links"
					:key="link.key"
					type="button"
					class="group flex items-center gap-2.5 px-3 py-2.5 rounded-md text-left cursor-pointer
						bg-overlay-4 border border-line-subtle transition-all duration-250 focus-ring
						hover:(bg-nori-teal-bright/8 border-nori-teal-soft -translate-y-[0.15rem] shadow-[0_0.4rem_1.6rem_var(--glow-teal-soft)])"
					:class="link.key === 'qq' && copiedQq ? 'bg-success/12 border-success/40' : ''"
					@click="handleLink(link)"
				>
					<span
						class="w-8 h-8 shrink-0 rounded-sm flex items-center justify-center
							bg-nori-teal-bright/8 border border-line-subtle text-nori-teal-bright transition-transform duration-200
							group-hover:scale-110"
						:class="link.key === 'qq' && copiedQq ? 'text-success' : ''"
					>
						<Icon :name="link.icon" :mode="link.mode" :size="16"/>
					</span>
					<span class="flex flex-col gap-0.5 min-w-0 flex-1">
						<span class="text-sm text-text-primary font-500 truncate">{{ link.label }}</span>
						<span class="text-xs text-text-faint truncate">{{ link.sub }}</span>
					</span>
					<span class="shrink-0 text-text-faint transition-transform duration-200 group-hover:translate-x-1">
						<Icon :name="link.key === 'qq' && copiedQq ? 'check' : 'arrow-right'" :size="13"/>
					</span>
				</button>
			</div>
		</div>

		<div class="relative shrink-0 w-[22rem] h-[26rem] flex flex-col items-center justify-center">
			<span class="absolute w-[20rem] h-[20rem] rounded-full border border-dashed border-nori-teal-bright/25 [animation:rotate_18s_linear_infinite]"/>
			<span class="absolute w-[15rem] h-[15rem] rounded-full bg-[radial-gradient(circle,var(--glow-teal)_0%,transparent_70%)] animate-glow-pulse"/>
			<img class="relative w-[15rem] h-[15rem] object-contain animate-breathe" :src="logo" alt="Nori"/>
			<span class="relative mt-2 text-sm tracking-[0.6rem] text-nori-teal-soft">- N O R I -</span>
		</div>
	</section>
</template>
