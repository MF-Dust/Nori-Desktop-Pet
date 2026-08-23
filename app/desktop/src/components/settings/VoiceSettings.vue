<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"
import useLanguages from "../../services/i18n/useLanguages"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import Icon from "../Icon.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppCard from "../ui/AppCard.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const I18N = computed(() => useLanguages().views.main.voice)

const SNAPSHOT = computed(() => RUNTIME.snapshot.value)
const VOICE = computed(() => SNAPSHOT.value?.voice)

// 字段级防抖保存 (每字段独立计时器, 卸载时自动 flush, 失败走反馈层)
const SAVE = useDebouncedSave({onError: (_key, error) => feedback.error(I18N.value.saveFailed, error)})

// 全局音量 (0 ~ 100)
const volume = ref(100)

// TTS 配置 (云端路径: openai / custom / gpt_sovits)
type TtsProvider = "openai" | "custom" | "gpt_sovits"
const ttsProvider = ref<TtsProvider>("openai")
const ttsBaseUrl = ref("")
const ttsApiKeyInput = ref("")
const ttsVoice = ref("nova")
const ttsSpeed = ref(1.0)
const ttsAutoPlay = ref(true)
const isSpeakingTest = ref(false)

// GPT-SoVITS 配置
const gptsovitsBaseUrl = ref("http://127.0.0.1:9880")
const gptsovitsRefAudio = ref("")
const gptsovitsPromptText = ref("")
const gptsovitsPromptLang = ref("zh")

// STT (仅 Whisper 云端识别)
const sttBaseUrl = ref("")
const sttApiKeyInput = ref("")

let synced = false
onMounted(async () => {
	await RUNTIME.init()
	syncFromSnapshot()
})

const syncFromSnapshot = () => {
	const V = VOICE.value
	if (!V || synced) return
	synced = true
	volume.value = Math.round(V.volume * 100)
	if (["openai", "custom", "gpt_sovits"].includes(V.ttsProvider)) {
		ttsProvider.value = V.ttsProvider as TtsProvider
	}
	ttsBaseUrl.value = V.ttsBaseUrl
	ttsVoice.value = V.ttsVoice || "nova"
	ttsSpeed.value = V.ttsSpeed
	ttsAutoPlay.value = V.ttsAutoPlay
	gptsovitsBaseUrl.value = V.gptsovitsBaseUrl
	gptsovitsRefAudio.value = V.gptsovitsRefAudio
	gptsovitsPromptText.value = V.gptsovitsPromptText
	gptsovitsPromptLang.value = V.gptsovitsPromptLang
	sttBaseUrl.value = V.sttBaseUrl
}

