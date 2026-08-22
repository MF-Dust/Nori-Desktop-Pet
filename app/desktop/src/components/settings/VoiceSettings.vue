<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import Icon from "../Icon.vue"

const SNAPSHOT = computed(() => RUNTIME.snapshot.value)
const VOICE = computed(() => SNAPSHOT.value?.voice)

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

// 保存辅助: 每个 key 独立防抖 timer
const timers = new Map<string, ReturnType<typeof setTimeout>>()
const saveDebounced = (key: string, value: () => Record<string, unknown>) => {
	clearTimeout(timers.get(key))
	timers.set(key, setTimeout(() => {
		timers.delete(key)
		void RUNTIME.updateVoice(value()).catch(error => console.error(`保存语音配置失败 (${key}):`, error))
	}, 400))
}

// 音量修改 (立即提交, 滑块松手即生效)
const onVolumeChange = (value: number) => {
	volume.value = value
	saveDebounced("volume", () => ({volume: String(value / 100)}))
}

// 试听当前音色 (合成与播放全部在后端)
const testVoice = async () => {
	if (isSpeakingTest.value) return
	isSpeakingTest.value = true
	try {
		await RUNTIME.ttsTest()
	} catch (error) {
		console.error("试听失败:", error)
	} finally {
		isSpeakingTest.value = false
	}
}
</script>

