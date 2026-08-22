<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {RUNTIME} from "../../services/runtime"

const I18N = computed(() => useLanguages().views.main.ai)

// 协议列表定义
type ProviderKey = "openai" | "openai_responses" | "anthropic" | "google"
const PROVIDER_OPTIONS: ProviderKey[] = ["openai", "openai_responses", "anthropic", "google"]

// 默认 Base URL 表
const DEFAULT_BASE_URLS: Record<string, string> = {
	openai: "https://api.openai.com/v1",
	openai_responses: "https://api.openai.com/v1",
	anthropic: "https://api.anthropic.com/v1",
	google: "https://generativelanguage.googleapis.com/v1beta",
}

// 本地编辑缓冲 (快照为真相, 输入防抖后提交)
const provider = ref<ProviderKey>("openai")
const baseUrl = ref("")
const apiKeyInput = ref("")
const model = ref("")
const persona = ref("")
const hasApiKey = computed(() => RUNTIME.snapshot.value?.ai.hasApiKey ?? false)
const apiKeyPlaceholder = computed(() => {
	switch (provider.value) {
		case "anthropic":
			return hasApiKey.value ? "已保存 (输入可更换)" : "sk-ant-..."
		case "google":
			return hasApiKey.value ? "已保存 (输入可更换)" : "AIza..."
		default:
			return hasApiKey.value ? "已保存 (输入可更换)" : "sk-..."
	}
})
const baseUrlPlaceholder = computed(() => DEFAULT_BASE_URLS[provider.value] || "https://api.openai.com/v1")

// 拉取模型
const loading = ref(false)
const models = ref<string[]>([])
const errorMsg = ref("")

// 从快照同步到本地编辑态 (仅初始化一次, 避免覆盖正在输入的内容)
let synced = false
const syncFromSnapshot = () => {
	const AI = RUNTIME.snapshot.value?.ai
	if (!AI || synced) return
	synced = true
	if ((PROVIDER_OPTIONS as string[]).includes(AI.provider)) provider.value = AI.provider as ProviderKey
	baseUrl.value = AI.baseUrl
	model.value = AI.model
	persona.value = AI.persona
}

onMounted(async () => {
	await RUNTIME.init()
	syncFromSnapshot()
})

// 保存辅助: 每个 key 独立防抖 timer (规范要求)
const timers = new Map<string, ReturnType<typeof setTimeout>>()
const saveDebounced = (key: string, value: () => Record<string, unknown>) => {
	clearTimeout(timers.get(key))
	timers.set(key, setTimeout(() => {
		timers.delete(key)
		void RUNTIME.updateAi(value()).catch(error => console.error(`保存 AI 配置失败 (${key}):`, error))
	}, 400))
}

const onBaseUrlChange = () => {
	if (!baseUrl.value.trim()) return
	saveDebounced("baseUrl", () => ({baseUrl: baseUrl.value.trim()}))
}
const onApiKeyChange = () => {
	const VALUE = apiKeyInput.value.trim()
	apiKeyInput.value = ""
	if (!VALUE) return
	saveDebounced("apiKey", () => ({apiKey: VALUE}))
}
const onPersonaChange = () => {
	saveDebounced("persona", () => ({persona: persona.value}))
}

// 切换 Provider: 默认地址联动 + 立即保存
const onProviderChange = () => {
	const CURRENT_DEF = DEFAULT_BASE_URLS[provider.value]
	const IS_ANY_DEFAULT = Object.values(DEFAULT_BASE_URLS).includes(baseUrl.value)
	if (!baseUrl.value || IS_ANY_DEFAULT) {
		baseUrl.value = CURRENT_DEF
	}
	models.value = []
	model.value = ""
	void RUNTIME.updateAi({provider: provider.value, baseUrl: baseUrl.value}).catch(error => console.error("保存协议类型失败:", error))
}

// 选中模型直接保存
const onSelectModel = (value: string) => {
	if (!value) return
	void RUNTIME.updateAi({model: value}).catch(error => console.error("保存模型失败:", error))
}

