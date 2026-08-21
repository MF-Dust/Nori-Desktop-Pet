<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {invoke} from "../../services/host/invoke"
import Icon from "../Icon.vue"

const I18N = computed(() => useLanguages().views.main.ai)

// 配置键名
const KEY_PROVIDER = "llm_provider"
const KEY_BASE = "llm_api_base"
const KEY_APIKEY = "llm_api_key"
const KEY_MODEL = "llm_model"

// 默认 Base URL 表
const DEFAULT_BASE_URLS: Record<string, string> = {
	openai: "https://api.openai.com/v1",
	openai_responses: "https://api.openai.com/v1",
	anthropic: "https://api.anthropic.com/v1",
	google: "https://generativelanguage.googleapis.com/v1beta",
}

// 协议列表定义
type ProviderKey = "openai" | "openai_responses" | "anthropic" | "google"
const PROVIDER_OPTIONS: ProviderKey[] = ["openai", "openai_responses", "anthropic", "google"]

// 协议类型
const provider = ref<ProviderKey>("openai")

// API 地址
const baseUrl = ref("")

// API Key
const apiKey = ref("")

// 是否正在请求模型列表
const loading = ref(false)

// 拉取到的模型 id 列表
const models = ref<string[]>([])

// 选中的模型
const selectedModel = ref("")

// 拉取失败提示
const errorMsg = ref("")

// API Key 占位符提示
const apiKeyPlaceholder = computed(() => {
	switch (provider.value) {
		case "anthropic":
			return "sk-ant-..."
		case "google":
			return "AIza..."
		default:
			return "sk-..."
	}
})

// Base URL 占位符提示
const baseUrlPlaceholder = computed(() => {
	return DEFAULT_BASE_URLS[provider.value] || "https://api.openai.com/v1"
})

// 用户自定义人设系统提示词
const userPersona = ref("")

// 读取已保存的配置
onMounted(async () => {
	try {
		const [SAVED_PROVIDER, BASE, KEY, MODEL, SAVED_PERSONA] = await Promise.all([
			invoke<string | null>("get_config", {key: KEY_PROVIDER}),
			invoke<string | null>("get_config", {key: KEY_BASE}),
			invoke<string | null>("get_config", {key: KEY_APIKEY}),
			invoke<string | null>("get_config", {key: KEY_MODEL}),
			invoke<string | null>("get_config", {key: "nori_user_persona"}),
		])
		if (SAVED_PROVIDER && PROVIDER_OPTIONS.includes(SAVED_PROVIDER as ProviderKey)) {
			provider.value = SAVED_PROVIDER as ProviderKey
		}
		if (BASE) baseUrl.value = BASE
		if (KEY) apiKey.value = KEY
		if (MODEL) selectedModel.value = MODEL
		if (SAVED_PERSONA) userPersona.value = SAVED_PERSONA
		if (BASE && KEY) await fetchModels()
	} catch (error) {
		console.error("读取 LLM 配置失败:", error)
	}
})

// 保存配置: 输入防抖 (每个 key 独立 timer, 避免互相 clear 导致写入丢失)
const timers = new Map<string, ReturnType<typeof setTimeout>>()
const saveOnChange = (key: string, get: () => string) => {
	clearTimeout(timers.get(key))
	timers.set(key, setTimeout(() => {
		timers.delete(key)
		const VALUE = get()
		if (!VALUE) return
		try {
			invoke("set_config", {key, value: VALUE})
			if (key !== KEY_APIKEY) invoke("write_log", {level: "info", message: `保存配置键 ${key} 为: ${VALUE}`})
		} catch (error) {
			console.error("保存 LLM 配置失败:", error)
		}
	}, 400))
}

watch(baseUrl, v => saveOnChange(KEY_BASE, () => v))
watch(apiKey, v => saveOnChange(KEY_APIKEY, () => v))

// 切换 Provider
const onProviderChange = () => {
	const CURRENT_DEF = DEFAULT_BASE_URLS[provider.value]
	// 如果之前地址为空或者属于某种默认地址，自动填充为当前协议的默认地址
	const IS_ANY_DEFAULT = Object.values(DEFAULT_BASE_URLS).includes(baseUrl.value)
	if (!baseUrl.value || IS_ANY_DEFAULT) {
		baseUrl.value = CURRENT_DEF
	}
	models.value = []
	selectedModel.value = ""
	try {
		invoke("set_config", {key: KEY_PROVIDER, value: provider.value})
		invoke("write_log", {level: "info", message: `保存配置键 ${KEY_PROVIDER} 为: ${provider.value}`})
	} catch (error) {
		console.error("保存协议类型失败:", error)
	}
}

// 选中模型直接保存
watch(selectedModel, value => {
	if (!value) return
	try {
		invoke("set_config", {key: KEY_MODEL, value: value})
		invoke("write_log", {level: "info", message: `保存配置键 ${KEY_MODEL} 为: ${value}`})
	} catch (error) {
		console.error("保存模型失败:", error)
	}
})

