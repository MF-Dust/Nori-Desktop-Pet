<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import AppChip from "../ui/AppChip.vue"
import AppButton from "../ui/AppButton.vue"
import type {IconMode, IconName} from "../../services/icon"
import {MODEL_LIST} from "../../services/live2d/models"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"

const props = defineProps<{
	petVisible: boolean
}>()

const emit = defineEmits<{
	"toggle-pet": []
	navigate: [tab: "talk" | "model" | "settings"]
}>()

const I18N = computed(() => useLanguages().views.main.home)

// ---- 状态全部来自后端快照 ----
const SNAPSHOT = computed(() => RUNTIME.snapshot.value)
const selectedModelId = computed(() => SNAPSHOT.value?.models.selected ?? "arg-nori")
const currentModel = computed(() => MODEL_LIST.find(model => model.id === selectedModelId.value) ?? MODEL_LIST[0])
const aiConfigured = computed(() => SNAPSHOT.value?.ai.configured ?? false)
const aiProvider = computed(() => SNAPSHOT.value?.ai.provider ?? "")
const aiModel = computed(() => SNAPSHOT.value?.ai.model ?? "")
const enabledSkillsCount = computed(() => SNAPSHOT.value?.enabledSkillsCount ?? 0)
const enabledToolsCount = computed(() => (SNAPSHOT.value?.tools ?? []).filter(tool => tool.enabled).length)
const ENGINE_TEXT = computed(() => {
	switch (SNAPSHOT.value?.platform.os) {
		case "windows": return I18N.value.system.engineWindows
		case "macos": return I18N.value.system.engineMacos
		case "linux": return I18N.value.system.engineLinux
		default: return I18N.value.system.engineUnknown
	}
})

// ---- 快捷动作提示反馈 ----
const motionFeedback = ref(false)
let feedbackTimer: ReturnType<typeof setTimeout> | null = null

// 复制 QQ 提示
const qqCopied = ref(false)
let qqTimer: ReturnType<typeof setTimeout> | null = null

onBeforeUnmount(() => {
	if (feedbackTimer) clearTimeout(feedbackTimer)
	if (qqTimer) clearTimeout(qqTimer)
})

// 社区外链列表
interface CommunityLink {
	key: string
	label: string
	icon: IconName
	mode?: IconMode
	url?: string
	qq?: string
}

const communityLinks = computed<CommunityLink[]>(() => [
	{
		key: "steam",
		label: I18N.value.links.steam,
		icon: "steam",
		mode: "fill",
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
	},
	{
		key: "noriOS",
		label: I18N.value.links.noriOS,
		icon: "noriOS",
		mode: "stroke",
		url: "https://os.inori.ai/landing",
	},
	{
		key: "qq",
		label: qqCopied.value ? I18N.value.links.copied : I18N.value.links.qq,
		icon: "qq",
		mode: "fill",
		qq: "1041616195",
	},
	{
		key: "bilibili",
		label: I18N.value.links.bilibili,
		icon: "bilibili",
		mode: "fill",
		url: "https://space.bilibili.com/326505494",
	},
])

// 磁贴卡片
interface NavCard {
	key: "talk" | "model" | "settings"
	icon: IconName
	iconClass: string
	title: string
	desc: string
	action: string
	status: string
	ok: boolean
}

const NAV_CARDS = computed<NavCard[]>(() => [
	{
		key: "talk",
		icon: "send",
		iconClass: "bg-nori-teal-bright/12 text-nori-teal-bright",
		title: I18N.value.cards.chat.title,
		desc: I18N.value.cards.chat.desc,
		action: I18N.value.cards.chat.action,
		status: aiConfigured.value
			? (aiModel.value ? `${I18N.value.cards.chat.statusConfigured}: ${aiModel.value}` : I18N.value.cards.chat.statusConfigured)
			: I18N.value.cards.chat.statusNotConfigured,
		ok: aiConfigured.value,
	},
	{
		key: "model",
		icon: "package",
		iconClass: "bg-nori-teal/12 text-nori-teal",
		title: I18N.value.cards.model.title,
		desc: I18N.value.cards.model.desc,
		action: I18N.value.cards.model.action,
		status: `${I18N.value.cards.model.current}: ${currentModel.value.name}`,
		ok: true,
	},
	{
		key: "settings",
		icon: "cpu",
		iconClass: "bg-warning/12 text-warning",
		title: I18N.value.cards.ai.title,
		desc: I18N.value.cards.ai.desc,
		action: I18N.value.cards.ai.action,
		status: aiProvider.value
			? `${I18N.value.cards.ai.provider}: ${aiProvider.value}`
			: I18N.value.cards.chat.statusNotConfigured,
		ok: aiConfigured.value,
	},
])

