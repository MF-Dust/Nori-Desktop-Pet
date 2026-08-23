<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import {feedback} from "../../services/feedback"
import {RUNTIME, type MemoryAtom, type MemoryItem, type MemoryRecallDebug, type MemorySource, type MemorySettings} from "../../services/runtime"
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
const memoryTotal = ref(0)
const memoryPage = ref(0)
const MEMORY_PAGE_SIZE = 20
const atoms = ref<MemoryAtom[]>([])
const knowledgeStatus = ref<{state?: string; processed?: number; total?: number; lastError?: string}>({})
const debugQuery = ref("")
const debugResult = ref<MemoryRecallDebug | null>(null)
const debugLoading = ref(false)
const knowledgeLoading = ref(false)
const searchKeyword = ref("")
const kindFilter = ref("")
const statusFilter = ref("")
const loading = ref(false)
const selectedMemory = ref<MemoryItem | null>(null)
const selectedAtoms = ref<MemoryAtom[]>([])
const selectedSources = ref<MemorySource[]>([])
const detailLoading = ref(false)
const savingDetail = ref(false)
const editContent = ref("")
const editCanonical = ref("")
const editPersona = ref("")
const editTags = ref("")
const editKind = ref("general")
const editImportance = ref(0.8)
const editConfidence = ref(0.8)
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
const embeddingModelField = useSnapshotField(snapshot => snapshot.embedding.model, "BAAI/bge-m3")
const embeddingBaseUrlField = useSnapshotField(snapshot => snapshot.embedding.baseUrl, "")
const embeddingDimensionsField = useSnapshotField(snapshot => snapshot.embedding.dimensions, "")
const embeddingModel = embeddingModelField.value
const embeddingBaseUrl = embeddingBaseUrlField.value
const embeddingDimensions = embeddingDimensionsField.value
const embeddingApiKeyInput = ref("")
const hasEmbeddingApiKey = computed(() => RUNTIME.snapshot.value?.embedding.hasApiKey ?? false)
const isReembedding = ref(false)
const reembedMessage = ref("")
const reflectionRoundsField = useSnapshotField(snapshot => snapshot.memory.reflectionRounds, 8)
const reflectionMinCharsField = useSnapshotField(snapshot => snapshot.memory.reflectionMinChars, 2500)
const recallTopKField = useSnapshotField(snapshot => snapshot.memory.recallTopK, 6)
const keywordTopKField = useSnapshotField(snapshot => snapshot.memory.keywordTopK, 20)
const vectorTopKField = useSnapshotField(snapshot => snapshot.memory.vectorTopK, 20)
const rrfKField = useSnapshotField(snapshot => snapshot.memory.rrfK, 60)
const minSimilarityField = useSnapshotField(snapshot => snapshot.memory.minSimilarity, 0.25)
const archiveThresholdField = useSnapshotField(snapshot => snapshot.memory.archiveThreshold, 0.15)
const knowledgeEnabledField = useSnapshotField(snapshot => snapshot.memory.knowledgeEnabled, true)
const knowledgeWatchField = useSnapshotField(snapshot => snapshot.memory.knowledgeWatch, true)
const debugRetrievalField = useSnapshotField(snapshot => snapshot.memory.debugRetrieval, false)

// 新建记忆
const newContent = ref("")
const newImportance = ref(0.8)
const newTags = ref("")
const newKind = ref("general")
const adding = ref(false)

// API Key 标签: 已保存时补一段加密提示
const API_KEY_LABEL = computed(() => {
	const SAVED = hasEmbeddingApiKey.value ? ` ${I18N.value.embedding.apiKeySaved}` : ""
	return `${I18N.value.embedding.apiKey}${SAVED} ${I18N.value.embedding.apiKeyReuse}`
})

// 加载记忆列表
const loadMemories = async () => {
	loading.value = true
	try {
		const STATUS = CURRENT_SECTION.value === "archive" ? "archived" : statusFilter.value || undefined
		const PAGE = await RUNTIME.memoryListPage(searchKeyword.value.trim() || undefined, kindFilter.value || undefined, STATUS, MEMORY_PAGE_SIZE, memoryPage.value * MEMORY_PAGE_SIZE)
		memories.value = PAGE.items
		memoryTotal.value = PAGE.total
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		loading.value = false
	}
}

