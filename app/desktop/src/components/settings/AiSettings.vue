<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import {feedback} from "../../services/feedback"
import {RUNTIME} from "../../services/runtime"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppCard from "../ui/AppCard.vue"
import AppField from "../ui/AppField.vue"
import AppButton from "../ui/AppButton.vue"

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

// 错误提示细分
const failText = (key: string): string => {
	if (key === "provider") return I18N.value.providerSaveFailed
	if (key === "model") return I18N.value.modelSaveFailed
	if (key.startsWith("embedding")) return I18N.value.embeddingSaveFailed
	return I18N.value.saveFailed
}

const SAVE_MGR = useSnapshotSave({
	onError: (key, error) => feedback.error(failText(key), error),
})
const {defineField, save, saveNow, stateOf, errorOf} = SAVE_MGR

// 对话配置字段
const providerField = useSnapshotField(snapshot => {
	const VALUE = snapshot.ai.chat?.provider ?? snapshot.ai.provider
	return (PROVIDER_OPTIONS as string[]).includes(VALUE) ? VALUE as ProviderKey : "openai"
}, "openai" as ProviderKey)
const provider = providerField.value

const baseUrlField = defineField(
	"baseUrl",
	snapshot => snapshot.ai.chat?.baseUrl ?? snapshot.ai.baseUrl,
	"",
	val => RUNTIME.updateAiProviders({chat: {baseUrl: val.trim()}}),
)
const baseUrl = baseUrlField.value

const modelField = useSnapshotField(snapshot => snapshot.ai.chat?.model ?? snapshot.ai.model, "")
const model = modelField.value

const personaField = defineField(
	"persona",
	snapshot => snapshot.ai.chat?.persona ?? snapshot.ai.persona,
	"",
	val => RUNTIME.updateAiProviders({persona: val}),
)
const persona = personaField.value

const apiKeyInput = ref("")
const hasApiKey = computed(() => RUNTIME.snapshot.value?.ai.chat?.hasApiKey ?? RUNTIME.snapshot.value?.ai.hasApiKey ?? false)

const apiKeyPlaceholder = computed(() => {
	if (hasApiKey.value) return I18N.value.apiKeySavedHint
	switch (provider.value) {
		case "anthropic":
			return "sk-ant-..."
		case "google":
			return "AIza..."
		default:
			return "sk-..."
	}
})
const baseUrlPlaceholder = computed(() => DEFAULT_BASE_URLS[provider.value] || "https://api.openai.com/v1")

// 对话模型状态
const loadingModels = ref(false)
const testingChat = ref(false)
const models = ref<string[]>([])
const chatErrorMsg = ref("")
const chatConnectionResult = ref("")
const chatConnectionSuccess = ref(false)

// Embedding 配置字段 (独立隔离)
const embeddingModelField = defineField(
	"embeddingModel",
	snapshot => snapshot.ai.embedding?.model ?? snapshot.embedding.model,
	"BAAI/bge-m3",
	val => RUNTIME.updateAiProviders({embedding: {model: val.trim()}}),
)
const embeddingModel = embeddingModelField.value

const embeddingBaseUrlField = defineField(
	"embeddingBaseUrl",
	snapshot => snapshot.ai.embedding?.baseUrl ?? snapshot.embedding.baseUrl,
	"",
	val => RUNTIME.updateAiProviders({embedding: {baseUrl: val.trim()}}),
)
const embeddingBaseUrl = embeddingBaseUrlField.value

const embeddingDimensionsField = defineField(
	"embeddingDimensions",
	snapshot => snapshot.ai.embedding?.dimensions ?? snapshot.embedding.dimensions,
	"",
)
const embeddingDimensions = embeddingDimensionsField.value

const embeddingApiKeyInput = ref("")
const hasEmbeddingApiKey = computed(() => RUNTIME.snapshot.value?.ai.embedding?.hasApiKey ?? RUNTIME.snapshot.value?.embedding.hasApiKey ?? false)

const embeddingApiKeyPlaceholder = computed(() => {
	if (hasEmbeddingApiKey.value) return I18N.value.apiKeySavedHint
	return I18N.value.embeddingApiKeyPlaceholder
})

const testingEmbedding = ref(false)
const embeddingConnectionResult = ref("")
const embeddingConnectionSuccess = ref(false)