// 快速动作: 触发打招呼/随机动作
const triggerQuickMotion = async () => {
	try {
		const PLAYED = await RUNTIME.petPlayMotion()
		if (!PLAYED) return
		motionFeedback.value = true
		if (feedbackTimer) clearTimeout(feedbackTimer)
		feedbackTimer = setTimeout(() => {
			motionFeedback.value = false
		}, 1500)
	} catch (error) {
		feedback.error(I18N.value.motionFailed, error)
	}
}

// 处理社区外链点击
const handleCommunityClick = async (link: CommunityLink) => {
	if (link.qq) {
		try {
			await RUNTIME.copyText(link.qq)
			qqCopied.value = true
			if (qqTimer) clearTimeout(qqTimer)
			qqTimer = setTimeout(() => {
				qqCopied.value = false
			}, 2000)
			await RUNTIME.writeLog("info", `已复制 QQ 交流群号: ${link.qq}`)
		} catch (error) {
			feedback.error(I18N.value.copyFailed, error)
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

onMounted(() => {
	void RUNTIME.init()
})
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-0.5 py-0.5 scroll-area">
		<!-- 顶部 Hero 角色中枢舞台 -->
		<section
			class="relative overflow-hidden flex items-center justify-between gap-5 p-5 rounded-lg
				bg-gradient-to-r from-bg-card/90 via-bg-card to-bg-panel/40 border border-line-strong backdrop-blur-[1.6rem]
				shadow-[0_0.8rem_3.2rem_rgba(0,0,0,0.45),inset_0_0_0_0.1rem_var(--line-subtle)]"
		>
			<!-- 深海径向光晕背景 -->
			<span class="absolute -top-1/2 -left-[15%] w-[42rem] h-[24rem] opacity-35 pointer-events-none bg-[radial-gradient(circle,var(--glow-teal)_0%,transparent_68%)]"/>
			<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/30 to-transparent pointer-events-none"/>

			<div class="relative flex items-center gap-5 min-w-0">
				<!-- 头像与能量光环 -->
				<div class="relative w-[5.8rem] h-[5.8rem] shrink-0 rounded-full flex items-center justify-center bg-bg-deep/90 border-2 border-nori-teal-bright/35 overflow-hidden shadow-[0_0_2rem_var(--glow-teal-soft)]">
					<img :src="currentModel.thumb" :alt="currentModel.name" class="w-full h-full object-cover object-top transition-transform duration-300 hover:scale-110"/>
					<!-- 在线状态呼吸环 -->
					<span
						class="absolute bottom-0 right-0 w-[1.3rem] h-[1.3rem] rounded-full border-2 border-bg-abyss"
						:class="props.petVisible ? 'bg-success shadow-[0_0_0.8rem_var(--success)] animate-pulse' : 'bg-text-faint'"
					/>
				</div>

				<div class="flex flex-col gap-1.5 min-w-0">
					<div class="flex items-center gap-2.5 flex-wrap">
						<h2 class="title-lg tracking-[0.02rem] text-text-primary [text-shadow:0_0_1.4rem_var(--glow-teal-soft)]">{{ currentModel.name }}</h2>
						<AppChip :tone="props.petVisible ? 'success' : 'neutral'" dot>
							{{ props.petVisible ? I18N.petStatusOnline : I18N.petStatusOffline }}
						</AppChip>
					</div>
					<p class="text-xs text-text-muted leading-relaxed">
						{{ props.petVisible ? I18N.petStatusDescOnline : I18N.petStatusDescOffline }}
					</p>

					<div class="flex items-center gap-2 flex-wrap mt-0.5">
						<AppChip :tone="aiConfigured ? 'teal' : 'neutral'" icon="cpu">
							{{ aiConfigured ? (aiModel || I18N.badge.aiReady) : I18N.badge.aiMissing }}
						</AppChip>
						<AppChip icon="sparkles">
							{{ enabledSkillsCount }} {{ I18N.badge.skills }} / {{ enabledToolsCount }} {{ I18N.badge.tools }}
						</AppChip>
					</div>
				</div>
			</div>

			<div class="relative flex items-center gap-2.5 shrink-0">
				<AppButton
					:variant="props.petVisible ? 'ghost' : 'primary'"
					:icon="props.petVisible ? 'close' : 'sparkles'"
					class="px-4 py-2"
					@click="emit('toggle-pet')"
				>
					{{ props.petVisible ? I18N.hidePet : I18N.summonPet }}
				</AppButton>

				<AppButton
					v-if="props.petVisible"
					icon="sparkles"
					:disabled="motionFeedback"
					class="px-3.5 py-2"
					@click="triggerQuickMotion"
				>
					{{ motionFeedback ? I18N.quickMotionDone : I18N.quickMotion }}
				</AppButton>
			</div>
		</section>

		<!-- 中部立体导航磁贴网格 -->
		<section class="grid gap-3.5 grid-cols-1 md:grid-cols-3">
			<button
				v-for="card in NAV_CARDS"
				:key="card.key"
				type="button"
				class="group relative overflow-hidden min-h-[14.5rem] flex flex-col justify-between p-4.5 text-left
					surface-card cursor-pointer transition-all duration-200 focus-ring
					hover:(border-line-glow bg-bg-card-hover -translate-y-[0.2rem] shadow-[0_0.8rem_2.4rem_rgba(0,0,0,0.5),0_0_1.6rem_var(--glow-teal-soft)])"
				@click="emit('navigate', card.key)"
			>
				<!-- 顶部悬浮光线 -->
				<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/0 to-transparent transition-all duration-300 group-hover:via-nori-teal-bright/40"/>

				<div>
					<div class="flex items-center justify-between gap-2 mb-3">
						<span
							class="w-[3.6rem] h-[3.6rem] rounded-sm flex items-center justify-center border border-line-subtle text-nori-teal-bright bg-nori-teal-bright/10 transition-all duration-200 group-hover:(scale-110 border-nori-teal-bright/40 shadow-[0_0_1.2rem_var(--glow-teal-soft)])"
						>
							<Icon :name="card.icon" :size="18"/>
						</span>
						<AppChip :tone="card.ok ? 'teal' : 'neutral'" dot>{{ card.status }}</AppChip>
					</div>

					<div class="flex flex-col gap-1.2">
						<span class="title-sm text-text-primary group-hover:text-nori-teal-bright transition-colors">{{ card.title }}</span>
						<span class="text-xs text-text-muted leading-relaxed">{{ card.desc }}</span>
					</div>
				</div>

				<div
					class="mt-3.5 flex items-center justify-between pt-2.5 border-t border-line-subtle text-xs text-text-muted transition-colors
						group-hover:text-nori-teal-bright"
				>
					<span class="font-500">{{ card.action }}</span>
					<Icon name="arrow-right" :size="14" class="transition-transform duration-200 group-hover:translate-x-1"/>
				</div>
			</button>
		</section>

		<!-- 底部生态社区与系统状态 -->
		<section class="flex flex-col gap-3 pt-3 border-t border-line-subtle">
			<div>
				<h4 class="text-hint mb-2 uppercase font-600 tracking-[0.06rem]">{{ I18N.links.title }}</h4>
				<div class="flex flex-wrap gap-2.5">
					<button
						v-for="item in communityLinks"
						:key="item.key"
						type="button"
						class="btn-ghost px-3.5 py-1.8"
						:class="item.key === 'qq' && qqCopied ? 'bg-success/15 border-success/40 text-success' : ''"
						@click="handleCommunityClick(item)"
					>
						<Icon :name="item.icon" :mode="item.mode" :size="14"/>
						<span class="font-500">{{ item.label }}</span>
					</button>
				</div>
			</div>

			<div class="flex items-center gap-2.5 flex-wrap text-xs text-text-faint pt-1">
				<span class="flex items-center">
					<span>{{ I18N.system.appVersion }}:</span>
					<span class="ml-1 text-text-muted mono font-600">v{{ SNAPSHOT?.app.appVersion ?? "0.1.0" }}</span>
				</span>
				<span class="opacity-30">/</span>
				<span class="flex items-center">
					<span>{{ I18N.system.webview }}:</span>
					<span class="ml-1 text-text-muted">{{ ENGINE_TEXT }}</span>
				</span>
				<span class="opacity-30">/</span>
				<span class="inline-flex items-center gap-1.5 text-success font-500">
					<span class="w-1.5 h-1.5 rounded-full bg-success shadow-[0_0_0.6rem_var(--success)]"/>
					<span>{{ I18N.system.statusNormal }}</span>
				</span>
			</div>
		</section>
	</div>
</template>
