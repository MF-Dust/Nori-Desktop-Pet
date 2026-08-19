<script setup lang="ts">
import {onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import {audioService} from "../../services/audio"
import {ttsService} from "../../services/tts"
import Icon from "../Icon.vue"

// 全局音量 (0 ~ 100)
const volume = ref(Math.round(audioService.getVolume() * 100))

// TTS 配置
const ttsProvider = ref<"web_speech" | "openai" | "custom">("web_speech")
const ttsBaseUrl = ref("")
const ttsApiKey = ref("")
const ttsVoice = ref("nova")
const ttsSpeed = ref(1.0)
const ttsAutoPlay = ref(true)
const isSpeakingTest = ref(false)

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
						<input
							v-model.number="volume"
							type="range"
							min="0"
							max="100"
							class="range-slider"
							@input="onVolumeChange"
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
						</div>
					</div>

					<template v-if="ttsProvider !== 'web_speech'">
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
							<input
								v-model.number="ttsSpeed"
								type="range"
								min="0.5"
								max="2.0"
								step="0.1"
								class="range-slider"
								@change="saveConfig('tts_speed', String(ttsSpeed))"
							/>
						</div>
					</div>

					<div class="switch-row">
						<div>
							<span class="switch-title">对话自动朗读</span>
							<p class="switch-desc">当桌宠生成回复消息时，自动进行语音朗读播放</p>
						</div>
						<label class="toggle-switch">
							<input
								v-model="ttsAutoPlay"
								type="checkbox"
								@change="saveConfig('tts_auto_play', String(ttsAutoPlay))"
							/>
							<span class="toggle-slider"/>
						</label>
					</div>

					<div class="action-row">
						<button class="btn-secondary" :disabled="isSpeakingTest" @click="testVoice">
							<Icon :name="isSpeakingTest ? 'loading' : 'play'" :size="15"/>
							<span>{{ isSpeakingTest ? "正在试听..." : "试听当前音色" }}</span>
						</button>
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
	padding: 1.5rem 2rem;
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
	color: var(--text-muted);
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.6rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.4rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
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
	gap: 1.2rem;
}

.range-slider {
	flex: 1;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.slider-value {
	min-width: 4rem;
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
	font-weight: 600;
	font-variant-numeric: tabular-nums;
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
	font-size: 1.15rem;
	color: var(--text-muted);
}

.input {
	padding: 0.8rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.25rem;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
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
	padding: 0.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: 2rem;
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-body);
	font-size: 1.15rem;
	cursor: pointer;
	transition: all 0.15s ease;

	input {
		display: none;
	}

	&.active {
		border-color: transparent;
		background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
		color: #05121a;
		font-weight: 600;
	}
}

.switch-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.8rem 0;
	border-top: 0.1rem solid rgba(255, 255, 255, 0.05);
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
	width: 4rem;
	height: 2.2rem;
	cursor: pointer;

	input {
		opacity: 0;
		width: 0;
		height: 0;
	}

	.toggle-slider {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: rgba(255, 255, 255, 0.15);
		border-radius: 2rem;
		transition: 0.2s;

		&::before {
			position: absolute;
			content: "";
			height: 1.6rem;
			width: 1.6rem;
			left: 0.3rem;
			bottom: 0.3rem;
			background: white;
			border-radius: 50%;
			transition: 0.2s;
		}
	}

	input:checked + .toggle-slider {
		background: var(--nori-teal-bright);
	}

	input:checked + .toggle-slider::before {
		transform: translateX(1.8rem);
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
	padding: 0.7rem 1.4rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	color: var(--nori-teal-bright);
	font-size: 1.2rem;
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
