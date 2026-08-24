<script setup lang="ts">
import {computed, onMounted} from "vue"
import {RUNTIME} from "../../services/runtime"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import {feedback} from "../../services/feedback"
import {APP_VERSION} from "../../services/version"
import AppCard from "../ui/AppCard.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const currentLangField = useSnapshotField(snapshot => snapshot.general.language, "zh-CN")
const autoSummonField = useSnapshotField(snapshot => snapshot.general.petAutoSummon, true)
const telemetryEnabledField = useSnapshotField(snapshot => snapshot.telemetry.enabled, false)
const currentLang = currentLangField.value
const autoSummon = autoSummonField.value
const telemetryEnabled = telemetryEnabledField.value
const appVersion = computed(() => RUNTIME.snapshot.value?.app.productVersion ?? RUNTIME.snapshot.value?.app.appVersion ?? APP_VERSION)
const telemetryAvailable = computed(() => RUNTIME.snapshot.value?.telemetry.available ?? false)
const telemetryConsent = computed(() => RUNTIME.snapshot.value?.telemetry.consent ?? "unset")
const SAFE_MODE = computed(() => RUNTIME.snapshot.value?.app.safeMode ?? false)
const TEXT = computed(() => useLanguages().views.main.general)
const ENGINE_TEXT = computed(() => {
	switch (RUNTIME.snapshot.value?.platform.os) {
		case "windows": return TEXT.value.about.engineWindows
		case "macos": return TEXT.value.about.engineMacos
		case "linux": return TEXT.value.about.engineLinux
		default: return TEXT.value.about.engineUnknown
	}
})
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

const SAVE = useDebouncedSave({
	onError: (key, error) => {
		if (key === "language") currentLangField.reset()
		if (key === "petAutoSummon") autoSummonField.reset()
		if (key === "telemetryEnabled") telemetryEnabledField.reset()
		feedback.error(TEXT.value.telemetry.saveFailed, error)
	},

})

// 切换语言: 本地立即生效, 失败时回滚到快照语言。
const onLanguageChange = (lang: string) => {
	currentLang.value = lang
	currentLangField.touch()
	void SAVE.saveNow("language", async () => {
		try {
			await useLanguage.setLanguage(lang)
			await RUNTIME.updateGeneral({language: lang})
			currentLangField.commit()
		} catch (error) {
			currentLangField.reset()
			await useLanguage.setLanguage(currentLang.value)
			throw error
		}
	})
}

const onAutoSummonChange = (val: boolean) => {
	autoSummon.value = val
	autoSummonField.touch()
	void SAVE.saveNow("petAutoSummon", async () => {
		try {
			await RUNTIME.updateGeneral({petAutoSummon: val})
			autoSummonField.commit()
		} catch (error) {
			autoSummonField.reset()
			throw error
		}
	})
}

const onTelemetryChange = (val: boolean) => {
	telemetryEnabled.value = val
	telemetryEnabledField.touch()
	void SAVE.saveNow("telemetryEnabled", async () => {
		try {
			await RUNTIME.updateGeneral({telemetryEnabled: val})
			telemetryEnabledField.commit()
		} catch (error) {
			telemetryEnabledField.reset()
			throw error
		}
	})
}

onMounted(() => {
	void RUNTIME.init().catch(error => feedback.error(TEXT.value.telemetry.saveFailed, error))
})
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
						<span class="text-sm text-text-primary mono">{{ TEXT.about.licenseValue }}</span>
					</div>
					<div class="flex items-center justify-between gap-3 py-2 border-b border-line-subtle">
						<span class="text-sm text-text-muted">{{ TEXT.about.safeMode }}</span>
						<span class="text-sm mono" :class="SAFE_MODE ? 'text-warning' : 'text-text-primary'">
							{{ SAFE_MODE ? TEXT.about.safeModeEnabled : TEXT.about.safeModeDisabled }}
						</span>
					</div>
					<div class="flex items-center justify-between gap-3 py-2">
						<span class="text-sm text-text-muted">{{ TEXT.about.renderer }}</span>
						<span class="text-sm text-text-primary mono">{{ ENGINE_TEXT }}</span>
					</div>
				</div>
			</AppCard>
		</div>
	</div>
</template>
