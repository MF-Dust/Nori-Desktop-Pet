<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {RUNTIME} from "../../services/runtime"
import Icon from "../../components/Icon.vue"

const I18N = computed(() => useLanguages().components.firstRun.llmConnect)
type ProviderKey = "openai" | "openai_responses" | "anthropic" | "google"
const PROVIDERS: ProviderKey[] = ["openai", "openai_responses", "anthropic", "google"]
const DEFAULT_BASE: Record<ProviderKey, string> = {
	openai: "https://api.openai.com/v1",
	openai_responses: "https://api.openai.com/v1",
	anthropic: "https://api.anthropic.com/v1",
	google: "https://generativelanguage.googleapis.com/v1beta",
}
const provider = ref<ProviderKey>("openai")
const baseUrl = ref("")
const apiKey = ref("")
const hasApiKey = ref(false)
const models = ref<string[]>([])
const selectedModel = ref("")
const loading = ref(false)
const errorMsg = ref("")

const baseUrlPlaceholder = computed(() => DEFAULT_BASE[provider.value])
const apiKeyPlaceholder = computed(() => hasApiKey.value ? "已保存 API Key；如需更换请重新输入" : "sk-...")

onMounted(async () => {
	await RUNTIME.init()
	const AI = RUNTIME.snapshot.value?.ai
	if (!AI) return
	if (PROVIDERS.includes(AI.provider as ProviderKey)) provider.value = AI.provider as ProviderKey
	baseUrl.value = AI.baseUrl
	selectedModel.value = AI.model
	if (AI.model) models.value = [AI.model]
	hasApiKey.value = AI.hasApiKey
})

const update = async (patch: Record<string, unknown>) => {
	try {
		await RUNTIME.updateAi(patch)
	} catch (error) {
		console.error("保存 LLM 配置失败:", error)
	}
}

const onProviderChange = () => {
	if (!baseUrl.value || Object.values(DEFAULT_BASE).includes(baseUrl.value)) baseUrl.value = DEFAULT_BASE[provider.value]
	models.value = []
	selectedModel.value = ""
	void update({provider: provider.value, baseUrl: baseUrl.value, model: ""})
}

const onBaseBlur = () => void update({baseUrl: baseUrl.value.trim()})
const onKeyBlur = () => {
	const KEY = apiKey.value.trim()
	apiKey.value = ""
	if (KEY) {
		hasApiKey.value = true
		void update({apiKey: KEY})
	}
}
const onModelChange = () => void update({model: selectedModel.value})

const fetchModels = async () => {
	errorMsg.value = ""
	if (!baseUrl.value.trim()) {
		errorMsg.value = I18N.value.error.apiBaseUrl
		return
	}
	const KEY = apiKey.value.trim()
	if (!KEY && !hasApiKey.value) {
		errorMsg.value = I18N.value.error.apiKey
		return
	}
	loading.value = true
	try {
		models.value = await RUNTIME.fetchModels(provider.value, baseUrl.value, KEY)
		if (KEY) {
			apiKey.value = ""
			hasApiKey.value = true
			await update({apiKey: KEY})
		}
		if (models.value.length === 0) errorMsg.value = I18N.value.modelEmpty
		else if (!models.value.includes(selectedModel.value)) {
			selectedModel.value = models.value[0]
			await update({model: selectedModel.value})
		}
	} catch (error) {
		errorMsg.value = error instanceof Error ? error.message : String(error)
		console.error("获取模型失败:", error)
	} finally {
		loading.value = false
	}
}
</script>

<template>
	<section class="page page-llm">
		<div class="head"><h2 class="title glow-teal">{{ I18N.title }}</h2><p class="subtitle">{{ I18N.sub }}</p></div>
		<div class="form">
			<label class="field"><span>{{ I18N.provider }}</span><select v-model="provider" class="input" @change="onProviderChange"><option v-for="item in PROVIDERS" :key="item" :value="item">{{ I18N.providers[item] }}</option></select></label>
			<label class="field"><span>{{ I18N.apiBaseUrl }}</span><input v-model="baseUrl" class="input" :placeholder="baseUrlPlaceholder" @blur="onBaseBlur"/></label>
			<label class="field"><span>{{ I18N.apiKey }}{{ hasApiKey ? " (已加密保存)" : "" }}</span><input v-model="apiKey" class="input" type="password" :placeholder="apiKeyPlaceholder" autocomplete="off" @blur="onKeyBlur"/></label>
			<div class="field"><span>{{ I18N.model }}</span><div class="model-row"><select v-model="selectedModel" class="input" :disabled="models.length === 0" @change="onModelChange"><option v-if="models.length === 0" value="" disabled>{{ I18N.modelEmpty }}</option><option v-for="model in models" :key="model" :value="model">{{ model }}</option></select><button class="fetch" :disabled="loading" @click="fetchModels"><Icon v-if="loading" name="loading" class="spin" :size="14"/>{{ loading ? I18N.getting : I18N.getModel }}</button></div></div>
			<p v-if="errorMsg" class="error">{{ errorMsg }}</p>
		</div>
	</section>
</template>

<style scoped lang="less">
.page { width: 100%; height: 100%; padding: 1rem 5.6rem; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2rem; }
.head { display: flex; flex-direction: column; align-items: center; gap: 0.6rem; }
.title { margin: 0; color: var(--text-primary); font-size: 2.4rem; }
.subtitle { margin: 0; color: var(--text-faint); font-size: 1.2rem; }
.form { width: 100%; max-width: 42rem; display: flex; flex-direction: column; gap: 1.3rem; }
.field { display: flex; flex-direction: column; gap: 0.5rem; color: var(--text-muted); font-size: 1.2rem; }
.input { width: 100%; box-sizing: border-box; padding: 0.85rem 1.1rem; border: 0.1rem solid var(--line-subtle); border-radius: var(--radius-sm); background: rgba(255,255,255,0.04); color: var(--text-primary); font: inherit; outline: none; }
.input:focus { border-color: var(--nori-teal-soft); }
.model-row { display: flex; align-items: center; gap: 0.8rem; }
.model-row .input { flex: 1; }
.fetch { display: inline-flex; align-items: center; gap: 0.5rem; padding: 0.85rem 1.2rem; border: none; border-radius: var(--radius-sm); background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal)); color: #05121a; font: inherit; font-size: 1.2rem; font-weight: 600; white-space: nowrap; cursor: pointer; }
.fetch:disabled { opacity: 0.6; cursor: not-allowed; }
.error { margin: 0; color: var(--danger); font-size: 1.2rem; text-align: center; }
.spin { animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
</style>