// 获取模型按钮
const fetchModels = async () => {
	errorMsg.value = ""
	if (!baseUrl.value.trim()) {
		errorMsg.value = I18N.value.error.apiBaseUrl
		return
	}
	if (!apiKey.value.trim()) {
		errorMsg.value = I18N.value.error.apiKey
		return
	}
	loading.value = true
	try {
		const result = await invoke<unknown>("fetch_llm_models", {
			provider: provider.value,
			baseUrl: baseUrl.value,
			apiKey: apiKey.value,
		})
		models.value = Array.isArray(result) ? (result as string[]) : []
		if (models.value.length === 0) {
			errorMsg.value = I18N.value.modelEmpty
		} else if (!models.value.includes(selectedModel.value)) {
			selectedModel.value = models.value[0]
		}
	} catch (error) {
		errorMsg.value = String(error)
		console.error("获取模型失败:", error)
	} finally {
		loading.value = false
	}
}
</script>

<template>
	<section class="ai-settings">
		<div class="ai-head">
			<h2 class="ai-title glow-teal">{{ I18N.title }}</h2>
			<p class="ai-sub">{{ I18N.sub }}</p>
		</div>

		<div class="ai-form">
			<label class="field">
				<span class="field-label">{{ I18N.provider }}</span>
				<select v-model="provider" class="input select" @change="onProviderChange">
					<option v-for="p in PROVIDER_OPTIONS" :key="p" :value="p">
						{{ I18N.providers[p] }}
					</option>
				</select>
			</label>

			<label class="field">
				<span class="field-label">{{ I18N.apiBaseUrl }}</span>
				<input
					v-model="baseUrl"
					class="input"
					type="text"
					:placeholder="baseUrlPlaceholder"
					spellcheck="false"
				/>
			</label>

			<label class="field">
				<span class="field-label">{{ I18N.apiKey }}</span>
				<input
					v-model="apiKey"
					class="input"
					type="password"
					:placeholder="apiKeyPlaceholder"
					spellcheck="false"
					autocomplete="off"
				/>
			</label>

			<div class="field">
				<span class="field-label">{{ I18N.model }}</span>
				<div class="model-row">
					<select v-model="selectedModel" class="input select" :disabled="models.length === 0">
						<option v-if="models.length === 0" value="" disabled>{{ I18N.modelEmpty }}</option>
						<option v-for="m in models" :key="m" :value="m">{{ m }}</option>
					</select>
					<button class="fetch-btn" :disabled="loading" @click="fetchModels">
						<Icon v-if="loading" name="loading" class="btn-icon spin"/>
						{{ loading ? I18N.getting : I18N.getModel }}
					</button>
				</div>
			</div>

			<div class="field">
				<span class="field-label">人设与系统提示词 (System Prompt)</span>
				<textarea
					v-model="userPersona"
					class="input textarea"
					rows="4"
					placeholder="设定 Nori 的人设、性格、口吻或对话规则（留空将使用默认陪伴人设）..."
					@blur="saveOnChange('nori_user_persona', () => userPersona)"
				/>
			</div>

			<p v-if="errorMsg" class="error">{{ errorMsg }}</p>
		</div>
	</section>
</template>

<style scoped lang="less">
.ai-settings {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
	padding: 1.6rem 2.4rem;
	gap: 1.6rem;
}

.ai-head {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.ai-title {
	font-size: 1.8rem;
	font-weight: 700;
	color: var(--text-primary);
}

.ai-sub {
	font-size: 1.2rem;
	color: var(--text-faint);
}

.ai-form {
	width: 100%;
	max-width: 52rem;
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.8rem;
}

.field {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.field-label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-muted);
}

.input {
	padding: 0.95rem 1.4rem;
	width: 100%;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	outline: none;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:focus {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.input::placeholder {
	color: var(--text-faint);
	opacity: 0.7;
}

.select {
	cursor: pointer;

	option {
		color: var(--text-primary);
		background: #081a2e;
	}
}

.model-row {
	display: flex;
	gap: 1rem;
	align-items: center;

	.select {
		flex: 1;
	}
}

.fetch-btn {
	padding: 0.95rem 1.8rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	font-size: 1.3rem;
	font-weight: 600;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	white-space: nowrap;
	flex-shrink: 0;

	&:hover:not(:disabled) {
		box-shadow: 0 0.4rem 1.6rem var(--glow-teal-strong);
		transform: translateY(-0.15rem);
	}

	&:active:not(:disabled) {
		transform: scale(0.96);
	}

	&:disabled {
		opacity: 0.6;
		cursor: default;
		filter: grayscale(0.5);
	}
}

.btn-icon {
	width: 1.4rem;
	height: 1.4rem;
}

.error {
	font-size: 1.2rem;
	color: var(--danger);
	padding: 0.6rem 1rem;
	background: rgba(251, 60, 68, 0.1);
	border: 0.1rem solid rgba(251, 60, 68, 0.25);
	border-radius: var(--radius-sm);
}
</style>
