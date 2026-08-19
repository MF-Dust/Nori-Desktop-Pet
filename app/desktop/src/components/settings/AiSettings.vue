<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {invoke} from "../../services/host/invoke"
import Icon from "../Icon.vue"

const I18N = computed(() => useLanguages().views.main.ai)

// 配置键名
const KEY_BASE = "llm_api_base"
const KEY_APIKEY = "llm_api_key"
const KEY_MODEL = "llm_model"

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

// 读取已保存的配置
onMounted(async () => {
	try {
		const [BASE, KEY, MODEL] = await Promise.all([
			invoke<string | null>("get_config", {key: KEY_BASE}),
			invoke<string | null>("get_config", {key: KEY_APIKEY}),
			invoke<string | null>("get_config", {key: KEY_MODEL}),
		])
		if (BASE) baseUrl.value = BASE
		if (KEY) apiKey.value = KEY
		if (MODEL) selectedModel.value = MODEL
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
		const result = await invoke<unknown>("fetch_llm_models", {baseUrl: baseUrl.value, apiKey: apiKey.value})
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
				<span class="field-label">{{ I18N.apiBaseUrl }}</span>
				<input
					v-model="baseUrl"
					class="input"
					type="text"
					placeholder="https://api.openai.com/v1"
					spellcheck="false"
				/>
			</label>

			<label class="field">
				<span class="field-label">{{ I18N.apiKey }}</span>
				<input
					v-model="apiKey"
					class="input"
					type="password"
					placeholder="sk-..."
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

			<p v-if="errorMsg" class="error">{{ errorMsg }}</p>
		</div>
	</section>
</template>

<style scoped lang="less">
.ai-settings {
	width: 100%;
	height: 100%;
	padding: 2.4rem 3.2rem;
	display: flex;
	flex-direction: column;
	gap: 2rem;
	overflow-y: auto;
}

.ai-head {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.ai-title {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--text-primary);
}

.ai-sub {
	font-size: 1.2rem;
	color: var(--text-faint);
}

.ai-form {
	width: 100%;
	max-width: 46rem;
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
}

.field {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.field-label {
	font-size: 1.2rem;
	color: var(--text-muted);
}

.input {
	padding: 0.9rem 1.2rem;
	width: 100%;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
	}
}

.input::placeholder {
	color: var(--text-muted);
	opacity: 0.6;
}

.select {
	cursor: pointer;

	option {
		color: var(--text-primary);
		background: var(--bg-deep);
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
	padding: 0.9rem 1.8rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	font-size: 1.3rem;
	font-weight: 600;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	white-space: nowrap;
	flex-shrink: 0;

	&:hover:not(:disabled) {
		box-shadow: 0 0 1.6rem var(--glow-teal-soft);
	}

	&:disabled {
		opacity: 0.6;
		cursor: default;
	}
}

.btn-icon {
	width: 1.4rem;
	height: 1.4rem;
}

.error {
	font-size: 1.2rem;
	color: var(--danger);
}
</style>