<template>
	<div class="voice-settings">
		<header class="section-header">
			<h2 class="title glow-teal">语音与音效设置</h2>
			<p class="subtitle">配置桌宠语音合成 (TTS)、语音识别 (STT) 与全局输出音量</p>
		</header>

		<!-- 旧浏览器语音配置一次性提示 -->
		<div v-if="VOICE?.noticePending" class="notice-card">
			<Icon name="alert" :size="16" class="notice-icon"/>
			<div class="notice-body">
				<p class="notice-title">检测到旧版浏览器语音配置</p>
				<p class="notice-desc">
					纯后端版本不再支持 Web Speech / 浏览器 Edge-TTS。请改用 OpenAI / 自定义 HTTP / GPT-SoVITS 云端语音；原配置已保留，不会被删除。
				</p>
			</div>
			<n-button size="small" @click="RUNTIME.ackVoiceNotice()">
				知道了
			</n-button>
		</div>

		<div class="settings-content">
			<!-- 1. 全局音量 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="volume" :size="18" class="card-icon"/>
					<span class="card-title">全局输出音量</span>
				</div>
				<div class="card-body">
					<div class="slider-row">
						<n-slider
							:value="volume"
							:min="0"
							:max="100"
							:format-tooltip="(v: number) => `${v}%`"
							class="volume-slider"
							@update:value="onVolumeChange"
						/>
						<span class="slider-value">{{ volume }}%</span>
					</div>
				</div>
			</div>

			<!-- 2. TTS 语音合成 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="sparkles" :size="18" class="card-icon"/>
					<span class="card-title">TTS 语音合成服务</span>
				</div>
				<div class="card-body">
					<div class="form-item">
						<label class="label">服务提供商</label>
						<div class="radio-group">
							<label class="radio-chip" :class="{active: ttsProvider === 'openai'}">
								<input v-model="ttsProvider" type="radio" value="openai"
									@change="saveDebounced('ttsProvider', () => ({ttsProvider: 'openai'}))"/>
								OpenAI / 兼容接口
							</label>
							<label class="radio-chip" :class="{active: ttsProvider === 'custom'}">
								<input v-model="ttsProvider" type="radio" value="custom"
									@change="saveDebounced('ttsProvider', () => ({ttsProvider: 'custom'}))"/>
								自定义 HTTP 端点
							</label>
							<label class="radio-chip" :class="{active: ttsProvider === 'gpt_sovits'}">
								<input v-model="ttsProvider" type="radio" value="gpt_sovits"
									@change="saveDebounced('ttsProvider', () => ({ttsProvider: 'gpt_sovits'}))"/>
								GPT-SoVITS API
							</label>
						</div>
					</div>

					<template v-if="ttsProvider === 'openai' || ttsProvider === 'custom'">
						<div class="form-item">
							<label class="label">TTS API 地址</label>
							<input
								v-model="ttsBaseUrl"
								class="input"
								placeholder="https://api.openai.com/v1"
								@blur="saveDebounced('tts_base_url', () => ({ttsBaseUrl: ttsBaseUrl.trim()}))"
							/>
						</div>

						<div class="form-item">
							<label class="label">TTS API Key {{ VOICE?.hasTtsApiKey ? "(已加密保存)" : "" }}</label>
							<input
								v-model="ttsApiKeyInput"
								type="password"
								class="input"
								placeholder="sk-..."
								@blur="() => {
									const VALUE = ttsApiKeyInput.trim()
									ttsApiKeyInput = ''
									if (VALUE) saveDebounced('tts_api_key', () => ({ttsApiKey: VALUE}))
								}"
							/>
						</div>
					</template>

					<template v-else-if="ttsProvider === 'gpt_sovits'">
						<div class="form-item">
							<label class="label">GPT-SoVITS API 地址</label>
							<input
								v-model="gptsovitsBaseUrl"
								class="input"
								placeholder="http://127.0.0.1:9880"
								@blur="saveDebounced('gptsovits_url', () => ({gptsovitsBaseUrl: gptsovitsBaseUrl.trim()}))"
							/>
						</div>

						<div class="form-item">
							<label class="label">参考音频路径 (Ref Audio)</label>
							<input
								v-model="gptsovitsRefAudio"
								class="input"
								placeholder="E:/GPT-SoVITS/reference.wav"
								@blur="saveDebounced('gptsovits_ref', () => ({gptsovitsRefAudio: gptsovitsRefAudio.trim()}))"
							/>
						</div>

						<div class="form-row">
							<div class="form-item flex-1">
								<label class="label">参考音频文本 (Prompt Text)</label>
								<input
									v-model="gptsovitsPromptText"
									class="input"
									placeholder="参考音频中所说的文字内容"
									@blur="saveDebounced('gptsovits_text', () => ({gptsovitsPromptText: gptsovitsPromptText.trim()}))"
								/>
							</div>
							<div class="form-item w-80">
								<label class="label">语言</label>
								<input
									v-model="gptsovitsPromptLang"
									class="input"
									placeholder="zh / ja / en"
									@blur="saveDebounced('gptsovits_lang', () => ({gptsovitsPromptLang: gptsovitsPromptLang.trim()}))"
								/>
							</div>
						</div>
					</template>

					<div class="form-row">
						<div class="form-item flex-1">
							<label class="label">朗读音色 (Voice)</label>
							<input
								v-model="ttsVoice"
								class="input"
								placeholder="nova, alloy, shimmer..."
								@blur="saveDebounced('tts_voice', () => ({ttsVoice: ttsVoice.trim()}))"
							/>
						</div>

						<div class="form-item flex-1">
							<label class="label">语速: {{ ttsSpeed }}x</label>
							<n-slider
								:value="ttsSpeed"
								:min="0.5"
								:max="2.0"
								:step="0.1"
								:format-tooltip="(v: number) => `${v}x`"
								class="speed-slider"
								@update:value="(v: number) => {
									ttsSpeed = v
									saveDebounced('tts_speed', () => ({ttsSpeed: String(v)}))
								}"
							/>
						</div>
					</div>

					<div class="switch-row">
						<div>
							<span class="switch-title">对话自动朗读</span>
							<p class="switch-desc">当桌宠生成回复消息时，自动进行语音朗读播放</p>
						</div>
						<n-switch
							:value="ttsAutoPlay"
							@update:value="(v: boolean) => {
								ttsAutoPlay = v
								saveDebounced('tts_auto_play', () => ({ttsAutoPlay: v}))
							}"
						/>
					</div>

					<div class="action-row">
						<n-button
							type="primary"
							:loading="isSpeakingTest"
							:disabled="isSpeakingTest"
							@click="testVoice"
						>
							<template #icon>
								<Icon :name="isSpeakingTest ? 'loading' : 'play'" :size="15"/>
							</template>
							{{ isSpeakingTest ? "正在试听..." : "试听当前音色" }}
						</n-button>
					</div>
				</div>
			</div>

			<!-- 3. STT 语音识别 (Whisper 云端) -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="mic" :size="18" class="card-icon"/>
					<span class="card-title">STT 语音识别服务 (Whisper)</span>
				</div>
				<div class="card-body">
					<p class="hint-line">
						录音在本地完成后上传至 OpenAI 兼容接口识别; 旧的浏览器听写已停用。
					</p>

					<div class="form-item">
						<label class="label">Whisper API 地址</label>
						<input
							v-model="sttBaseUrl"
							class="input"
							placeholder="https://api.openai.com/v1"
							@blur="saveDebounced('stt_base_url', () => ({sttBaseUrl: sttBaseUrl.trim(), sttProvider: 'whisper'}))"
						/>
					</div>

					<div class="form-item">
						<label class="label">Whisper API Key {{ VOICE?.hasSttApiKey ? "(已加密保存)" : "" }}</label>
						<input
							v-model="sttApiKeyInput"
							type="password"
							class="input"
							placeholder="sk-..."
							@blur="() => {
								const VALUE = sttApiKeyInput.trim()
								sttApiKeyInput = ''
								if (VALUE) saveDebounced('stt_api_key', () => ({sttApiKey: VALUE}))
							}"
						/>
					</div>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.voice-settings {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
	padding: 1.6rem 2.4rem;
	gap: 1.6rem;
}

