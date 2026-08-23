<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {feedback} from "../../services/feedback"
import {RUNTIME, type MemoryAtom, type MemoryItem, type MemoryRecallDebug} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppChip from "../ui/AppChip.vue"
import AppField from "../ui/AppField.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"

const I18N = computed(() => useLanguages().views.main.memory)
const SNAPSHOT = computed(() => RUNTIME.snapshot.value)

type MemorySection = "overview" | "memories" | "atoms" | "knowledge" | "archive" | "debugger" | "advanced"
const CURRENT_SECTION = ref<MemorySection>("overview")
const memories = ref<MemoryItem[]>([])
const atoms = ref<MemoryAtom[]>([])
const knowledgeStatus = ref<{state?: string; processed?: number; total?: number; lastError?: string}>({})
const debugQuery = ref("")
const debugResult = ref<MemoryRecallDebug | null>(null)
const debugLoading = ref(false)
const knowledgeLoading = ref(false)
const searchKeyword = ref("")
const loading = ref(false)
const SECTIONS = computed(() => [
	{key: "overview" as MemorySection, label: I18N.value.tabs.overview},
	{key: "memories" as MemorySection, label: I18N.value.tabs.memories},
	{key: "atoms" as MemorySection, label: I18N.value.tabs.atoms},
	{key: "knowledge" as MemorySection, label: I18N.value.tabs.knowledge},
	{key: "archive" as MemorySection, label: I18N.value.tabs.archive},
	{key: "debugger" as MemorySection, label: I18N.value.tabs.debugger},
	{key: "advanced" as MemorySection, label: I18N.value.tabs.advanced},
])

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
		const STATUS = CURRENT_SECTION.value === "archive" ? "archived" : undefined
		const PAGE = await RUNTIME.memoryListPage(searchKeyword.value.trim() || undefined, undefined, STATUS, 50, 0)
		memories.value = PAGE.items
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

const loadAtoms = async () => {
	try {
		atoms.value = await RUNTIME.memoryAtoms(undefined, "active", 100, 0)
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	}
}

const loadKnowledgeStatus = async () => {
	try {
		knowledgeStatus.value = await RUNTIME.memoryKnowledgeStatus() as typeof knowledgeStatus.value
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	}
}

const toggleMemorySetting = async (key: "enabled" | "reflectionEnabled" | "decayEnabled" | "archiveEnabled") => {
	const MEMORY = RUNTIME.snapshot.value?.memory
	if (!MEMORY) return
	try {
		await RUNTIME.memoryUpdateSettings({[key]: !MEMORY[key]})
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.saveFailed, error)
	}
}

const reindexKnowledge = async () => {
	if (knowledgeLoading.value) return
	knowledgeLoading.value = true
	try {
		knowledgeStatus.value = await RUNTIME.memoryKnowledgeReindex() as typeof knowledgeStatus.value
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		knowledgeLoading.value = false
	}
}

const openKnowledge = async () => {
	try {
		await RUNTIME.memoryKnowledgeOpen()
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	}
}

const runDebugger = async () => {
	if (!debugQuery.value.trim() || debugLoading.value) return
	debugLoading.value = true
	try {
		debugResult.value = await RUNTIME.memoryRecallDebug(debugQuery.value.trim())
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		debugLoading.value = false
	}
}

watch(CURRENT_SECTION, async section => {
	if (section === "memories" || section === "archive") await loadMemories()
	if (section === "atoms") await loadAtoms()
	if (section === "knowledge") await loadKnowledgeStatus()
})