onMounted(async () => {
	await RUNTIME.init()
})

const onBaseUrlBlur = () => {
	if (!baseUrl.value.trim()) {
		baseUrlField.reset()
		return
	}
	baseUrlField.save()
}

const onApiKeyBlur = () => {
	const VALUE = apiKeyInput.value.trim()
	apiKeyInput.value = ""
	if (!VALUE) return
	save("apiKey", async () => {
		try {
			await RUNTIME.updateAiProviders({chat: {apiKey: VALUE}})
		} catch (error) {
			apiKeyInput.value = VALUE
			throw error
		}
	})
}

const onPersonaBlur = () => {
	personaField.save()
}

// 切换 Provider: 默认地址联动 + 立即保存
const onProviderChange = () => {
	const CURRENT_DEF = DEFAULT_BASE_URLS[provider.value]
	const IS_ANY_DEFAULT = Object.values(DEFAULT_BASE_URLS).includes(baseUrl.value)
	if (!baseUrl.value || IS_ANY_DEFAULT) {
		baseUrl.value = CURRENT_DEF
	}
	providerField.touch()
	baseUrlField.touch()
	models.value = []
	model.value = ""
	void saveNow("provider", async () => {
		try {
			await RUNTIME.updateAiProviders({chat: {provider: provider.value, baseUrl: baseUrl.value}})
			providerField.commit()
			baseUrlField.commit()
		} catch (error) {
			providerField.reset()
			baseUrlField.reset()
			throw error
		}
	})
}

// 选中模型直接保存
const onSelectModel = (value: string) => {
	if (!value) return
	model.value = value
	modelField.touch()
	void saveNow("model", async () => {
		try {
			await RUNTIME.updateAiProviders({chat: {model: value}})
			modelField.commit()
		} catch (error) {
			modelField.reset()
			throw error
		}
	})
}

// 获取模型列表
const fetchModels = async () => {
	chatErrorMsg.value = ""
	if (!baseUrl.value.trim()) {
		chatErrorMsg.value = I18N.value.error.apiBaseUrl
		return
	}
	if (!hasApiKey.value && !apiKeyInput.value.trim()) {
		chatErrorMsg.value = I18N.value.error.apiKey
		return
	}
	loadingModels.value = true
	try {
		const KEY = apiKeyInput.value.trim()
		const result = await RUNTIME.fetchModels(provider.value, baseUrl.value.trim(), KEY)
		if (KEY) {
			apiKeyInput.value = ""
			await RUNTIME.updateAiProviders({chat: {apiKey: KEY}})
		}
		models.value = Array.isArray(result) ? result : []
		if (models.value.length === 0) {
			chatErrorMsg.value = I18N.value.modelEmpty
		} else if (!models.value.includes(model.value)) {
			onSelectModel(models.value[0])
			model.value = models.value[0]
		}
	} catch (error) {
		chatErrorMsg.value = I18N.value.fetchFailed
		console.error("获取模型失败", error instanceof Error ? error.name : "未知错误")
	} finally {
		loadingModels.value = false
	}
}

// 测试对话模型连接
const testChatConnection = async () => {
	chatConnectionResult.value = ""
	testingChat.value = true
	const API_KEY = apiKeyInput.value.trim()
	try {
		const RESULT = await RUNTIME.testAiConnection({
			target: "chat",
			provider: provider.value,
			baseUrl: baseUrl.value.trim(),
			apiKey: API_KEY,
			model: model.value.trim(),
		})
		chatConnectionSuccess.value = RESULT.success
		chatConnectionResult.value = RESULT.success
			? I18N.value.testSuccess
			: `${I18N.value.testFailed}: ${RESULT.message}`
	} catch (error) {
		chatConnectionSuccess.value = false
		chatConnectionResult.value = I18N.value.connectionError
		feedback.error(I18N.value.connectionError, error)
		console.error("Chat 连接测试失败", error instanceof Error ? error.name : "未知错误")
	} finally {
		apiKeyInput.value = ""
		testingChat.value = false
	}
}

// Embedding 字段与操作
const onEmbeddingModelBlur = () => {
	embeddingModelField.save()
}

const onEmbeddingBaseUrlBlur = () => {
	embeddingBaseUrlField.save()
}

