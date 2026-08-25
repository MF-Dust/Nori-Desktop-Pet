<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppCard from "../ui/AppCard.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"
import AppButton from "../ui/AppButton.vue"

const I18N = computed(() => useLanguages().views.main.voice)

const SNAPSHOT = computed(() => RUNTIME.snapshot.value)
const VOICE = computed(() => SNAPSHOT.value?.voice)

// 字段级防抖保存 (每字段独立计时器, 卸载时自动 flush, 失败走反馈层)
const SAVE_MGR = useSnapshotSave({
	onError: (_key, error) => feedback.error(I18N.value.saveFailed, error),
})
const {defineField, save} = SAVE_MGR

// 全局音量 (0 ~ 100)
const volumeField = defineField(
	"volume",
	snapshot => Math.round(snapshot.voice.volume * 100),
	100,
	val => RUNTIME.updateVoice({volume: String(val / 100)}),
)
const volume = volumeField.value

// TTS 配置 (云端路径: openai / custom / gpt_sovits)
type TtsProvider = "openai" | "custom" | "gpt_sovits"
const ttsProviderField = defineField<TtsProvider>(
	"ttsProvider",
	snapshot => {
		const VALUE = snapshot.voice.ttsProvider
		return (["openai", "custom", "gpt_sovits"] as string[]).includes(VALUE) ? VALUE as TtsProvider : "openai"
	},
	"openai",
	val => RUNTIME.updateVoice({ttsProvider: val}),
)
const ttsProvider = ttsProviderField.value

const ttsBaseUrlField = defineField(
	"tts_base_url",
	snapshot => snapshot.voice.ttsBaseUrl,
	"",
	val => RUNTIME.updateVoice({ttsBaseUrl: val.trim()}),
)
const ttsBaseUrl = ttsBaseUrlField.value

const ttsApiKeyInput = ref("")

const ttsVoiceField = defineField(
	"tts_voice",
	snapshot => snapshot.voice.ttsVoice || "nova",
	"nova",
	val => RUNTIME.updateVoice({ttsVoice: val.trim()}),
)
const ttsVoice = ttsVoiceField.value

const ttsSpeedField = defineField(
	"tts_speed",
	snapshot => snapshot.voice.ttsSpeed,
	1,
	val => RUNTIME.updateVoice({ttsSpeed: String(val)}),
)
const ttsSpeed = ttsSpeedField.value

const ttsAutoPlayField = defineField(
	"tts_auto_play",
	snapshot => snapshot.voice.ttsAutoPlay,
	true,
	val => RUNTIME.updateVoice({ttsAutoPlay: val}),
)
const ttsAutoPlay = ttsAutoPlayField.value

const isSpeakingTest = ref(false)
const isSpeaking = computed(() => VOICE.value?.speaking ?? false)

// GPT-SoVITS 配置
const gptsovitsBaseUrlField = defineField(
	"gptsovits_url",
	snapshot => snapshot.voice.gptsovitsBaseUrl,
	"http://127.0.0.1:9880",
	val => RUNTIME.updateVoice({gptsovitsBaseUrl: val.trim()}),
)
const gptsovitsBaseUrl = gptsovitsBaseUrlField.value

const gptsovitsRefAudioField = defineField(
	"gptsovits_ref",
	snapshot => snapshot.voice.gptsovitsRefAudio,
	"",
	val => RUNTIME.updateVoice({gptsovitsRefAudio: val.trim()}),
)
const gptsovitsRefAudio = gptsovitsRefAudioField.value

const gptsovitsPromptTextField = defineField(
	"gptsovits_text",
	snapshot => snapshot.voice.gptsovitsPromptText,
	"",
	val => RUNTIME.updateVoice({gptsovitsPromptText: val.trim()}),
)
const gptsovitsPromptText = gptsovitsPromptTextField.value

const gptsovitsPromptLangField = defineField(
	"gptsovits_lang",
	snapshot => snapshot.voice.gptsovitsPromptLang,
	"zh",
	val => RUNTIME.updateVoice({gptsovitsPromptLang: val.trim()}),
)
const gptsovitsPromptLang = gptsovitsPromptLangField.value

// STT (仅 Whisper 云端识别)
const sttBaseUrlField = defineField(
	"stt_base_url",
	snapshot => snapshot.voice.sttBaseUrl,
	"",
	val => RUNTIME.updateVoice({sttBaseUrl: val.trim(), sttProvider: "whisper"}),
)
const sttBaseUrl = sttBaseUrlField.value

const sttApiKeyInput = ref("")

onMounted(async () => {
	await RUNTIME.init()
})

// 音量修改 (滑块连续拖动, 防抖提交)
const onVolumeChange = (value: number) => {
	volume.value = value
	volumeField.save()
}

const onTtsProviderChange = (value: TtsProvider) => {
	ttsProvider.value = value
	void ttsProviderField.saveNow()
}