onMounted(async () => {
	await RUNTIME.init()
	syncEmbeddingFromSnapshot()
	await loadKnowledgeStatus()
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
		feedback.error(I18N.value.embedding.reembedFailed, error)
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

const restoreMemory = async (id: number) => {
	try {
		await RUNTIME.memoryRestore(id)
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

		<nav class="flex flex-wrap gap-1.5" :aria-label="I18N.header.title">
			<button
				v-for="section in SECTIONS"
				:key="section.key"
				type="button"
				:class="section.key === CURRENT_SECTION ? 'nav-item-active' : 'nav-item'"
				class="focus-ring"
				@click="CURRENT_SECTION = section.key"
			>
				{{ section.label }}
			</button>
		</nav>

		<div v-if="CURRENT_SECTION === 'overview'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.header.title" icon="package">
				<div class="grid grid-cols-2 gap-2.5 md:grid-cols-4">
					<div class="surface-card p-3"><p class="text-hint">{{ I18N.overview.active }}</p><p class="title-md">{{ SNAPSHOT?.memory?.active ?? 0 }}</p></div>
					<div class="surface-card p-3"><p class="text-hint">{{ I18N.overview.atoms }}</p><p class="title-md">{{ SNAPSHOT?.memory?.atoms ?? 0 }}</p></div>
					<div class="surface-card p-3"><p class="text-hint">{{ I18N.overview.archived }}</p><p class="title-md">{{ SNAPSHOT?.memory?.archived ?? 0 }}</p></div>
					<div class="surface-card p-3"><p class="text-hint">{{ I18N.overview.knowledge }}</p><p class="title-md">{{ SNAPSHOT?.memory?.knowledgeChunks ?? 0 }}</p></div>
				</div>
				<div class="grid grid-cols-2 gap-2.5 md:grid-cols-4">
					<button type="button" class="surface-card flex items-center justify-between p-3 text-left focus-ring" @click="toggleMemorySetting('enabled')"><span>{{ I18N.header.title }}</span><AppChip :tone="SNAPSHOT?.memory?.enabled ? 'success' : 'warning'">{{ SNAPSHOT?.memory?.enabled ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip></button>
					<button type="button" class="surface-card flex items-center justify-between p-3 text-left focus-ring" @click="toggleMemorySetting('reflectionEnabled')"><span>{{ I18N.overview.reflection }}</span><AppChip :tone="SNAPSHOT?.memory?.reflectionEnabled ? 'success' : 'warning'">{{ SNAPSHOT?.memory?.reflectionEnabled ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip></button>
					<button type="button" class="surface-card flex items-center justify-between p-3 text-left focus-ring" @click="toggleMemorySetting('decayEnabled')"><span>{{ I18N.overview.decay }}</span><AppChip :tone="SNAPSHOT?.memory?.decayEnabled ? 'success' : 'warning'">{{ SNAPSHOT?.memory?.decayEnabled ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip></button>
					<button type="button" class="surface-card flex items-center justify-between p-3 text-left focus-ring" @click="toggleMemorySetting('archiveEnabled')"><span>{{ I18N.overview.archive }}</span><AppChip :tone="SNAPSHOT?.memory?.archiveEnabled ? 'success' : 'warning'">{{ SNAPSHOT?.memory?.archiveEnabled ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip></button>
				</div>
				<p class="text-hint">{{ I18N.overview.index }}: {{ SNAPSHOT?.memory?.indexState }} ({{ SNAPSHOT?.memory?.indexProcessed ?? 0 }}/{{ SNAPSHOT?.memory?.indexTotal ?? 0 }})</p>
			</AppCard>
		</div>

		<div v-if="CURRENT_SECTION === 'atoms'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.atoms.title" icon="package">
				<div v-if="atoms.length === 0" class="py-4 text-center text-sm text-text-faint">{{ I18N.atoms.empty }}</div>
				<div v-for="atom in atoms" :key="atom.id" class="surface-card flex flex-col gap-1.5 p-3">
					<div class="flex flex-wrap gap-1.5"><AppChip tone="teal">{{ atom.atomType }}</AppChip><AppChip tone="warning">{{ Math.round(atom.importance * 100) }}%</AppChip></div>
					<p class="text-base text-text-primary">{{ atom.content }}</p>
					<span class="text-xs text-text-faint">{{ I18N.atoms.parent }} #{{ atom.parentMemoryId }}</span>
				</div>
			</AppCard>
		</div>

		<div v-if="CURRENT_SECTION === 'knowledge'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.knowledge.title" icon="package">
				<template #actions><div class="flex gap-2"><n-button secondary @click="openKnowledge">{{ I18N.knowledge.open }}</n-button><n-button type="primary" :loading="knowledgeLoading" @click="reindexKnowledge">{{ I18N.knowledge.reindex }}</n-button></div></template>
				<div class="flex flex-col gap-2 text-sm text-text-muted"><div class="flex justify-between gap-3"><span>{{ I18N.knowledge.path }}</span><span class="mono break-all text-right">{{ SNAPSHOT?.memory?.knowledgePath }}</span></div><div class="flex justify-between"><span>{{ I18N.knowledge.chunks }}</span><span>{{ knowledgeStatus.total ?? SNAPSHOT?.memory?.knowledgeChunks ?? 0 }}</span></div><div class="flex justify-between"><span>{{ I18N.knowledge.status }}</span><span>{{ knowledgeStatus.state ?? SNAPSHOT?.memory?.indexState }}</span></div></div>
				<p v-if="knowledgeStatus.lastError" class="text-sm text-danger-text">{{ knowledgeStatus.lastError }}</p>
			</AppCard>
		</div>

		<div v-if="CURRENT_SECTION === 'debugger'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.debugger.title" icon="terminal">
				<div class="flex gap-2"><input v-model="debugQuery" class="input-base flex-1" :placeholder="I18N.debugger.placeholder" @keyup.enter="runDebugger"/><n-button type="primary" :loading="debugLoading" @click="runDebugger">{{ I18N.debugger.run }}</n-button></div>
				<div v-if="debugResult" class="flex flex-col gap-2 text-sm"><div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.query }}</p><p class="mono whitespace-pre-wrap text-text-muted">{{ debugResult.trace?.expandedQuery }}</p></div><div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.injected }}</p><p v-for="id in (debugResult.trace?.injectedIds ?? [])" :key="id" class="text-text-primary">#{{ id }}</p></div></div>
				<div v-else class="py-4 text-center text-sm text-text-faint">{{ I18N.debugger.empty }}</div>
			</AppCard>
		</div>

		<div v-if="CURRENT_SECTION === 'memories' || CURRENT_SECTION === 'archive' || CURRENT_SECTION === 'advanced'" class="flex flex-col gap-3.5 pb-5">
			<!-- 1. Embedding 向量嵌入配置 -->
			<AppCard v-if="CURRENT_SECTION === 'advanced'" :title="I18N.embedding.title" icon="sparkles">
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
			<AppCard v-if="CURRENT_SECTION === 'memories'" :title="I18N.add.title" icon="sparkles">
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
			<AppCard v-if="CURRENT_SECTION === 'memories' || CURRENT_SECTION === 'archive'" :title="`${I18N.list.title} (${memories.length})`" icon="package">
				<template #actions>
					<n-popconfirm
						v-if="memories.length > 0 && CURRENT_SECTION === 'memories'"
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
						<button
							v-if="CURRENT_SECTION === 'archive'"
							type="button"
							class="btn-base w-7 h-7 shrink-0 rounded-sm bg-white/6 text-text-muted hover:(bg-nori-teal-bright/12 text-nori-teal-bright)"
							:title="I18N.archive.restore"
							:aria-label="I18N.archive.restore"
							@click="restoreMemory(item.id)"
						>
							<Icon name="refresh" :size="14"/>
						</button>
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