const loadAtoms = async () => {
	try {
		atoms.value = await RUNTIME.memoryAtoms(undefined, "active", 100, 0)
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	}
}

const openMemory = async (id: number) => {
	detailLoading.value = true
	try {
		const DETAIL = await RUNTIME.memoryGet(id)
		selectedMemory.value = DETAIL.item
		selectedAtoms.value = DETAIL.atoms
		selectedSources.value = DETAIL.sources
		editContent.value = DETAIL.item.content
		editCanonical.value = DETAIL.item.canonicalSummary || DETAIL.item.content
		editPersona.value = DETAIL.item.personaSummary || DETAIL.item.content
		editTags.value = DETAIL.item.tags || ""
		editKind.value = DETAIL.item.kind || "general"
		editImportance.value = DETAIL.item.importance
		editConfidence.value = DETAIL.item.confidence ?? 0.8
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		detailLoading.value = false
	}
}

const closeMemory = () => {
	selectedMemory.value = null
	selectedAtoms.value = []
	selectedSources.value = []
}

const onModalUpdate = (show: boolean) => {
	if (!show) closeMemory()
}

const saveMemory = async () => {
	if (!selectedMemory.value || !editContent.value.trim() || savingDetail.value) return
	savingDetail.value = true
	try {
		await RUNTIME.memoryUpdate(selectedMemory.value.id, editContent.value.trim(), editImportance.value, editTags.value.trim(), {
			kind: editKind.value,
			canonicalSummary: editCanonical.value.trim(),
			personaSummary: editPersona.value.trim(),
			confidence: editConfidence.value,
		})
		await loadMemories()
		await openMemory(selectedMemory.value.id)
	} catch (error) {
		feedback.error(I18N.value.toast.saveFailed, error)
	} finally {
		savingDetail.value = false
	}
}

const archiveMemory = async (id: number) => {
	try {
		await RUNTIME.memoryArchive(id)
		closeMemory()
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.archiveFailed, error)
	}
}

const resetMemoryPage = async () => {
	memoryPage.value = 0
	await loadMemories()
}

const changeSection = (section: MemorySection) => {
	CURRENT_SECTION.value = section
	memoryPage.value = 0
	if (section === "memories") statusFilter.value = ""
	if (section === "archive") statusFilter.value = "archived"
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
	await loadKnowledgeStatus()
	await loadMemories()
})

// 保存 Embedding 配置: 每个字段独立防抖 (400ms), 卸载时由 composable 负责 flush
const SAVE = useDebouncedSave({onError: (_key, error) => feedback.error(I18N.value.toast.saveFailed, error)})

const numberValue = (event: Event) => Number((event.target as HTMLInputElement).value)

const saveMemorySetting = <T>(key: string, field: {touch: () => void; commit: () => void}, value: T) => {
	field.touch()
	SAVE.save(key, async () => {
		await RUNTIME.memoryUpdateSettings({[key]: value} as Partial<MemorySettings>)
		field.commit()
	})
}

const saveEmbedding = (key: string, field: {blur: () => void; commit: () => void}, task: () => Promise<void>) => {
	field.blur()
	SAVE.save(key, async () => {
		await task()
		field.commit()
	})
}

// 保存维数: 留空表示用模型默认; 非正整数一律回退为空
const saveEmbeddingModel = () => saveEmbedding("model", embeddingModelField, () => RUNTIME.updateEmbedding({model: embeddingModel.value.trim()}))
const saveEmbeddingBase = () => saveEmbedding("base", embeddingBaseUrlField, () => RUNTIME.updateEmbedding({baseUrl: embeddingBaseUrl.value.trim()}))

