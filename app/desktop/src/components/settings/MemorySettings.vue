<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {feedback} from "../../services/feedback"
import {RUNTIME, type MemoryItem} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppChip from "../ui/AppChip.vue"
import AppField from "../ui/AppField.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"

const I18N = computed(() => useLanguages().views.main.memory)

const memories = ref<MemoryItem[]>([])
const searchKeyword = ref("")
const loading = ref(false)

// Embedding 配置 (秘密脱敏)
const embeddingModel = ref("BAAI/bge-m3")
const embeddingBaseUrl = ref("")
const embeddingApiKeyInput = ref("")
const embeddingDimensions = ref("")
const hasEmbeddingApiKey = ref(false)
const isReembedding = ref(false)
const reembedMessage = ref("")

// 新建记忆
const newContent = ref("")
const newImportance = ref(0.8)
const newTags = ref("")
const adding = ref(false)

let syncedEmbedding = false

// API Key 标签: 已保存时补一段加密提示
const API_KEY_LABEL = computed(() => {
	const SAVED = hasEmbeddingApiKey.value ? ` ${I18N.value.embedding.apiKeySaved}` : ""
	return `API Key${SAVED} ${I18N.value.embedding.apiKeyReuse}`
})

// 加载记忆列表
const loadMemories = async () => {
	loading.value = true
	try {
		if (searchKeyword.value.trim()) {
			memories.value = await RUNTIME.memorySearch(searchKeyword.value.trim(), 50)
		} else {
			memories.value = await RUNTIME.memoryList(50)
		}
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		loading.value = false
	}
}

const syncEmbeddingFromSnapshot = () => {
	const EMBEDDING = RUNTIME.snapshot.value?.embedding
	if (!EMBEDDING || syncedEmbedding) return
	syncedEmbedding = true
	embeddingModel.value = EMBEDDING.model || embeddingModel.value
	embeddingBaseUrl.value = EMBEDDING.baseUrl
	embeddingDimensions.value = EMBEDDING.dimensions
	hasEmbeddingApiKey.value = EMBEDDING.hasApiKey
}

onMounted(async () => {
	await RUNTIME.init()
	syncEmbeddingFromSnapshot()
	await loadMemories()
})

// 保存 Embedding 配置: 每个字段独立防抖 (400ms), 卸载时由 composable 负责 flush
const SAVE = useDebouncedSave({onError: (_key, error) => feedback.error(I18N.value.toast.saveFailed, error)})

// 保存维数: 留空表示用模型默认; 非正整数一律回退为空
const saveDimensions = () => {
	const RAW = embeddingDimensions.value.trim()
	if (RAW === "") {
		SAVE.save("dims", () => RUNTIME.updateEmbedding({dimensions: ""}))
		return
	}
	const NUM = Number.parseInt(RAW, 10)
	if (Number.isNaN(NUM) || NUM <= 0) {
		embeddingDimensions.value = ""
		SAVE.save("dims", () => RUNTIME.updateEmbedding({dimensions: ""}))
		return
	}
	embeddingDimensions.value = String(NUM)
	SAVE.save("dims", () => RUNTIME.updateEmbedding({dimensions: String(NUM)}))
}

// 重新计算向量嵌入
const reembedAll = async () => {
	if (isReembedding.value) return
	isReembedding.value = true
	reembedMessage.value = I18N.value.embedding.reembedRunning
	try {
		const COUNT = await RUNTIME.memoryReembed()
		reembedMessage.value = `${I18N.value.embedding.reembedDonePrefix}${COUNT}${I18N.value.embedding.reembedDoneSuffix}`
		await loadMemories()
	} catch (error) {
		reembedMessage.value = I18N.value.embedding.reembedFailed
		console.error("重新生成向量失败:", error)
	} finally {
		isReembedding.value = false
	}
}

// 添加记忆
const addMemory = async () => {
	if (!newContent.value.trim()) return
	adding.value = true
	try {
		await RUNTIME.memoryAdd(
			newContent.value.trim(),
			newImportance.value,
			newTags.value.trim() || undefined
		)
		newContent.value = ""
		newTags.value = ""
		await loadMemories()
	} catch (error) {
		feedback.error(I18N.value.toast.addFailed, error)
	} finally {
		adding.value = false
	}
}

// 删除记忆
const deleteMemory = async (id: number) => {
	try {
		await RUNTIME.memoryDelete(id)
		await loadMemories()
	} catch (error) {
		feedback.error(I18N.value.toast.deleteFailed, error)
	}
}

