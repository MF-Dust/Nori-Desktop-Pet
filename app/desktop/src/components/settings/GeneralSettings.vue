<script setup lang="ts">
import {computed, onMounted} from "vue"
import {RUNTIME} from "../../services/runtime"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import {feedback} from "../../services/feedback"
import AppCard from "../ui/AppCard.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"
import Icon from "../Icon.vue"
import zhCn from "../../assets/images/flags/cn.png"
import enUs from "../../assets/images/flags/us.png"

const TEXT = computed(() => useLanguages().views.main.general)

const SAVE_MGR = useSnapshotSave({
	onError: (_key, error) => feedback.error(TEXT.value.telemetry.saveFailed, error),
})
const {defineField, saveNow} = SAVE_MGR

const currentLangField = useSnapshotField(snapshot => snapshot.general.language, "zh-CN")
const currentLang = currentLangField.value

const autoSummonField = defineField(
	"petAutoSummon",
	snapshot => snapshot.general.petAutoSummon,
	true,
	val => RUNTIME.updateGeneral({petAutoSummon: val}),
)
const autoSummon = autoSummonField.value

const telemetryEnabledField = defineField(
	"telemetryEnabled",
	snapshot => snapshot.telemetry.enabled,
	false,
	val => RUNTIME.updateGeneral({telemetryEnabled: val}),
)
const telemetryEnabled = telemetryEnabledField.value

const telemetryAvailable = computed(() => RUNTIME.snapshot.value?.telemetry.available ?? false)
const telemetryConsent = computed(() => RUNTIME.snapshot.value?.telemetry.consent ?? "unset")
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

// 切换语言: 本地立即生效, 失败时回滚到快照语言。
const onLanguageChange = (lang: string) => {
	currentLang.value = lang
	currentLangField.touch()
	void saveNow("language", async () => {
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
	void autoSummonField.saveNow()
}

const onTelemetryChange = (val: boolean) => {
	telemetryEnabled.value = val
	void telemetryEnabledField.saveNow()
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
				<div class="flex flex-wrap gap-2.5">
					<!-- 单选按钮本体用 sr-only 隐藏而非 display:none, 保留键盘可达与读屏语义 -->
					<label
						class="pill-choice focus-ring-within gap-2 px-4 py-2 text-sm"
						:class="currentLang === 'zh-CN' ? 'pill-choice-on' : 'pill-choice-off'"
					>
						<input
							v-model="currentLang"
							type="radio"
							value="zh-CN"
							class="sr-only"
							@change="onLanguageChange('zh-CN')"
						/>
						<span class="w-[2rem] h-[1.4rem] shrink-0 rounded-[0.2rem] overflow-hidden border border-overlay-12">
							<img :src="zhCn" alt="CN" class="w-full h-full object-cover block"/>
						</span>
						<span>{{ TEXT.language.chinese }}</span>
						<Icon v-if="currentLang === 'zh-CN'" name="check" :size="13" class="text-nori-teal-bright ml-0.5"/>
					</label>
					<label
						class="pill-choice focus-ring-within gap-2 px-4 py-2 text-sm"
						:class="currentLang === 'en-US' ? 'pill-choice-on' : 'pill-choice-off'"
					>
						<input
							v-model="currentLang"
							type="radio"
							value="en-US"
							class="sr-only"
							@change="onLanguageChange('en-US')"
						/>
						<span class="w-[2rem] h-[1.4rem] shrink-0 rounded-[0.2rem] overflow-hidden border border-overlay-12">
							<img :src="enUs" alt="US" class="w-full h-full object-cover block"/>
						</span>
						<span>{{ TEXT.language.english }}</span>
						<Icon v-if="currentLang === 'en-US'" name="check" :size="13" class="text-nori-teal-bright ml-0.5"/>
					</label>
				</div>
			</AppCard>

			<!-- 2. 启动与窗口行为 -->
			<AppCard :title="TEXT.startup.title" icon="settings">
				<AppSwitchRow
					:title="TEXT.startup.autoSummon"
					:desc="TEXT.startup.autoSummonDesc"
					:model-value="autoSummon"
					@update:model-value="onAutoSummonChange"
				/>
			</AppCard>

			<!-- 3. 错误遥测与隐私 -->
			<AppCard :title="TEXT.telemetry.title" icon="info">
				<AppSwitchRow
					:title="TEXT.telemetry.enabled"
					:desc="TELEMETRY_DESC"
					:model-value="telemetryEnabled"
					@update:model-value="onTelemetryChange"
				/>
				<span class="text-hint">{{ TELEMETRY_STATUS }}</span>
			</AppCard>
		</div>
	</div>
</template>