const onTtsBaseUrlBlur = () => ttsBaseUrlField.save()
const onTtsVoiceBlur = () => ttsVoiceField.save()
const onGptBaseUrlBlur = () => gptsovitsBaseUrlField.save()
const onGptRefAudioBlur = () => gptsovitsRefAudioField.save()
const onGptPromptTextBlur = () => gptsovitsPromptTextField.save()
const onGptPromptLangBlur = () => gptsovitsPromptLangField.save()
const onSttBaseUrlBlur = () => sttBaseUrlField.save()

const onTtsApiKeyBlur = () => {
	const VALUE = ttsApiKeyInput.value.trim()
	ttsApiKeyInput.value = ""
	if (!VALUE) return
	save("tts_api_key", async () => {
		try {
			await RUNTIME.updateVoice({ttsApiKey: VALUE})
		} catch (error) {
			ttsApiKeyInput.value = VALUE
			throw error
		}
	})
}

const onSttApiKeyBlur = () => {
	const VALUE = sttApiKeyInput.value.trim()
	sttApiKeyInput.value = ""
	if (!VALUE) return
	save("stt_api_key", async () => {
		try {
			await RUNTIME.updateVoice({sttApiKey: VALUE})
		} catch (error) {
			sttApiKeyInput.value = VALUE
			throw error
		}
	})
}

const onTtsSpeedChange = (value: number) => {
	ttsSpeed.value = value
	ttsSpeedField.save()
}

const onTtsAutoPlayChange = (value: boolean) => {
	ttsAutoPlay.value = value
	void ttsAutoPlayField.saveNow()
}

// 关闭旧浏览器语音配置提示
const ackNotice = async () => {
	try {
		await RUNTIME.ackVoiceNotice()
	} catch (error) {
		feedback.error(I18N.value.notice.ackFailed, error)
	}
}

