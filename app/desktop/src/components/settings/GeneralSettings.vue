<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import {feedback} from "../../services/feedback"
import AppCard from "../ui/AppCard.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const currentLang = ref("zh-CN")
const autoSummon = ref(true)
const appVersion = ref("0.1.0")
const telemetryEnabled = ref(true)
const telemetryConsent = ref<"unset" | "granted" | "denied">("unset")
const telemetryAvailable = ref(false)
const TEXT = computed(() => useLanguages().views.main.general)
const TELEMETRY_DESC = computed(() => {
	if (!telemetryAvailable.value) return TEXT.value.telemetry.unavailable
	if (telemetryConsent.value === "unset") return TEXT.value.telemetry.pending
	return telemetryEnabled.value ? TEXT.value.telemetry.enabledDesc : TEXT.value.telemetry.disabled
})
const TELEMETRY_STATUS = computed(() => {
	if (!telemetryAvailable.value) return TEXT.value.telemetry.unavailable
	if (telemetryConsent.value === "unset") return TEXT.value.telemetry.statusPending
	return telemetryEnabled.value ? TEXT.value.telemetry.statusEnabled : TEXT.value.telemetry.statusDisabled
})

let synced = false
onMounted(async () => {
	await RUNTIME.init()
	const SNAPSHOT = RUNTIME.snapshot.value
	if (!SNAPSHOT || synced) return
	synced = true
	currentLang.value = SNAPSHOT.general.language
	autoSummon.value = SNAPSHOT.general.petAutoSummon
	telemetryConsent.value = SNAPSHOT.telemetry.consent
	telemetryEnabled.value = SNAPSHOT.telemetry.consent !== "denied"
	telemetryAvailable.value = SNAPSHOT.telemetry.available
	appVersion.value = SNAPSHOT.app.appVersion
})

// 切换语言: 本地立即生效, 持久化交给后端 (其他窗口经广播刷新)
const onLanguageChange = (lang: string) => {
	currentLang.value = lang
	void useLanguage.setLanguage(lang)
	void RUNTIME.updateGeneral({language: lang})
}

const onAutoSummonChange = (val: boolean) => {
	autoSummon.value = val
	void RUNTIME.updateGeneral({petAutoSummon: val})
}

const onTelemetryChange = async (val: boolean) => {
	const PREVIOUS = telemetryEnabled.value
	telemetryEnabled.value = val
	try {
		await RUNTIME.updateGeneral({telemetryEnabled: val})
		telemetryConsent.value = val ? "granted" : "denied"
	} catch (error) {
		telemetryEnabled.value = PREVIOUS
		feedback.error(TEXT.value.telemetry.saveFailed, error)
	}
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="TEXT.title" :subtitle="TEXT.subtitle"/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 界面语言 -->
			<AppCard :title="TEXT.language.title" icon="noriOS">
				<div class="flex flex-wrap gap-2">
					<!-- 单选按钮本体用 sr-only 隐藏而非 display:none, 保留键盘可达与读屏语义 -->
					<label
						class="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-pill border text-sm cursor-pointer
							transition-all duration-200
							focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
						:class="currentLang === 'zh-CN'
							? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
							: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-nori-teal-bright/6 border-nori-teal-soft)'"
					>
						<input
							v-model="currentLang"
							type="radio"
							value="zh-CN"
							class="sr-only"
							@change="onLanguageChange('zh-CN')"
						/>
						🇨🇳 {{ TEXT.language.chinese }}
					</label>
					<label
						class="inline-flex items-center gap-1.5 px-3.5 py-1.5 rounded-pill border text-sm cursor-pointer
							transition-all duration-200
							focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
						:class="currentLang === 'en-US'
							? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
							: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-nori-teal-bright/6 border-nori-teal-soft)'"
					>
						<input
							v-model="currentLang"
							type="radio"
							value="en-US"
							class="sr-only"
							@change="onLanguageChange('en-US')"
						/>
						🇺🇸 {{ TEXT.language.english }}
					</label>
				</div>
			</AppCard>

			<!-- 2. 启动与窗口行为 -->
			<AppCard :title="TEXT.startup.title" icon="settings">
				<AppSwitchRow :title="TEXT.startup.autoSummon" :desc="TEXT.startup.autoSummonDesc">
					<n-switch
						:value="autoSummon"
						@update:value="(val: boolean) => onAutoSummonChange(val)"
					/>
				</AppSwitchRow>
			</AppCard>

			<!-- 3. 错误遥测与隐私 -->
			<AppCard :title="TEXT.telemetry.title" icon="info">
				<AppSwitchRow :title="TEXT.telemetry.enabled" :desc="TELEMETRY_DESC">
					<n-switch
						:value="telemetryEnabled"
						:disabled="!telemetryAvailable"
						@update:value="(val: boolean) => onTelemetryChange(val)"
					/>
				</AppSwitchRow>
				<span class="text-hint">{{ TELEMETRY_STATUS }}</span>
			</AppCard>

			<!-- 4. 应用关于信息 -->
			<AppCard :title="TEXT.about.title" icon="info">
				<div class="flex flex-col">
					<div class="flex items-center justify-between gap-3 py-2 border-b border-line-subtle">
						<span class="text-sm text-text-muted">{{ TEXT.about.version }}</span>
						<span class="text-sm text-text-primary mono">v{{ appVersion }}</span>
					</div>
					<div class="flex items-center justify-between gap-3 py-2 border-b border-line-subtle">
						<span class="text-sm text-text-muted">{{ TEXT.about.license }}</span>
						<span class="text-sm text-text-primary mono">GPL-3.0 License</span>
					</div>
					<div class="flex items-center justify-between gap-3 py-2">
						<span class="text-sm text-text-muted">{{ TEXT.about.renderer }}</span>
						<span class="text-sm text-text-primary mono">Avalonia UI + Microsoft WebView2</span>
					</div>
				</div>
			</AppCard>
		</div>
	</div>
</template>