const saveDimensions = () => {
	embeddingDimensionsField.blur()
	const RAW = embeddingDimensions.value.trim()
	if (RAW === "") {
		SAVE.save("dims", async () => {
			await RUNTIME.updateEmbedding({dimensions: ""})
			embeddingDimensionsField.commit()
		})
		return
	}
	const NUM = Number.parseInt(RAW, 10)
	if (Number.isNaN(NUM) || NUM <= 0) {
		embeddingDimensions.value = ""
		SAVE.save("dims", async () => {
			await RUNTIME.updateEmbedding({dimensions: ""})
			embeddingDimensionsField.commit()
		})
		return
	}
	embeddingDimensions.value = String(NUM)
	SAVE.save("dims", async () => {
		await RUNTIME.updateEmbedding({dimensions: String(NUM)})
		embeddingDimensionsField.commit()
	})
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
			newTags.value.trim() || undefined,
			newKind.value
		)
		newContent.value = ""
		newTags.value = ""
		newKind.value = "general"
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
		closeMemory()
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.deleteFailed, error)
	}
}

const restoreMemory = async (id: number) => {
	try {
		await RUNTIME.memoryRestore(id)
		closeMemory()
		await loadMemories()
		await RUNTIME.refresh()
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
				@click="changeSection(section.key)"
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
				<div v-if="debugResult" class="flex flex-col gap-2 text-sm">
					<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.query }}</p><p class="mono whitespace-pre-wrap text-text-muted">{{ debugResult.trace?.expandedQuery }}</p></div>
					<div class="grid grid-cols-2 gap-2">
						<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.keyword }}</p><p v-for="hit in (debugResult.trace?.keywordHits ?? [])" :key="`k-${hit.memoryId}`">#{{ hit.memoryId }} · {{ hit.score.toFixed(4) }} · {{ hit.rank }}</p></div>
						<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.vector }}</p><p v-for="hit in (debugResult.trace?.vectorHits ?? [])" :key="`v-${hit.memoryId}`">#{{ hit.memoryId }} · {{ hit.score.toFixed(4) }} · {{ hit.rank }}</p></div>
						<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.atoms }}</p><p v-for="hit in (debugResult.trace?.atomHits ?? [])" :key="`a-${hit.memoryId}`">#{{ hit.memoryId }} · {{ hit.score.toFixed(4) }} · {{ hit.rank }}</p></div>
						<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.rrf }}</p><p v-for="hit in (debugResult.trace?.rrfHits ?? [])" :key="`r-${hit.memoryId}`">#{{ hit.memoryId }} · {{ hit.score.toFixed(4) }} · {{ hit.rank }}</p></div>
					</div>
					<div class="surface-card p-3"><p class="field-label">{{ I18N.debugger.injected }}</p><p v-for="item in debugResult.personal" :key="item.id" class="text-text-primary">#{{ item.id }} · {{ item.personaSummary || item.content }}</p></div>
					<div v-if="debugResult.trace?.filteredIds?.length" class="surface-card p-3"><p class="field-label">{{ I18N.debugger.filtered }}</p><p class="text-text-muted">{{ debugResult.trace.filteredIds.join(", ") }}</p></div>
					<div v-if="debugResult.knowledge?.length" class="surface-card p-3"><p class="field-label">{{ I18N.debugger.knowledge }}</p><p v-for="item in debugResult.knowledge" :key="item.id">{{ item.heading }} · {{ item.awareness }} · {{ item.score.toFixed(4) }}</p></div>
					<div v-if="debugResult.echoes?.length" class="surface-card p-3"><p class="field-label">{{ I18N.debugger.echoes }}</p><p v-for="item in debugResult.echoes" :key="item.content">{{ item.content }}</p></div>
				</div>
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
							:placeholder="I18N.embedding.modelPlaceholder"
							@focus="embeddingModelField.focus"
							@input="embeddingModelField.touch"
							@blur="saveEmbeddingModel"
						/>
					</AppField>
					<AppField :label="I18N.embedding.baseUrl" class="flex-1">
						<input
							v-model="embeddingBaseUrl"
							class="input-base"
							:placeholder="I18N.embedding.baseUrlPlaceholder"
							@focus="embeddingBaseUrlField.focus"
							@input="embeddingBaseUrlField.touch"
							@blur="saveEmbeddingBase"
						/>
					</AppField>
				</div>

				<div class="flex gap-3">
					<AppField :label="API_KEY_LABEL" class="flex-1">
						<input
							v-model="embeddingApiKeyInput"
							type="password"
							class="input-base"
							:placeholder="I18N.embedding.apiKeyPlaceholder"
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
							@focus="embeddingDimensionsField.focus"
							@input="embeddingDimensionsField.touch"
							@blur="saveDimensions"
						/>
					</AppField>
				</div>

				<p class="text-hint leading-relaxed">{{ I18N.embedding.dimensionsHint }}</p>

				<p v-if="reembedMessage" class="text-sm text-nori-teal-bright">{{ reembedMessage }}</p>
			</AppCard>

			<AppCard v-if="CURRENT_SECTION === 'advanced'" :title="I18N.advanced.title" icon="settings">
				<div class="grid grid-cols-2 gap-3 md:grid-cols-4">
					<AppField :label="I18N.advanced.reflectionRounds"><input :value="reflectionRoundsField.value" type="number" min="1" max="32" class="input-base" @focus="reflectionRoundsField.focus" @input="reflectionRoundsField.touch" @blur="saveMemorySetting('reflectionRounds', reflectionRoundsField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.reflectionMinChars"><input :value="reflectionMinCharsField.value" type="number" min="100" max="20000" class="input-base" @focus="reflectionMinCharsField.focus" @input="reflectionMinCharsField.touch" @blur="saveMemorySetting('reflectionMinChars', reflectionMinCharsField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.recallTopK"><input :value="recallTopKField.value" type="number" min="1" max="20" class="input-base" @focus="recallTopKField.focus" @input="recallTopKField.touch" @blur="saveMemorySetting('recallTopK', recallTopKField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.keywordTopK"><input :value="keywordTopKField.value" type="number" min="1" max="100" class="input-base" @focus="keywordTopKField.focus" @input="keywordTopKField.touch" @blur="saveMemorySetting('keywordTopK', keywordTopKField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.vectorTopK"><input :value="vectorTopKField.value" type="number" min="1" max="100" class="input-base" @focus="vectorTopKField.focus" @input="vectorTopKField.touch" @blur="saveMemorySetting('vectorTopK', vectorTopKField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.rrfK"><input :value="rrfKField.value" type="number" min="1" max="500" class="input-base" @focus="rrfKField.focus" @input="rrfKField.touch" @blur="saveMemorySetting('rrfK', rrfKField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.minSimilarity"><input :value="minSimilarityField.value" type="number" min="0" max="1" step="0.01" class="input-base" @focus="minSimilarityField.focus" @input="minSimilarityField.touch" @blur="saveMemorySetting('minSimilarity', minSimilarityField, numberValue($event))"/></AppField>
					<AppField :label="I18N.advanced.archiveThreshold"><input :value="archiveThresholdField.value" type="number" min="0" max="1" step="0.01" class="input-base" @focus="archiveThresholdField.focus" @input="archiveThresholdField.touch" @blur="saveMemorySetting('archiveThreshold', archiveThresholdField, numberValue($event))"/></AppField>
				</div>
				<div class="grid grid-cols-2 gap-2 md:grid-cols-4">
					<AppChip :tone="knowledgeEnabledField.value ? 'success' : 'warning'" class="cursor-pointer" @click="saveMemorySetting('knowledgeEnabled', knowledgeEnabledField, !knowledgeEnabledField.value)">{{ I18N.advanced.knowledge }}: {{ knowledgeEnabledField.value ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
					<AppChip :tone="knowledgeWatchField.value ? 'success' : 'warning'" class="cursor-pointer" @click="saveMemorySetting('knowledgeWatch', knowledgeWatchField, !knowledgeWatchField.value)">{{ I18N.advanced.watch }}: {{ knowledgeWatchField.value ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
					<AppChip :tone="debugRetrievalField.value ? 'success' : 'warning'" class="cursor-pointer" @click="saveMemorySetting('debugRetrieval', debugRetrievalField, !debugRetrievalField.value)">{{ I18N.advanced.debug }}: {{ debugRetrievalField.value ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
				</div>
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
					<select v-model="newKind" class="input-base w-[10rem]" aria-label="memory kind">
						<option value="general">{{ I18N.add.kindGeneral }}</option>
						<option value="factual">{{ I18N.add.kindFactual }}</option>
						<option value="preference">{{ I18N.add.kindPreference }}</option>
						<option value="relational">{{ I18N.add.kindRelational }}</option>
						<option value="planned">{{ I18N.add.kindPlanned }}</option>
						<option value="identity">{{ I18N.add.kindIdentity }}</option>
					</select>
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
							class="w-[12rem]"
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
			<AppCard v-if="CURRENT_SECTION === 'memories' || CURRENT_SECTION === 'archive'" :title="`${I18N.list.title} (${memoryTotal})`" icon="package">
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

				<div class="flex flex-wrap gap-2">
					<input
						v-model="searchKeyword"
						class="input-base min-w-[12rem] flex-1"
						:placeholder="I18N.list.searchPlaceholder"
						@input="resetMemoryPage"
					/>
					<select v-model="kindFilter" class="input-base w-[10rem]" :aria-label="I18N.list.kind" @change="resetMemoryPage">
						<option value="">{{ I18N.list.allKinds }}</option>
						<option value="general">{{ I18N.add.kindGeneral }}</option>
						<option value="factual">{{ I18N.add.kindFactual }}</option>
						<option value="preference">{{ I18N.add.kindPreference }}</option>
						<option value="relational">{{ I18N.add.kindRelational }}</option>
						<option value="planned">{{ I18N.add.kindPlanned }}</option>
						<option value="identity">{{ I18N.add.kindIdentity }}</option>
					</select>
					<select v-if="CURRENT_SECTION === 'memories'" v-model="statusFilter" class="input-base w-[10rem]" :aria-label="I18N.list.status" @change="resetMemoryPage">
						<option value="">{{ I18N.list.allStatuses }}</option>
						<option value="active">{{ I18N.list.active }}</option>
						<option value="dormant">{{ I18N.list.dormant }}</option>
						<option value="expired">{{ I18N.list.expired }}</option>
					</select>
				</div>

				<div class="flex flex-col gap-2 max-h-[28rem] scroll-area">
					<div v-if="memories.length === 0" class="py-4 text-center text-sm text-text-faint">
						{{ searchKeyword ? I18N.list.emptySearch : I18N.list.empty }}
					</div>

					<div
						v-for="item in memories"
						:key="item.id"
						class="flex items-start justify-between gap-3 px-3.5 py-2.5 rounded-sm bg-white/3 cursor-pointer
							border border-line-subtle transition-all duration-200
							hover:(bg-nori-teal-bright/4 border-line-strong)"
						@click="openMemory(item.id)"
					>
						<div class="flex flex-1 flex-col gap-1.5 min-w-0">
							<div class="flex flex-wrap gap-1.5">
								<AppChip v-if="item.tags" tone="teal">{{ item.tags }}</AppChip>
								<AppChip>{{ item.source === "agent" ? I18N.list.sourceAgent : I18N.list.sourceManual }}</AppChip>
								<AppChip>{{ item.kind || "general" }}</AppChip>
								<AppChip tone="warning">{{ I18N.add.importance }} {{ Math.round(item.importance * 100) }}%</AppChip>
								<AppChip tone="teal">{{ item.status || "active" }}</AppChip>
							</div>
							<p class="text-base text-text-primary leading-normal">{{ item.content }}</p>
							<span class="text-xs text-text-faint">{{ new Date(item.createdAt).toLocaleString() }}</span>
						</div>
						<button
							v-if="CURRENT_SECTION === 'memories' && item.status === 'active'"
							type="button"
							class="btn-base w-7 h-7 shrink-0 rounded-sm bg-white/6 text-text-muted hover:(bg-nori-teal-bright/12 text-nori-teal-bright)"
							:title="I18N.list.archiveThis"
							:aria-label="I18N.list.archiveThis"
							@click.stop="archiveMemory(item.id)"
						>
							<Icon name="package" :size="14"/>
						</button>
						<button
							v-if="CURRENT_SECTION === 'archive'"
							type="button"
							class="btn-base w-7 h-7 shrink-0 rounded-sm bg-white/6 text-text-muted hover:(bg-nori-teal-bright/12 text-nori-teal-bright)"
							:title="I18N.archive.restore"
							:aria-label="I18N.archive.restore"
							@click.stop="restoreMemory(item.id)"
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
				<div v-if="memoryTotal > MEMORY_PAGE_SIZE" class="flex items-center justify-between pt-2 text-sm text-text-muted">
					<button type="button" class="btn-base" :disabled="memoryPage === 0" @click="memoryPage--; loadMemories()">{{ I18N.list.previous }}</button>
					<span>{{ memoryPage + 1 }} / {{ Math.ceil(memoryTotal / MEMORY_PAGE_SIZE) }}</span>
					<button type="button" class="btn-base" :disabled="(memoryPage + 1) * MEMORY_PAGE_SIZE >= memoryTotal" @click="memoryPage++; loadMemories()">{{ I18N.list.next }}</button>
				</div>
			</AppCard>
		</div>

		<n-modal :show="selectedMemory !== null" :mask-closable="false" @update:show="onModalUpdate">
			<AppCard v-if="selectedMemory" :title="`${I18N.detail.title} #${selectedMemory.id}`" icon="package">
				<div v-if="detailLoading" class="py-4 text-center text-text-faint">{{ I18N.detail.loading }}</div>
				<div v-else class="flex max-h-[70vh] flex-col gap-3 overflow-auto">
					<AppField :label="I18N.detail.content"><textarea v-model="editContent" class="input-base resize-y" rows="3"/></AppField>
					<div class="grid grid-cols-2 gap-3">
						<AppField :label="I18N.detail.canonical"><textarea v-model="editCanonical" class="input-base resize-y" rows="2"/></AppField>
						<AppField :label="I18N.detail.persona"><textarea v-model="editPersona" class="input-base resize-y" rows="2"/></AppField>
					</div>
					<div class="grid grid-cols-2 gap-3">
						<AppField :label="I18N.detail.kind"><select v-model="editKind" class="input-base"><option value="general">{{ I18N.add.kindGeneral }}</option><option value="factual">{{ I18N.add.kindFactual }}</option><option value="preference">{{ I18N.add.kindPreference }}</option><option value="relational">{{ I18N.add.kindRelational }}</option><option value="planned">{{ I18N.add.kindPlanned }}</option><option value="identity">{{ I18N.add.kindIdentity }}</option></select></AppField>
						<AppField :label="I18N.detail.tags"><input v-model="editTags" class="input-base"/></AppField>
						<AppField :label="I18N.detail.confidence"><input v-model.number="editConfidence" type="number" min="0" max="1" step="0.05" class="input-base"/></AppField>
						<AppField :label="I18N.add.importance"><input v-model.number="editImportance" type="number" min="0" max="1" step="0.05" class="input-base"/></AppField>
					</div>
					<div class="surface-card p-3"><p class="field-label">{{ I18N.detail.atoms }}</p><p v-for="atom in selectedAtoms" :key="atom.id" class="text-sm">#{{ atom.id }} · {{ atom.content }}</p><p v-if="selectedAtoms.length === 0" class="text-hint">{{ I18N.detail.empty }}</p></div>
					<div class="surface-card p-3"><p class="field-label">{{ I18N.detail.sources }}</p><p v-for="source in selectedSources" :key="source.id" class="text-sm">{{ source.role }} · {{ source.content }}</p><p v-if="selectedSources.length === 0" class="text-hint">{{ I18N.detail.empty }}</p></div>
					<div class="flex justify-end gap-2">
						<n-button secondary @click="closeMemory">{{ I18N.common.cancel }}</n-button>
						<n-button v-if="selectedMemory.status === 'active'" secondary @click="archiveMemory(selectedMemory.id)">{{ I18N.list.archiveThis }}</n-button>
						<n-button type="primary" :loading="savingDetail" @click="saveMemory">{{ I18N.detail.save }}</n-button>
					</div>
				</div>
			</AppCard>
		</n-modal>
	</div>
</template>
