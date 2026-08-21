<script setup lang="ts">
import {onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import {audioService} from "../../services/audio"
import {ttsService} from "../../services/tts"
import Icon from "../Icon.vue"

// 全局音量 (0 ~ 100)
const volume = ref(Math.round(audioService.getVolume() * 100))

// TTS 配置
const ttsProvider = ref<"web_speech" | "openai" | "custom" | "gpt_sovits" | "edge_tts">("web_speech")
const ttsBaseUrl = ref("")
const ttsApiKey = ref("")
const ttsVoice = ref("nova")
const ttsSpeed = ref(1.0)
const ttsAutoPlay = ref(true)
const isSpeakingTest = ref(false)

// GPT-SoVITS 配置
const gptsovitsBaseUrl = ref("http://127.0.0.1:9880")
const gptsovitsRefAudio = ref("")
const gptsovitsPromptText = ref("")
const gptsovitsPromptLang = ref("zh")

// STT 配置
const sttProvider = ref<"web_speech" | "whisper">("web_speech")
const sttBaseUrl = ref("")
const sttApiKey = ref("")

// 初始加载配置
onMounted(async () => {
	try {
		const [
			SAVED_VOL,
			SAVED_TTS_P,
			SAVED_TTS_URL,
			SAVED_TTS_KEY,
			SAVED_TTS_VOICE,
			SAVED_TTS_SPEED,
			SAVED_TTS_AUTO,
			SAVED_SOVITS_URL,
			SAVED_SOVITS_REF,
			SAVED_SOVITS_TXT,
			SAVED_SOVITS_LANG,
			SAVED_STT_P,
			SAVED_STT_URL,
			SAVED_STT_KEY,
		] = await Promise.all([
			invoke<string | null>("get_config", {key: "audio_volume"}),
			invoke<string | null>("get_config", {key: "tts_provider"}),
			invoke<string | null>("get_config", {key: "tts_base_url"}),
			invoke<string | null>("get_config", {key: "tts_api_key"}),
			invoke<string | null>("get_config", {key: "tts_voice"}),
			invoke<string | null>("get_config", {key: "tts_speed"}),
			invoke<string | null>("get_config", {key: "tts_auto_play"}),
			invoke<string | null>("get_config", {key: "gptsovits_base_url"}),
			invoke<string | null>("get_config", {key: "gptsovits_ref_audio"}),
			invoke<string | null>("get_config", {key: "gptsovits_prompt_text"}),
			invoke<string | null>("get_config", {key: "gptsovits_prompt_lang"}),
			invoke<string | null>("get_config", {key: "stt_provider"}),
			invoke<string | null>("get_config", {key: "stt_base_url"}),
			invoke<string | null>("get_config", {key: "stt_api_key"}),
		])

		if (SAVED_VOL !== null) {
			const NUM = parseFloat(SAVED_VOL)
			if (!Number.isNaN(NUM)) volume.value = Math.round(NUM * 100)
		}
		if (SAVED_TTS_P) ttsProvider.value = SAVED_TTS_P as any
		if (SAVED_TTS_URL) ttsBaseUrl.value = SAVED_TTS_URL
		if (SAVED_TTS_KEY) ttsApiKey.value = SAVED_TTS_KEY
		if (SAVED_TTS_VOICE) ttsVoice.value = SAVED_TTS_VOICE
		if (SAVED_TTS_SPEED) ttsSpeed.value = parseFloat(SAVED_TTS_SPEED) || 1.0
		if (SAVED_TTS_AUTO !== null) ttsAutoPlay.value = SAVED_TTS_AUTO === "true" || SAVED_TTS_AUTO === "1"
		if (SAVED_SOVITS_URL) gptsovitsBaseUrl.value = SAVED_SOVITS_URL
		if (SAVED_SOVITS_REF) gptsovitsRefAudio.value = SAVED_SOVITS_REF
		if (SAVED_SOVITS_TXT) gptsovitsPromptText.value = SAVED_SOVITS_TXT
		if (SAVED_SOVITS_LANG) gptsovitsPromptLang.value = SAVED_SOVITS_LANG
		if (SAVED_STT_P) sttProvider.value = SAVED_STT_P as any
		if (SAVED_STT_URL) sttBaseUrl.value = SAVED_STT_URL
		if (SAVED_STT_KEY) sttApiKey.value = SAVED_STT_KEY
	} catch (error) {
		console.error("加载声音配置失败:", error)
	}
})

// 音量修改
const onVolumeChange = () => {
	const VAL = volume.value / 100
	audioService.setVolume(VAL)
}

// 保存配置项辅助
const saveConfig = (key: string, value: string) => {
	void invoke("set_config", {key, value})
}

// 试听语音
const testVoice = async () => {
	if (isSpeakingTest.value) return
	isSpeakingTest.value = true
	try {
		await ttsService.speak("主人好呀！我是 Nori，这是一条声音播放测试~", {
			voice: ttsVoice.value,
			speed: ttsSpeed.value,
		})
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
							v-model:value="volume"
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
							<label class="radio-chip" :class="{active: ttsProvider === 'web_speech'}">
								<input
									v-model="ttsProvider"
									type="radio"
									value="web_speech"
									@change="saveConfig('tts_provider', 'web_speech')"
								/>
								浏览器内置 (Web Speech)
							</label>
							<label class="radio-chip" :class="{active: ttsProvider === 'openai'}">
								<input
									v-model="ttsProvider"
									type="radio"
									value="openai"
									@change="saveConfig('tts_provider', 'openai')"
								/>
								OpenAI / 兼容接口
							</label>
							<label class="radio-chip" :class="{active: ttsProvider === 'custom'}">
								<input
									v-model="ttsProvider"
									type="radio"
									value="custom"
									@change="saveConfig('tts_provider', 'custom')"
								/>
								自定义 HTTP 端点
							</label>
							<label class="radio-chip" :class="{active: ttsProvider === 'gpt_sovits'}">
								<input
									v-model="ttsProvider"
									type="radio"
									value="gpt_sovits"
									@change="saveConfig('tts_provider', 'gpt_sovits')"
								/>
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
								@blur="saveConfig('tts_base_url', ttsBaseUrl)"
							/>
						</div>

						<div class="form-item">
							<label class="label">TTS API Key</label>
							<input
								v-model="ttsApiKey"
								type="password"
								class="input"
								placeholder="sk-..."
								@blur="saveConfig('tts_api_key', ttsApiKey)"
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
								@blur="saveConfig('gptsovits_base_url', gptsovitsBaseUrl)"
							/>
						</div>

						<div class="form-item">
							<label class="label">参考音频路径 (Ref Audio)</label>
							<input
								v-model="gptsovitsRefAudio"
								class="input"
								placeholder="E:/GPT-SoVITS/reference.wav"
								@blur="saveConfig('gptsovits_ref_audio', gptsovitsRefAudio)"
							/>
						</div>

						<div class="form-row">
							<div class="form-item flex-1">
								<label class="label">参考音频文本 (Prompt Text)</label>
								<input
									v-model="gptsovitsPromptText"
									class="input"
									placeholder="参考音频中所说的文字内容"
									@blur="saveConfig('gptsovits_prompt_text', gptsovitsPromptText)"
								/>
							</div>
							<div class="form-item w-80">
								<label class="label">语言</label>
								<input
									v-model="gptsovitsPromptLang"
									class="input"
									placeholder="zh / ja / en"
									@blur="saveConfig('gptsovits_prompt_lang', gptsovitsPromptLang)"
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
								@blur="saveConfig('tts_voice', ttsVoice)"
							/>
						</div>

						<div class="form-item flex-1">
							<label class="label">语速: {{ ttsSpeed }}x</label>
							<n-slider
								v-model:value="ttsSpeed"
								:min="0.5"
								:max="2.0"
								:step="0.1"
								:format-tooltip="(v: number) => `${v}x`"
								class="speed-slider"
								@update:value="(v: number) => saveConfig('tts_speed', String(v))"
							/>
						</div>
					</div>

					<div class="switch-row">
						<div>
							<span class="switch-title">对话自动朗读</span>
							<p class="switch-desc">当桌宠生成回复消息时，自动进行语音朗读播放</p>
						</div>
						<n-switch
							v-model:value="ttsAutoPlay"
							@update:value="(v: boolean) => saveConfig('tts_auto_play', String(v))"
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

			<!-- 3. STT 语音识别 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="mic" :size="18" class="card-icon"/>
					<span class="card-title">STT 语音识别服务</span>
				</div>
				<div class="card-body">
					<div class="form-item">
						<label class="label">识别方式</label>
						<div class="radio-group">
							<label class="radio-chip" :class="{active: sttProvider === 'web_speech'}">
								<input
									v-model="sttProvider"
									type="radio"
									value="web_speech"
									@change="saveConfig('stt_provider', 'web_speech')"
								/>
								浏览器原生听写 (Web Speech)
							</label>
							<label class="radio-chip" :class="{active: sttProvider === 'whisper'}">
								<input
									v-model="sttProvider"
									type="radio"
									value="whisper"
									@change="saveConfig('stt_provider', 'whisper')"
								/>
								OpenAI Whisper 录音识别
							</label>
						</div>
					</div>

					<template v-if="sttProvider === 'whisper'">
						<div class="form-item">
							<label class="label">Whisper API 地址</label>
							<input
								v-model="sttBaseUrl"
								class="input"
								placeholder="https://api.openai.com/v1"
								@blur="saveConfig('stt_base_url', sttBaseUrl)"
							/>
						</div>

						<div class="form-item">
							<label class="label">Whisper API Key</label>
							<input
								v-model="sttApiKey"
								type="password"
								class="input"
								placeholder="sk-..."
								@blur="saveConfig('stt_api_key', sttApiKey)"
							/>
						</div>
					</template>
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

.range-slider {
	flex: 1;
	height: 0.6rem;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
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

.label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-muted);
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

.toggle-switch {
	position: relative;
	width: 4.2rem;
	height: 2.4rem;
	cursor: pointer;

	input {
		opacity: 0;
		width: 0;
		height: 0;
	}

	.toggle-slider {
		position: absolute;
		inset: 0;
		background: rgba(255, 255, 255, 0.12);
		border-radius: var(--radius-pill);
		transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);

		&::before {
			position: absolute;
			content: "";
			height: 1.8rem;
			width: 1.8rem;
			left: 0.3rem;
			bottom: 0.3rem;
			background: white;
			border-radius: 50%;
			transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
		}
	}

	input:checked + .toggle-slider {
		background: var(--nori-teal);
		box-shadow: 0 0 1rem var(--glow-teal);
	}

	input:checked + .toggle-slider::before {
		transform: translateX(1.8rem);
		background: #03101c;
	}
}

.action-row {
	display: flex;
	gap: 0.8rem;
	padding-top: 0.4rem;
}

.btn-secondary {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.75rem 1.6rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	color: var(--nori-teal-bright);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover:not(:disabled) {
		background: rgba(125, 227, 255, 0.18);
		box-shadow: 0 0 1rem var(--glow-teal-soft);
	}

	&:disabled {
		opacity: 0.6;
		cursor: default;
	}
}
</style>