const onEmbeddingApiKeyBlur = () => {
	const VALUE = embeddingApiKeyInput.value.trim()
	embeddingApiKeyInput.value = ""
	if (!VALUE) return
	save("embeddingApiKey", async () => {
		try {
			await RUNTIME.updateAiProviders({embedding: {apiKey: VALUE}})
		} catch (error) {
			embeddingApiKeyInput.value = VALUE
			throw error
		}
	})
}

const onEmbeddingDimensionsBlur = () => {
	const RAW = embeddingDimensions.value.trim()
	if (RAW === "") {
		embeddingDimensionsField.save(() => RUNTIME.updateAiProviders({embedding: {dimensions: ""}}))
		return
	}
	const NUM = Number.parseInt(RAW, 10)
	if (Number.isNaN(NUM) || NUM <= 0) {
		embeddingDimensions.value = ""
		embeddingDimensionsField.save(() => RUNTIME.updateAiProviders({embedding: {dimensions: ""}}))
		return
	}
	embeddingDimensions.value = String(NUM)
	embeddingDimensionsField.save(() => RUNTIME.updateAiProviders({embedding: {dimensions: String(NUM)}}))
}

const testEmbeddingConnection = async () => {
	embeddingConnectionResult.value = ""
	testingEmbedding.value = true
	const API_KEY = embeddingApiKeyInput.value.trim()
	try {
		const RESULT = await RUNTIME.testAiConnection({
			target: "embedding",
			baseUrl: embeddingBaseUrl.value.trim(),
			apiKey: API_KEY,
			model: embeddingModel.value.trim(),
			dimensions: embeddingDimensions.value.trim() || undefined,
		})
		embeddingConnectionSuccess.value = RESULT.success
		embeddingConnectionResult.value = RESULT.success
			? I18N.value.testSuccess
			: `${I18N.value.testFailed}: ${RESULT.message}`
	} catch (error) {
		embeddingConnectionSuccess.value = false
		embeddingConnectionResult.value = I18N.value.connectionError
		feedback.error(I18N.value.connectionError, error)
		console.error("Embedding 连接测试失败", error instanceof Error ? error.name : "未知错误")
	} finally {
		embeddingApiKeyInput.value = ""
		testingEmbedding.value = false
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
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.sub"/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 对话模型配置 (Chat LLM) -->
			<AppCard :title="I18N.chatSection" icon="chat">
				<div class="field">
					<span class="field-label font-500">{{ I18N.provider }}</span>
					<n-select
						v-model:value="provider"
						:options="providerOptions"
						@update:value="onProviderChange"
					/>
				</div>

				<AppField :label="I18N.apiBaseUrl" :state="stateOf('baseUrl')" :error="errorOf('baseUrl')">
					<input
						v-model="baseUrl"
						class="input-base"
						type="text"
						:placeholder="baseUrlPlaceholder"
						spellcheck="false"
						@focus="baseUrlField.focus"
						@input="baseUrlField.touch"
						@blur="onBaseUrlBlur"
					/>
				</AppField>

				<AppField :label="hasApiKey ? `${I18N.apiKey} ${I18N.apiKeyStored}` : I18N.apiKey" :state="stateOf('apiKey')" :error="errorOf('apiKey')">
					<input
						v-model="apiKeyInput"
						class="input-base"
						type="password"
						:placeholder="apiKeyPlaceholder"
						spellcheck="false"
						autocomplete="off"
						@blur="onApiKeyBlur"
					/>
				</AppField>

				<div class="field">
					<span class="field-label font-500">{{ I18N.model }}</span>
					<div class="flex items-center gap-2.5">
						<n-select
							:value="model"
							:options="modelOptions"
							:disabled="modelOptions.length === 0"
							:placeholder="modelOptions.length === 0 ? I18N.modelEmpty : I18N.modelPlaceholder"
							class="flex-1"
							@update:value="onSelectModel"
						/>
						<div class="flex items-center gap-2">
							<AppButton
								variant="primary"
								size="sm"
								:loading="loadingModels"
								:disabled="loadingModels || testingChat"
								@click="fetchModels"
							>
								{{ loadingModels ? I18N.getting : I18N.getModel }}
							</AppButton>
							<AppButton
								variant="ghost"
								size="sm"
								:loading="testingChat"
								:disabled="loadingModels || testingChat"
								@click="testChatConnection"
							>
								{{ testingChat ? I18N.testingConnection : I18N.testConnection }}
							</AppButton>
						</div>
					</div>
				</div>

				<AppField :label="I18N.persona" :state="stateOf('persona')" :error="errorOf('persona')">
					<textarea
						v-model="persona"
						class="input-base resize-y leading-relaxed"
						rows="4"
						:placeholder="I18N.personaPlaceholder"
						@focus="personaField.focus"
						@input="personaField.touch"
						@blur="onPersonaBlur"
					/>
				</AppField>

				<p
					v-if="chatErrorMsg"
					class="px-3 py-2 rounded-sm text-sm text-danger-text bg-danger/12 border border-danger/35 font-500"
					role="alert"
				>{{ chatErrorMsg }}</p>
				<p
					v-if="chatConnectionResult"
					class="px-3 py-2 rounded-sm text-sm font-500"
					:class="chatConnectionSuccess ? 'text-nori-teal-bright bg-nori-teal-bright/8 border border-nori-teal-soft/30' : 'text-danger-text bg-danger/12 border border-danger/35'"
					role="status"
				>{{ chatConnectionResult }}</p>
			</AppCard>

			<!-- 2. 向量嵌入配置 (Embedding) -->
			<AppCard :title="I18N.embeddingSection" icon="sparkles">
				<template #actions>
					<AppButton
						variant="ghost"
						size="sm"
						:loading="testingEmbedding"
						:disabled="testingEmbedding"
						@click="testEmbeddingConnection"
					>
						{{ testingEmbedding ? I18N.testingConnection : I18N.testEmbeddingConnection }}
					</AppButton>
				</template>

				<div class="flex gap-3">
					<AppField :label="I18N.embeddingModel" :state="stateOf('embeddingModel')" :error="errorOf('embeddingModel')" class="flex-1">
						<input
							v-model="embeddingModel"
							class="input-base"
							type="text"
							:placeholder="I18N.embeddingModelPlaceholder"
							spellcheck="false"
							@focus="embeddingModelField.focus"
							@input="embeddingModelField.touch"
							@blur="onEmbeddingModelBlur"
						/>
					</AppField>
					<AppField :label="I18N.embeddingBaseUrl" :state="stateOf('embeddingBaseUrl')" :error="errorOf('embeddingBaseUrl')" class="flex-1">
						<input
							v-model="embeddingBaseUrl"
							class="input-base"
							type="text"
							:placeholder="I18N.embeddingBaseUrlPlaceholder"
							spellcheck="false"
							@focus="embeddingBaseUrlField.focus"
							@input="embeddingBaseUrlField.touch"
							@blur="onEmbeddingBaseUrlBlur"
						/>
					</AppField>
				</div>

				<div class="flex gap-3">
					<AppField :label="hasEmbeddingApiKey ? `${I18N.embeddingApiKey} ${I18N.apiKeyStored}` : I18N.embeddingApiKey" :state="stateOf('embeddingApiKey')" :error="errorOf('embeddingApiKey')" class="flex-1">
						<input
							v-model="embeddingApiKeyInput"
							type="password"
							class="input-base"
							:placeholder="embeddingApiKeyPlaceholder"
							spellcheck="false"
							autocomplete="off"
							@blur="onEmbeddingApiKeyBlur"
						/>
					</AppField>
					<AppField :label="I18N.embeddingDimensions" :state="stateOf('embeddingDimensions')" :error="errorOf('embeddingDimensions')" class="w-[14rem] shrink-0">
						<input
							v-model="embeddingDimensions"
							type="number"
							min="1"
							class="input-base"
							:placeholder="I18N.embeddingDimensionsPlaceholder"
							@focus="embeddingDimensionsField.focus"
							@input="embeddingDimensionsField.touch"
							@blur="onEmbeddingDimensionsBlur"
						/>
					</AppField>
				</div>

				<p class="text-hint leading-relaxed">{{ I18N.embeddingDimensionsHint }}</p>

				<p
					v-if="embeddingConnectionResult"
					class="px-3 py-2 rounded-sm text-sm font-500"
					:class="embeddingConnectionSuccess ? 'text-nori-teal-bright bg-nori-teal-bright/8 border border-nori-teal-soft/30' : 'text-danger-text bg-danger/12 border border-danger/35'"
					role="status"
				>{{ embeddingConnectionResult }}</p>
			</AppCard>
		</div>
	</div>
</template>