// 音量修改 (滑块连续拖动, 防抖提交)
const onVolumeChange = (value: number) => {
	volume.value = value
	SAVE.save("volume", () => RUNTIME.updateVoice({volume: String(value / 100)}))
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
	if (isSpeakingTest.value) return
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
			class="shrink-0 flex items-start gap-2.5 px-4 py-3 rounded-md bg-white/4 border border-warning/35"
			role="status"
		>
			<Icon name="alert" :size="16" class="shrink-0 mt-0.5 text-warning"/>
			<div class="flex-1 min-w-0 flex flex-col gap-1">
				<p class="text-base font-600 text-text-primary">{{ I18N.notice.title }}</p>
				<p class="text-xs text-text-muted leading-relaxed">
					{{ I18N.notice.desc }}
				</p>
			</div>
			<n-button size="small" @click="ackNotice">
				{{ I18N.notice.ack }}
			</n-button>
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
							class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-pill border text-xs cursor-pointer
								transition-all duration-200 focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
							:class="ttsProvider === 'openai'
								? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
								: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-white/6 border-nori-teal-soft)'"
						>
							<input v-model="ttsProvider" type="radio" value="openai" class="sr-only"
								@change="SAVE.saveNow('ttsProvider', () => RUNTIME.updateVoice({ttsProvider: 'openai'}))"/>
							{{ I18N.tts.providerOpenai }}
						</label>
						<label
							class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-pill border text-xs cursor-pointer
								transition-all duration-200 focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
							:class="ttsProvider === 'custom'
								? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
								: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-white/6 border-nori-teal-soft)'"
						>
							<input v-model="ttsProvider" type="radio" value="custom" class="sr-only"
								@change="SAVE.saveNow('ttsProvider', () => RUNTIME.updateVoice({ttsProvider: 'custom'}))"/>
							{{ I18N.tts.providerCustom }}
						</label>
						<label
							class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-pill border text-xs cursor-pointer
								transition-all duration-200 focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
							:class="ttsProvider === 'gpt_sovits'
								? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
								: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-white/6 border-nori-teal-soft)'"
						>
							<input v-model="ttsProvider" type="radio" value="gpt_sovits" class="sr-only"
								@change="SAVE.saveNow('ttsProvider', () => RUNTIME.updateVoice({ttsProvider: 'gpt_sovits'}))"/>
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
							@blur="SAVE.save('tts_base_url', () => RUNTIME.updateVoice({ttsBaseUrl: ttsBaseUrl.trim()}))"
						/>
					</label>

					<label class="field">
						<span class="field-label font-500">{{ I18N.tts.apiKey }} {{ VOICE?.hasTtsApiKey ? I18N.encrypted : "" }}</span>
						<input
							v-model="ttsApiKeyInput"
							type="password"
							class="input-base"
							placeholder="sk-..."
							@blur="() => {
								const VALUE = ttsApiKeyInput.trim()
								ttsApiKeyInput = ''
								if (VALUE) SAVE.save('tts_api_key', () => RUNTIME.updateVoice({ttsApiKey: VALUE}))
							}"
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
							@blur="SAVE.save('gptsovits_url', () => RUNTIME.updateVoice({gptsovitsBaseUrl: gptsovitsBaseUrl.trim()}))"
						/>
					</label>

					<label class="field">
						<span class="field-label font-500">{{ I18N.gptSovits.refAudio }}</span>
						<input
							v-model="gptsovitsRefAudio"
							class="input-base"
							placeholder="E:/GPT-SoVITS/reference.wav"
							@blur="SAVE.save('gptsovits_ref', () => RUNTIME.updateVoice({gptsovitsRefAudio: gptsovitsRefAudio.trim()}))"
						/>
					</label>

					<div class="flex gap-3">
						<label class="field flex-1">
							<span class="field-label font-500">{{ I18N.gptSovits.promptText }}</span>
							<input
								v-model="gptsovitsPromptText"
								class="input-base"
								:placeholder="I18N.gptSovits.promptTextPlaceholder"
								@blur="SAVE.save('gptsovits_text', () => RUNTIME.updateVoice({gptsovitsPromptText: gptsovitsPromptText.trim()}))"
							/>
						</label>
						<label class="field w-[10rem] shrink-0">
							<span class="field-label font-500">{{ I18N.gptSovits.lang }}</span>
							<input
								v-model="gptsovitsPromptLang"
								class="input-base"
								placeholder="zh / ja / en"
								@blur="SAVE.save('gptsovits_lang', () => RUNTIME.updateVoice({gptsovitsPromptLang: gptsovitsPromptLang.trim()}))"
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
							@blur="SAVE.save('tts_voice', () => RUNTIME.updateVoice({ttsVoice: ttsVoice.trim()}))"
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
							@update:value="(v: number) => {
								ttsSpeed = v
								SAVE.save('tts_speed', () => RUNTIME.updateVoice({ttsSpeed: String(v)}))
							}"
						/>
					</div>
				</div>

				<div class="pt-2 border-t border-line-subtle">
					<AppSwitchRow :title="I18N.tts.autoPlay" :desc="I18N.tts.autoPlayDesc">
						<n-switch
							:value="ttsAutoPlay"
							@update:value="(v: boolean) => {
								ttsAutoPlay = v
								void SAVE.saveNow('tts_auto_play', () => RUNTIME.updateVoice({ttsAutoPlay: v}))
							}"
						/>
					</AppSwitchRow>
				</div>

				<div class="flex gap-2 pt-1">
					<n-button
						type="primary"
						:loading="isSpeakingTest"
						:disabled="isSpeakingTest"
						@click="testVoice"
					>
						<template #icon>
							<Icon :name="isSpeakingTest ? 'loading' : 'play'" :size="15"/>
						</template>
						{{ isSpeakingTest ? I18N.tts.testing : I18N.tts.test }}
					</n-button>
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
						@blur="SAVE.save('stt_base_url', () => RUNTIME.updateVoice({sttBaseUrl: sttBaseUrl.trim(), sttProvider: 'whisper'}))"
					/>
				</label>

				<label class="field">
					<span class="field-label font-500">{{ I18N.stt.apiKey }} {{ VOICE?.hasSttApiKey ? I18N.encrypted : "" }}</span>
					<input
						v-model="sttApiKeyInput"
						type="password"
						class="input-base"
						placeholder="sk-..."
						@blur="() => {
							const VALUE = sttApiKeyInput.trim()
							sttApiKeyInput = ''
							if (VALUE) SAVE.save('stt_api_key', () => RUNTIME.updateVoice({sttApiKey: VALUE}))
						}"
					/>
				</label>
			</AppCard>
		</div>
	</div>
</template>
