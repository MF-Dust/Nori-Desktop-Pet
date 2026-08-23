<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import {feedback, errorText} from "../../services/feedback"
import {RUNTIME} from "../../services/runtime"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppField from "../ui/AppField.vue"

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

// 本地编辑缓冲: 未编辑字段跟随快照, 正在编辑或脏字段保持本地值。
const providerField = useSnapshotField(snapshot => {
	const VALUE = snapshot.ai.provider
	return (PROVIDER_OPTIONS as string[]).includes(VALUE) ? VALUE as ProviderKey : "openai"
}, "openai" as ProviderKey)
const baseUrlField = useSnapshotField(snapshot => snapshot.ai.baseUrl, "")
const modelField = useSnapshotField(snapshot => snapshot.ai.model, "")
const personaField = useSnapshotField(snapshot => snapshot.ai.persona, "")
const provider = providerField.value
const baseUrl = baseUrlField.value
const apiKeyInput = ref("")
const model = modelField.value
const persona = personaField.value
const hasApiKey = computed(() => RUNTIME.snapshot.value?.ai.hasApiKey ?? false)
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

// 拉取模型
const loading = ref(false)
const models = ref<string[]>([])
const errorMsg = ref("")

onMounted(async () => {
	await RUNTIME.init()
})

// 保存辅助: 每个 key 独立防抖 timer + 卸载 flush (规范要求)
// 失败提示按字段细分, 保留原来日志里的语义
const failText = (key: string): string => {
	if (key === "provider") return I18N.value.providerSaveFailed
	if (key === "model") return I18N.value.modelSaveFailed
	return I18N.value.saveFailed
}
const SAVE = useDebouncedSave({
	onError: (key, error) => feedback.error(failText(key), error),
})

const saveField = (key: string, field: {touch: () => void; blur: () => void; reset: () => void; commit: () => void}, task: () => Promise<void>): void => {
	field.touch()
	field.blur()
	SAVE.save(key, async () => {
		try {
			await task()
			field.commit()
		} catch (error) {
			field.reset()
			throw error
		}
	})
}

const onBaseUrlChange = () => {
	if (!baseUrl.value.trim()) {
		baseUrlField.reset()
		return
	}
	saveField("baseUrl", baseUrlField, () => RUNTIME.updateAi({baseUrl: baseUrl.value.trim()}))
}
const onApiKeyChange = () => {
	const VALUE = apiKeyInput.value.trim()
	apiKeyInput.value = ""
	if (!VALUE) return
	SAVE.save("apiKey", async () => {
		try {
			await RUNTIME.updateAi({apiKey: VALUE})
		} catch (error) {
			apiKeyInput.value = VALUE
			throw error
		}
	})
}
const onPersonaChange = () => {
	saveField("persona", personaField, () => RUNTIME.updateAi({persona: persona.value}))
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
	void SAVE.saveNow("provider", async () => {
		try {
			await RUNTIME.updateAi({provider: provider.value, baseUrl: baseUrl.value})
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
	void SAVE.saveNow("model", async () => {
		try {
			await RUNTIME.updateAi({model: value})
			modelField.commit()
		} catch (error) {
			modelField.reset()
			throw error
		}
	})
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
		errorMsg.value = `${I18N.value.fetchFailed}: ${errorText(error)}`
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
	<section class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.sub"/>

		<div class="w-full max-w-[54rem] flex flex-col gap-4 p-5 surface-card rounded-lg shadow-[0_0.6rem_2.4rem_rgba(0,0,0,0.35)]">
			<div class="field">
				<span class="field-label font-500">{{ I18N.provider }}</span>
				<n-select
					v-model:value="provider"
					:options="providerOptions"
					@update:value="onProviderChange"
				/>
			</div>

			<AppField :label="I18N.apiBaseUrl" :state="SAVE.stateOf('baseUrl')" :error="SAVE.errorOf('baseUrl')">
				<input
					v-model="baseUrl"
					class="input-base"
					type="text"
					:placeholder="baseUrlPlaceholder"
					spellcheck="false"
					@focus="baseUrlField.focus"
					@input="baseUrlField.touch"
					@blur="onBaseUrlChange"
				/>
			</AppField>

			<AppField :label="hasApiKey ? `${I18N.apiKey} ${I18N.apiKeyStored}` : I18N.apiKey" :state="SAVE.stateOf('apiKey')">
				<input
					v-model="apiKeyInput"
					class="input-base"
					type="password"
					:placeholder="apiKeyPlaceholder"
					spellcheck="false"
					autocomplete="off"
					@blur="onApiKeyChange"
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

			<AppField :label="I18N.persona" :state="SAVE.stateOf('persona')" :error="SAVE.errorOf('persona')">
				<textarea
					v-model="persona"
					class="input-base resize-y leading-relaxed"
					rows="4"
					:placeholder="I18N.personaPlaceholder"
					@focus="personaField.focus"
					@input="personaField.touch"
					@blur="onPersonaChange"
				/>
			</AppField>

			<p
				v-if="errorMsg"
				class="px-3 py-2 rounded-sm text-sm text-danger-text bg-danger/12 border border-danger/35 font-500"
				role="alert"
			>{{ errorMsg }}</p>
		</div>
	</section>
</template>