// 获取模型按钮 (密钥只在本次调用中发往后端, 不回显)
const fetchModels = async () => {
	errorMsg.value = ""
	if (!baseUrl.value.trim()) {
		errorMsg.value = I18N.value.error.apiBaseUrl
		return
	}
	if (!hasApiKey.value && !apiKeyInput.value.trim()) {
		errorMsg.value = I18N.value.error.apiKey
		return
	}
	loading.value = true
	try {
		// 明文密钥只在当前请求中传给后端; 已保存密钥由后端内部读取, 前端永不回读。
		const KEY = apiKeyInput.value.trim()
		const result = await RUNTIME.fetchModels(provider.value, baseUrl.value.trim(), KEY)
		if (KEY) {
			apiKeyInput.value = ""
			await RUNTIME.updateAi({apiKey: KEY})
		}
		models.value = Array.isArray(result) ? result : []
		if (models.value.length === 0) {
			errorMsg.value = I18N.value.modelEmpty
		} else if (!models.value.includes(model.value)) {
			onSelectModel(models.value[0])
			model.value = models.value[0]
		}
	} catch (error) {
		errorMsg.value = String(error)
		console.error("获取模型失败:", error)
	} finally {
		loading.value = false
	}
}

const providerOptions = computed(() =>
	PROVIDER_OPTIONS.map(p => ({label: I18N.value.providers[p], value: p}))
)

const modelOptions = computed(() => {
	const LIST = [...models.value]
	if (model.value && !LIST.includes(model.value)) LIST.unshift(model.value)
	return LIST.map(item => ({label: item, value: item}))
})
</script>

<template>
	<section class="ai-settings">
		<div class="ai-head">
			<h2 class="ai-title glow-teal">{{ I18N.title }}</h2>
			<p class="ai-sub">{{ I18N.sub }}</p>
		</div>

		<div class="ai-form">
			<div class="field">
				<span class="field-label">{{ I18N.provider }}</span>
				<n-select
					v-model:value="provider"
					:options="providerOptions"
					@update:value="onProviderChange"
				/>
			</div>

			<label class="field">
				<span class="field-label">{{ I18N.apiBaseUrl }}</span>
				<input
					v-model="baseUrl"
					class="input"
					type="text"
					:placeholder="baseUrlPlaceholder"
					spellcheck="false"
					@blur="onBaseUrlChange"
				/>
			</label>

			<label class="field">
				<span class="field-label">{{ I18N.apiKey }}{{ hasApiKey ? " (已加密保存)" : "" }}</span>
				<input
					v-model="apiKeyInput"
					class="input"
					type="password"
					:placeholder="apiKeyPlaceholder"
					spellcheck="false"
					autocomplete="off"
					@blur="onApiKeyChange"
				/>
			</label>

			<div class="field">
				<span class="field-label">{{ I18N.model }}</span>
				<div class="model-row">
					<n-select
						:value="model"
						:options="modelOptions"
						:disabled="modelOptions.length === 0"
						:placeholder="modelOptions.length === 0 ? I18N.modelEmpty : '请选择模型'"
						class="flex-1"
						@update:value="onSelectModel"
					/>
					<n-button
						type="primary"
						:loading="loading"
						:disabled="loading"
						@click="fetchModels"
					>
						{{ loading ? I18N.getting : I18N.getModel }}
					</n-button>
				</div>
			</div>

			<div class="field">
				<span class="field-label">人设与系统提示词 (System Prompt)</span>
				<textarea
					v-model="persona"
					class="input textarea"
					rows="4"
					placeholder="设定 Nori 的人设、性格、口吻或对话规则（留空将使用默认陪伴人设）..."
					@blur="onPersonaChange"
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

.model-row {
	display: flex;
	gap: 1rem;
	align-items: center;

	.flex-1 {
		flex: 1;
	}
}

.textarea {
	resize: vertical;
	line-height: 1.5;
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