// 试听当前音色 (合成与播放全部在后端)
const testVoice = async () => {
	if (isSpeakingTest.value || isSpeaking.value) return
	isSpeakingTest.value = true
	try {
		await RUNTIME.ttsTest()
	} catch (error) {
		feedback.error(I18N.value.tts.testFailed, error)
	} finally {
		isSpeakingTest.value = false
	}
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader
			:title="I18N.title"
			:subtitle="I18N.subtitle"
		/>

		<!-- 旧浏览器语音配置一次性提示 -->
		<div
			v-if="VOICE?.noticePending"
			class="shrink-0 flex items-start gap-2.5 px-4 py-3 rounded-md bg-overlay-4 border border-warning/35"
			role="status"
		>
			<Icon name="alert" :size="16" class="shrink-0 mt-0.5 text-warning"/>
			<div class="flex-1 min-w-0 flex flex-col gap-1">
				<p class="text-base font-600 text-text-primary">{{ I18N.notice.title }}</p>
				<p class="text-xs text-text-muted leading-relaxed">
					{{ I18N.notice.desc }}
				</p>
			</div>
			<AppButton size="sm" @click="ackNotice">
				{{ I18N.notice.ack }}
			</AppButton>
		</div>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 全局音量 -->
			<AppCard :title="I18N.volume.title" icon="volume">
				<div class="flex items-center gap-3.5">
					<n-slider
						:value="volume"
						:min="0"
						:max="100"
						:format-tooltip="(v: number) => `${v}%`"
						class="flex-1"
						@update:value="onVolumeChange"
					/>
					<span class="w-[4.8rem] text-right text-sm font-600 text-nori-teal-bright mono">{{ volume }}%</span>
				</div>
			</AppCard>

			<!-- 2. TTS 语音合成 -->
			<AppCard :title="I18N.tts.title" icon="sparkles">
				<div class="field">
					<span class="field-label font-500">{{ I18N.tts.provider }}</span>
					<div class="flex flex-wrap gap-2">
						<label
							class="pill-choice focus-ring-within gap-1.5 px-3.5 py-1.5 text-xs"
							:class="ttsProvider === 'openai' ? 'pill-choice-on' : 'pill-choice-off'"
						>
							<input v-model="ttsProvider" type="radio" value="openai" class="sr-only"
								@change="onTtsProviderChange('openai')"/>
							{{ I18N.tts.providerOpenai }}
						</label>
						<label
							class="pill-choice focus-ring-within gap-1.5 px-3.5 py-1.5 text-xs"
							:class="ttsProvider === 'custom' ? 'pill-choice-on' : 'pill-choice-off'"
						>
							<input v-model="ttsProvider" type="radio" value="custom" class="sr-only"
								@change="onTtsProviderChange('custom')"/>
							{{ I18N.tts.providerCustom }}
						</label>
						<label
							class="pill-choice focus-ring-within gap-1.5 px-3.5 py-1.5 text-xs"
							:class="ttsProvider === 'gpt_sovits' ? 'pill-choice-on' : 'pill-choice-off'"
						>
							<input v-model="ttsProvider" type="radio" value="gpt_sovits" class="sr-only"
								@change="onTtsProviderChange('gpt_sovits')"/>
							{{ I18N.tts.providerGptSovits }}
						</label>
					</div>
				</div>

				<template v-if="ttsProvider === 'openai' || ttsProvider === 'custom'">
					<label class="field">
						<span class="field-label font-500">{{ I18N.tts.baseUrl }}</span>
						<input
							v-model="ttsBaseUrl"
							class="input-base"
							placeholder="https://api.openai.com/v1"
							@focus="ttsBaseUrlField.focus"
							@input="ttsBaseUrlField.touch"
							@blur="onTtsBaseUrlBlur"
						/>
					</label>

					<label class="field">
						<span class="field-label font-500">{{ I18N.tts.apiKey }} {{ VOICE?.hasTtsApiKey ? I18N.encrypted : "" }}</span>
						<input
							v-model="ttsApiKeyInput"
							type="password"
							class="input-base"
							placeholder="sk-..."
							@blur="onTtsApiKeyBlur"
						/>
					</label>
				</template>

				<template v-else-if="ttsProvider === 'gpt_sovits'">
					<label class="field">
						<span class="field-label font-500">{{ I18N.gptSovits.baseUrl }}</span>
						<input
							v-model="gptsovitsBaseUrl"
							class="input-base"
							placeholder="http://127.0.0.1:9880"
							@focus="gptsovitsBaseUrlField.focus"
							@input="gptsovitsBaseUrlField.touch"
							@blur="onGptBaseUrlBlur"
						/>
					</label>

					<label class="field">
						<span class="field-label font-500">{{ I18N.gptSovits.refAudio }}</span>
						<input
							v-model="gptsovitsRefAudio"
							class="input-base"
							placeholder="E:/GPT-SoVITS/reference.wav"
							@focus="gptsovitsRefAudioField.focus"
							@input="gptsovitsRefAudioField.touch"
							@blur="onGptRefAudioBlur"
						/>
					</label>

					<div class="flex gap-3">
						<label class="field flex-1">
							<span class="field-label font-500">{{ I18N.gptSovits.promptText }}</span>
							<input
								v-model="gptsovitsPromptText"
								class="input-base"
								:placeholder="I18N.gptSovits.promptTextPlaceholder"
								@focus="gptsovitsPromptTextField.focus"
								@input="gptsovitsPromptTextField.touch"
								@blur="onGptPromptTextBlur"
							/>
						</label>
						<label class="field w-[10rem] shrink-0">
							<span class="field-label font-500">{{ I18N.gptSovits.lang }}</span>
							<input
								v-model="gptsovitsPromptLang"
								class="input-base"
								placeholder="zh / ja / en"
								@focus="gptsovitsPromptLangField.focus"
								@input="gptsovitsPromptLangField.touch"
								@blur="onGptPromptLangBlur"
							/>
						</label>
					</div>
				</template>

				<div class="flex gap-3">
					<label class="field flex-1">
						<span class="field-label font-500">{{ I18N.tts.voice }}</span>
						<input
							v-model="ttsVoice"
							class="input-base"
							placeholder="nova, alloy, shimmer..."
							@focus="ttsVoiceField.focus"
							@input="ttsVoiceField.touch"
							@blur="onTtsVoiceBlur"
						/>
					</label>

					<div class="field flex-1">
						<span class="field-label font-500">{{ I18N.tts.speed }}: {{ ttsSpeed }}x</span>
						<n-slider
							:value="ttsSpeed"
							:min="0.5"
							:max="2.0"
							:step="0.1"
							:format-tooltip="(v: number) => `${v}x`"
							@update:value="onTtsSpeedChange"
						/>
					</div>
				</div>

				<div class="pt-2 border-t border-line-subtle">
					<AppSwitchRow
						:title="I18N.tts.autoPlay"
						:desc="I18N.tts.autoPlayDesc"
						:model-value="ttsAutoPlay"
						@update:model-value="onTtsAutoPlayChange"
					/>
				</div>

				<div class="flex gap-2 pt-1">
					<AppButton
						variant="primary"
						size="sm"
						:icon="isSpeakingTest || isSpeaking ? undefined : 'play'"
						:loading="isSpeakingTest || isSpeaking"
						:disabled="isSpeakingTest || isSpeaking"
						@click="testVoice"
					>
						{{ isSpeakingTest ? I18N.tts.testing : I18N.tts.test }}
					</AppButton>
				</div>
			</AppCard>

			<!-- 3. STT 语音识别 (Whisper 云端) -->
			<AppCard :title="I18N.stt.title" icon="mic">
				<p class="text-xs text-text-faint leading-relaxed">
					{{ I18N.stt.desc }}
				</p>

				<label class="field">
					<span class="field-label font-500">{{ I18N.stt.baseUrl }}</span>
					<input
						v-model="sttBaseUrl"
						class="input-base"
						placeholder="https://api.openai.com/v1"
						@focus="sttBaseUrlField.focus"
						@input="sttBaseUrlField.touch"
						@blur="onSttBaseUrlBlur"
					/>
				</label>

				<label class="field">
					<span class="field-label font-500">{{ I18N.stt.apiKey }} {{ VOICE?.hasSttApiKey ? I18N.encrypted : "" }}</span>
					<input
						v-model="sttApiKeyInput"
						type="password"
						class="input-base"
						placeholder="sk-..."
						@blur="onSttApiKeyBlur"
					/>
				</label>
			</AppCard>
		</div>
	</div>
</template>
