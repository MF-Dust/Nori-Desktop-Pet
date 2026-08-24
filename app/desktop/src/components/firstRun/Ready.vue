<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"
import useLanguages from "../../services/i18n/useLanguages.ts"
import logo from "../../assets/images/logo.png"
import Icon from "../Icon.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"
import type {IconName} from "../../services/icon"

const I18N = computed(() => useLanguages().components.firstRun.ready)

const emit = defineEmits<{
	telemetryChanged: [enabled: boolean]
}>()

const telemetryEnabled = ref(true)
const telemetryConsent = ref<"unset" | "granted" | "denied">("unset")
const telemetryAvailable = ref(false)
const TELEMETRY_DESC = computed(() => {
	if (!telemetryAvailable.value) return I18N.value.telemetry.unavailable
	if (telemetryConsent.value === "unset") return I18N.value.telemetry.pending
	return telemetryEnabled.value ? I18N.value.telemetry.enabled : I18N.value.telemetry.disabled
})

onMounted(async () => {
	try {
		await RUNTIME.init()
		const SNAPSHOT = RUNTIME.snapshot.value
		if (!SNAPSHOT) return
		telemetryConsent.value = SNAPSHOT.telemetry.consent
		telemetryEnabled.value = SNAPSHOT.telemetry.consent !== "denied"
		telemetryAvailable.value = SNAPSHOT.telemetry.available
		emit("telemetryChanged", telemetryEnabled.value)
	} catch (error) {
		feedback.error(I18N.value.telemetry.saveFailed, error)
	}
})

const onTelemetryChange = (value: boolean) => {
	telemetryEnabled.value = value
	telemetryConsent.value = value ? "granted" : "denied"
	emit("telemetryChanged", value)
}

// 就绪摘要 (语言/形象/AI 三项)
const SUMMARY = computed<{icon: IconName; label: string; value: string}[]>(() => [
	{icon: "noriOS", label: I18N.value.summary.language, value: I18N.value.summary.languageValue},
	{icon: "package", label: I18N.value.summary.model, value: I18N.value.summary.modelValue},
	{icon: "cpu", label: I18N.value.summary.ai, value: I18N.value.summary.aiValue},
])
</script>

<template>
	<section key="ready" class="w-full min-h-full flex flex-col items-center justify-center gap-3.5 px-14 pt-4 pb-2.5 text-center">
		<div class="relative w-[11rem] h-[11rem] flex items-center justify-center">
			<span class="absolute -inset-3 rounded-full bg-[radial-gradient(circle,var(--glow-teal-strong)_0%,var(--glow-teal-soft)_55%,transparent_70%)] animate-glow-pulse pointer-events-none"/>
			<img class="relative w-[9.6rem] h-[9.6rem] object-contain animate-breathe" :src="logo" alt="Nori"/>
		</div>

		<div class="flex flex-col items-center gap-1.5">
			<span class="chip-teal">
				<Icon name="sparkles" :size="12"/>
				<span>All Set &amp; Ready</span>
			</span>
			<h2 class="text-3xl font-700 glow-teal">{{ I18N.title }}</h2>
			<p class="text-base text-text-body leading-relaxed max-w-[38rem]">{{ I18N.desc }}</p>
		</div>

		<div class="w-full max-w-[44rem] flex items-center justify-around gap-3 px-4 py-2.5 surface-card backdrop-blur-[0.8rem]">
			<template v-for="(item, index) in SUMMARY" :key="item.label">
				<span v-if="index > 0" class="w-[0.1rem] h-6 bg-line-subtle shrink-0"/>
				<div class="flex items-center gap-2 text-left">
					<span class="w-7 h-7 shrink-0 rounded-sm flex items-center justify-center bg-nori-teal-bright/8 border border-line-subtle text-nori-teal-bright">
						<Icon :name="item.icon" :size="14"/>
					</span>
					<span class="flex flex-col gap-0.5">
						<span class="text-xs text-text-faint">{{ item.label }}</span>
						<span class="text-xs text-text-primary font-500">{{ item.value }}</span>
					</span>
				</div>
			</template>
		</div>

		<div class="w-full max-w-[44rem] px-4 py-3 surface-card text-left">
			<AppSwitchRow :title="I18N.telemetry.title" :desc="TELEMETRY_DESC">
				<n-switch
					:value="telemetryEnabled"
					@update:value="(value: boolean) => onTelemetryChange(value)"
				/>
			</AppSwitchRow>
		</div>

		<span class="chip">
			<Icon name="info" :size="13" class="text-nori-teal-soft"/>
			<span>{{ I18N.initDesc }}</span>
		</span>
	</section>
</template>