.section-header {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.title {
	margin: 0;
	font-size: 1.8rem;
	font-weight: 700;
	color: var(--text-primary);
}

.subtitle {
	margin: 0;
	font-size: 1.2rem;
	color: var(--text-faint);
}

.notice-card {
	display: flex;
	align-items: flex-start;
	gap: 1rem;
	padding: 1.2rem 1.6rem;
	background: rgba(255, 180, 50, 0.08);
	border: 0.1rem solid rgba(255, 180, 50, 0.35);
	border-radius: var(--radius-md);
}

.notice-icon {
	color: #ffb432;
	margin-top: 0.2rem;
	flex-shrink: 0;
}

.notice-body {
	flex: 1;

	.notice-title {
		margin: 0 0 0.3rem;
		font-size: 1.25rem;
		font-weight: 600;
		color: var(--text-primary);
	}

	.notice-desc {
		margin: 0;
		font-size: 1.15rem;
		color: var(--text-muted);
		line-height: 1.5;
	}
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	transition: all 0.2s ease;

	&:hover {
		border-color: var(--line-strong);
	}
}

.card-header {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	color: var(--nori-teal-bright);
}

.card-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-body {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.slider-row {
	display: flex;
	align-items: center;
	gap: 1.4rem;
}

.slider-value {
	width: 4.8rem;
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
	font-family: monospace;
	font-weight: 600;
	text-align: right;
}

.form-item {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.form-row {
	display: flex;
	gap: 1.2rem;
}

.flex-1 {
	flex: 1;
}

.w-80 {
	width: 10rem;
	flex-shrink: 0;
}

.label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-muted);
}

.hint-line {
	margin: 0;
	font-size: 1.15rem;
	color: var(--text-faint);
	line-height: 1.5;
}

.input {
	padding: 0.9rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.25rem;
	font-family: inherit;
	outline: none;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:focus {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.radio-group {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
}

.radio-chip {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.65rem 1.3rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-body);
	font-size: 1.15rem;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	input {
		display: none;
	}

	&:hover {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: transparent;
		background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		color: #03101c;
		font-weight: 600;
		box-shadow: 0 0.2rem 1.2rem var(--glow-teal-soft);
	}
}

.switch-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.8rem 0;
	border-top: 0.1rem solid var(--line-subtle);
}

.switch-title {
	font-size: 1.25rem;
	color: var(--text-primary);
	font-weight: 500;
}

.switch-desc {
	margin: 0.2rem 0 0;
	font-size: 1.1rem;
	color: var(--text-faint);
}

.action-row {
	display: flex;
	gap: 0.8rem;
	padding-top: 0.4rem;
}
</style>