// 清空记忆
const clearAll = async () => {
	try {
		await RUNTIME.memoryClear()
		await loadMemories()
	} catch (error) {
		feedback.error(I18N.value.toast.clearFailed, error)
	}
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader
			:title="I18N.header.title"
			:subtitle="I18N.header.subtitle"
		/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. Embedding 向量嵌入配置 -->
			<AppCard :title="I18N.embedding.title" icon="sparkles">
				<template #actions>
					<n-button type="primary" :loading="isReembedding" :disabled="isReembedding" @click="reembedAll">
						<template #icon>
							<Icon :name="isReembedding ? 'loading' : 'sparkles'" :size="14"/>
						</template>
						{{ isReembedding ? I18N.embedding.indexing : I18N.embedding.reembed }}
					</n-button>
				</template>

				<div class="flex gap-3">
					<AppField :label="I18N.embedding.model" class="flex-1">
						<input
							v-model="embeddingModel"
							class="input-base"
							placeholder="BAAI/bge-m3, text-embedding-3-small..."
							@blur="SAVE.save('model', () => RUNTIME.updateEmbedding({model: embeddingModel.trim()}))"
						/>
					</AppField>
					<AppField :label="I18N.embedding.baseUrl" class="flex-1">
						<input
							v-model="embeddingBaseUrl"
							class="input-base"
							placeholder="https://api.openai.com/v1"
							@blur="SAVE.save('base', () => RUNTIME.updateEmbedding({baseUrl: embeddingBaseUrl.trim()}))"
						/>
					</AppField>
				</div>

				<div class="flex gap-3">
					<AppField :label="API_KEY_LABEL" class="flex-1">
						<input
							v-model="embeddingApiKeyInput"
							type="password"
							class="input-base"
							placeholder="sk-..."
							@blur="() => {
								const VALUE = embeddingApiKeyInput.trim()
								embeddingApiKeyInput = ''
								if (VALUE) SAVE.save('key', () => RUNTIME.updateEmbedding({apiKey: VALUE}))
							}"
						/>
					</AppField>
					<AppField :label="I18N.embedding.dimensions" class="w-[11rem] shrink-0">
						<input
							v-model="embeddingDimensions"
							type="number"
							min="1"
							class="input-base"
							:placeholder="I18N.embedding.dimensionsPlaceholder"
							@blur="saveDimensions"
						/>
					</AppField>
				</div>

				<p class="text-hint leading-relaxed">{{ I18N.embedding.dimensionsHint }}</p>

				<p v-if="reembedMessage" class="text-sm text-nori-teal-bright">{{ reembedMessage }}</p>
			</AppCard>

			<!-- 2. 新增记忆 -->
			<AppCard :title="I18N.add.title" icon="sparkles">
				<textarea
					v-model="newContent"
					class="input-base resize-y"
					rows="2"
					:placeholder="I18N.add.contentPlaceholder"
				/>

				<div class="flex items-center gap-3">
					<div class="flex-1">
						<input
							v-model="newTags"
							class="input-base"
							:placeholder="I18N.add.tagsPlaceholder"
						/>
					</div>

					<div class="flex items-center gap-2 shrink-0">
						<span class="field-label">{{ I18N.add.importance }}: {{ Math.round(newImportance * 100) }}%</span>
						<n-slider
							v-model:value="newImportance"
							:min="0.1"
							:max="1.0"
							:step="0.1"
							:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
							style="width: 12rem;"
						/>
					</div>

					<n-button type="primary" :disabled="!newContent.trim() || adding" :loading="adding" @click="addMemory">
						<template #icon>
							<Icon :name="adding ? 'loading' : 'check'" :size="14"/>
						</template>
						{{ I18N.add.submit }}
					</n-button>
				</div>
			</AppCard>

			<!-- 3. 记忆库列表与搜索 -->
			<AppCard :title="`${I18N.list.title} (${memories.length})`" icon="package">
				<template #actions>
					<n-popconfirm
						v-if="memories.length > 0"
						:positive-text="I18N.list.clearConfirm"
						:negative-text="I18N.common.cancel"
						@positive-click="clearAll"
					>
						<template #trigger>
							<button
								type="button"
								class="btn-base px-1 bg-transparent text-sm text-danger-text opacity-85
									hover:(opacity-100 underline)"
							>
								{{ I18N.list.clearAll }}
							</button>
						</template>
						{{ I18N.list.clearQuestion }}
					</n-popconfirm>
				</template>

				<div class="flex gap-2">
					<input
						v-model="searchKeyword"
						class="input-base flex-1"
						:placeholder="I18N.list.searchPlaceholder"
						@input="loadMemories"
					/>
				</div>

				<div class="flex flex-col gap-2 max-h-[28rem] scroll-area">
					<div v-if="memories.length === 0" class="py-4 text-center text-sm text-text-faint">
						{{ searchKeyword ? I18N.list.emptySearch : I18N.list.empty }}
					</div>

					<div
						v-for="item in memories"
						:key="item.id"
						class="flex items-start justify-between gap-3 px-3.5 py-2.5 rounded-sm bg-white/3
							border border-line-subtle transition-all duration-200
							hover:(bg-nori-teal-bright/4 border-line-strong)"
					>
						<div class="flex flex-1 flex-col gap-1.5 min-w-0">
							<div class="flex flex-wrap gap-1.5">
								<AppChip v-if="item.tags" tone="teal">{{ item.tags }}</AppChip>
								<AppChip>{{ item.source === "agent" ? I18N.list.sourceAgent : I18N.list.sourceManual }}</AppChip>
								<AppChip tone="warning">{{ I18N.add.importance }} {{ Math.round(item.importance * 100) }}%</AppChip>
							</div>
							<p class="text-base text-text-primary leading-normal">{{ item.content }}</p>
							<span class="text-xs text-text-faint">{{ new Date(item.createdAt).toLocaleString("zh-CN") }}</span>
						</div>
						<n-popconfirm
							:positive-text="I18N.list.delete"
							:negative-text="I18N.common.cancel"
							@positive-click="deleteMemory(item.id)"
						>
							<template #trigger>
								<button
									type="button"
									class="btn-base w-7 h-7 shrink-0 rounded-sm bg-white/6 text-text-muted
										hover:(bg-danger/18 text-danger-text)"
									:title="I18N.list.deleteThis"
									:aria-label="I18N.list.deleteThis"
								>
									<Icon name="close" :size="14"/>
								</button>
							</template>
							{{ I18N.list.deleteQuestion }}
						</n-popconfirm>
					</div>
				</div>
			</AppCard>
		</div>
	</div>
</template>
