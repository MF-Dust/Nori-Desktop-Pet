<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {feedback} from "../../services/feedback"
import {RUNTIME, type MemoryAtom, type MemoryItem, type MemoryRecallDebug, type MemorySource} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppChip from "../ui/AppChip.vue"
import AppField from "../ui/AppField.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppButton from "../ui/AppButton.vue"
import AppModal from "../ui/AppModal.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

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

const KIND_OPTIONS = computed(() => [
	{label: I18N.value.add.kindGeneral, value: "general"},
	{label: I18N.value.add.kindFactual, value: "factual"},
	{label: I18N.value.add.kindPreference, value: "preference"},
	{label: I18N.value.add.kindRelational, value: "relational"},
	{label: I18N.value.add.kindPlanned, value: "planned"},
	{label: I18N.value.add.kindIdentity, value: "identity"},
])

const KIND_FILTER_OPTIONS = computed(() => [
	{label: I18N.value.list.allKinds, value: ""},
	...KIND_OPTIONS.value,
])

const STATUS_FILTER_OPTIONS = computed(() => [
	{label: I18N.value.list.allStatuses, value: ""},
	{label: I18N.value.list.active, value: "active"},
	{label: I18N.value.list.dormant, value: "dormant"},
	{label: I18N.value.list.expired, value: "expired"},
])

const SECTIONS = computed(() => [
	{key: "overview" as MemorySection, label: I18N.value.tabs.overview},
	{key: "memories" as MemorySection, label: I18N.value.tabs.memories},
	{key: "atoms" as MemorySection, label: I18N.value.tabs.atoms},
	{key: "knowledge" as MemorySection, label: I18N.value.tabs.knowledge},
	{key: "archive" as MemorySection, label: I18N.value.tabs.archive},
	{key: "debugger" as MemorySection, label: I18N.value.tabs.debugger},
	{key: "advanced" as MemorySection, label: I18N.value.tabs.advanced},
])

// 记忆设置防抖与状态管理
const SAVE_MGR = useSnapshotSave({
	onError: (_key, error) => feedback.error(I18N.value.toast.saveFailed, error),
})
const {defineField} = SAVE_MGR

const isReembedding = ref(false)
const reembedMessage = ref("")

const reflectionRoundsField = defineField(
	"reflectionRounds",
	snapshot => snapshot.memory.reflectionRounds,
	8,
	async val => {
		await RUNTIME.memoryUpdateSettings({reflectionRounds: val})
	},
)
const reflectionMinCharsField = defineField(
	"reflectionMinChars",
	snapshot => snapshot.memory.reflectionMinChars,
	2500,
	async val => {
		await RUNTIME.memoryUpdateSettings({reflectionMinChars: val})
	},
)
const recallTopKField = defineField(
	"recallTopK",
	snapshot => snapshot.memory.recallTopK,
	6,
	async val => {
		await RUNTIME.memoryUpdateSettings({recallTopK: val})
	},
)
const keywordTopKField = defineField(
	"keywordTopK",
	snapshot => snapshot.memory.keywordTopK,
	20,
	async val => {
		await RUNTIME.memoryUpdateSettings({keywordTopK: val})
	},
)
const vectorTopKField = defineField(
	"vectorTopK",
	snapshot => snapshot.memory.vectorTopK,
	20,
	async val => {
		await RUNTIME.memoryUpdateSettings({vectorTopK: val})
	},
)
const rrfKField = defineField(
	"rrfK",
	snapshot => snapshot.memory.rrfK,
	60,
	async val => {
		await RUNTIME.memoryUpdateSettings({rrfK: val})
	},
)
const minSimilarityField = defineField(
	"minSimilarity",
	snapshot => snapshot.memory.minSimilarity,
	0.25,
	async val => {
		await RUNTIME.memoryUpdateSettings({minSimilarity: val})
	},
)
const archiveThresholdField = defineField(
	"archiveThreshold",
	snapshot => snapshot.memory.archiveThreshold,
	0.15,
	async val => {
		await RUNTIME.memoryUpdateSettings({archiveThreshold: val})
	},
)
const knowledgeEnabledField = defineField(
	"knowledgeEnabled",
	snapshot => snapshot.memory.knowledgeEnabled,
	true,
	async val => {
		await RUNTIME.memoryUpdateSettings({knowledgeEnabled: val})
	},
)
const knowledgeWatchField = defineField(
	"knowledgeWatch",
	snapshot => snapshot.memory.knowledgeWatch,
	true,
	async val => {
		await RUNTIME.memoryUpdateSettings({knowledgeWatch: val})
	},
)
const debugRetrievalField = defineField(
	"debugRetrieval",
	snapshot => snapshot.memory.debugRetrieval,
	false,
	async val => {
		await RUNTIME.memoryUpdateSettings({debugRetrieval: val})
	},
)

const reflectionRounds = reflectionRoundsField.value
const reflectionMinChars = reflectionMinCharsField.value
const recallTopK = recallTopKField.value
const keywordTopK = keywordTopKField.value
const vectorTopK = vectorTopKField.value
const rrfK = rrfKField.value
const minSimilarity = minSimilarityField.value
const archiveThreshold = archiveThresholdField.value
const knowledgeEnabled = knowledgeEnabledField.value
const knowledgeWatch = knowledgeWatchField.value
const debugRetrieval = debugRetrievalField.value

// 新建记忆
const newContent = ref("")
const newImportance = ref(0.8)
const newTags = ref("")
const newKind = ref("general")
const adding = ref(false)

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

const updateNumberField = (field: {value: {value: number}; touch: () => void}, event: Event): void => {
	field.value.value = Number((event.target as HTMLInputElement).value)
	field.touch()
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

		<!-- 导航分段 -->
		<div class="flex flex-wrap gap-2">
			<button
				v-for="s in SECTIONS"
				:key="s.key"
				type="button"
				class="px-3.5 py-1.5 rounded-pill text-sm font-500 transition-all duration-200"
				:class="CURRENT_SECTION === s.key ? 'bg-nori-teal-bright/18 text-nori-teal-bright border border-nori-teal-bright/40 shadow-[0_0.2rem_1rem_var(--glow-teal-soft)]' : 'bg-white/4 text-text-muted hover:(bg-white/8 text-text-primary)'"
				@click="changeSection(s.key)"
			>
				{{ s.label }}
			</button>
		</div>

		<!-- 各分段视图 -->
		<div v-if="CURRENT_SECTION === 'overview'" class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 记忆资产概览 -->
			<AppCard :title="I18N.overview.active" icon="sparkles">
				<div class="grid grid-cols-2 gap-3 md:grid-cols-4">
					<div class="surface-card flex flex-col gap-1 p-3.5 rounded-md border border-line-subtle bg-white/3">
						<span class="text-xs text-text-muted font-500">{{ I18N.overview.active }}</span>
						<span class="text-2xl font-700 text-nori-teal-bright mono">{{ SNAPSHOT?.memory?.active ?? 0 }}</span>
					</div>
					<div class="surface-card flex flex-col gap-1 p-3.5 rounded-md border border-line-subtle bg-white/3">
						<span class="text-xs text-text-muted font-500">{{ I18N.overview.atoms }}</span>
						<span class="text-2xl font-700 text-nori-teal-bright mono">{{ SNAPSHOT?.memory?.atoms ?? 0 }}</span>
					</div>
					<div class="surface-card flex flex-col gap-1 p-3.5 rounded-md border border-line-subtle bg-white/3">
						<span class="text-xs text-text-muted font-500">{{ I18N.overview.archived }}</span>
						<span class="text-2xl font-700 text-text-body mono">{{ SNAPSHOT?.memory?.archived ?? 0 }}</span>
					</div>
					<div class="surface-card flex flex-col gap-1 p-3.5 rounded-md border border-line-subtle bg-white/3">
						<span class="text-xs text-text-muted font-500">{{ I18N.overview.knowledge }}</span>
						<span class="text-2xl font-700 text-nori-teal-soft mono">{{ SNAPSHOT?.memory?.knowledgeChunks ?? 0 }}</span>
					</div>
				</div>

				<div class="flex items-center gap-2 pt-2 border-t border-line-subtle text-xs text-text-muted">
					<Icon name="info" :size="13" class="text-nori-teal-soft shrink-0"/>
					<span>{{ I18N.overview.index }}: {{ SNAPSHOT?.memory?.indexState }} ({{ SNAPSHOT?.memory?.indexProcessed ?? 0 }} / {{ SNAPSHOT?.memory?.indexTotal ?? 0 }})</span>
				</div>
			</AppCard>

			<!-- 2. 核心记忆机制开关 -->
			<AppCard :title="I18N.header.title" icon="settings">
				<AppSwitchRow :title="I18N.header.title" :desc="SNAPSHOT?.memory?.enabled ? I18N.overview.enabled : I18N.overview.disabled">
					<n-switch :value="Boolean(SNAPSHOT?.memory?.enabled)" @update:value="() => toggleMemorySetting('enabled')"/>
				</AppSwitchRow>

				<AppSwitchRow :title="I18N.overview.reflection" :desc="SNAPSHOT?.memory?.reflectionEnabled ? I18N.overview.enabled : I18N.overview.disabled">
					<n-switch :value="Boolean(SNAPSHOT?.memory?.reflectionEnabled)" @update:value="() => toggleMemorySetting('reflectionEnabled')"/>
				</AppSwitchRow>

				<AppSwitchRow :title="I18N.overview.decay" :desc="SNAPSHOT?.memory?.decayEnabled ? I18N.overview.enabled : I18N.overview.disabled">
					<n-switch :value="Boolean(SNAPSHOT?.memory?.decayEnabled)" @update:value="() => toggleMemorySetting('decayEnabled')"/>
				</AppSwitchRow>

				<AppSwitchRow :title="I18N.overview.archive" :desc="SNAPSHOT?.memory?.archiveEnabled ? I18N.overview.enabled : I18N.overview.disabled">
					<n-switch :value="Boolean(SNAPSHOT?.memory?.archiveEnabled)" @update:value="() => toggleMemorySetting('archiveEnabled')"/>
				</AppSwitchRow>
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
				<template #actions>
					<div class="flex gap-2">
						<AppButton size="sm" @click="openKnowledge">{{ I18N.knowledge.open }}</AppButton>
						<AppButton variant="primary" size="sm" :loading="knowledgeLoading" @click="reindexKnowledge">{{ I18N.knowledge.reindex }}</AppButton>
					</div>
				</template>
				<div class="flex flex-col gap-2 text-sm text-text-muted"><div class="flex justify-between gap-3"><span>{{ I18N.knowledge.path }}</span><span class="mono break-all text-right">{{ SNAPSHOT?.memory?.knowledgePath }}</span></div><div class="flex justify-between"><span>{{ I18N.knowledge.chunks }}</span><span>{{ knowledgeStatus.total ?? SNAPSHOT?.memory?.knowledgeChunks ?? 0 }}</span></div><div class="flex justify-between"><span>{{ I18N.knowledge.status }}</span><span>{{ knowledgeStatus.state ?? SNAPSHOT?.memory?.indexState }}</span></div></div>
				<p v-if="knowledgeStatus.lastError" class="text-sm text-danger-text">{{ knowledgeStatus.lastError }}</p>
			</AppCard>
		</div>

		<div v-if="CURRENT_SECTION === 'debugger'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.debugger.title" icon="terminal">
				<div class="flex gap-2">
					<input v-model="debugQuery" class="input-base flex-1" :placeholder="I18N.debugger.placeholder" @keyup.enter="runDebugger"/>
					<AppButton variant="primary" size="sm" :loading="debugLoading" @click="runDebugger">{{ I18N.debugger.run }}</AppButton>
				</div>
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
			<!-- 1. 向量索引重建 -->
			<AppCard v-if="CURRENT_SECTION === 'advanced'" :title="I18N.embedding.vectorRebuild" icon="sparkles">
				<template #actions>
					<AppButton variant="primary" size="sm" :loading="isReembedding" :disabled="isReembedding" @click="reembedAll">
						<template #icon>
							<Icon :name="isReembedding ? 'loading' : 'sparkles'" :size="14"/>
						</template>
						{{ isReembedding ? I18N.embedding.indexing : I18N.embedding.reembed }}
					</AppButton>
				</template>

				<p class="text-sm text-text-muted leading-relaxed">{{ I18N.embedding.vectorRebuildDesc }}</p>

				<p v-if="reembedMessage" class="text-sm text-nori-teal-bright" role="status">{{ reembedMessage }}</p>
			</AppCard>

			<!-- 2. 高级检索与记忆衰减配置 -->
			<AppCard v-if="CURRENT_SECTION === 'advanced'" :title="I18N.advanced.title" icon="settings">
				<div class="grid grid-cols-2 gap-3 md:grid-cols-4">
					<AppField :label="I18N.advanced.reflectionRounds" :state="reflectionRoundsField.state.value" :error="reflectionRoundsField.error.value">
						<input :value="reflectionRounds" type="number" min="1" max="32" class="input-base" @focus="reflectionRoundsField.focus" @input="updateNumberField(reflectionRoundsField, $event)" @blur="reflectionRoundsField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.reflectionMinChars" :state="reflectionMinCharsField.state.value" :error="reflectionMinCharsField.error.value">
						<input :value="reflectionMinChars" type="number" min="100" max="20000" class="input-base" @focus="reflectionMinCharsField.focus" @input="updateNumberField(reflectionMinCharsField, $event)" @blur="reflectionMinCharsField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.recallTopK" :state="recallTopKField.state.value" :error="recallTopKField.error.value">
						<input :value="recallTopK" type="number" min="1" max="20" class="input-base" @focus="recallTopKField.focus" @input="updateNumberField(recallTopKField, $event)" @blur="recallTopKField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.keywordTopK" :state="keywordTopKField.state.value" :error="keywordTopKField.error.value">
						<input :value="keywordTopK" type="number" min="1" max="100" class="input-base" @focus="keywordTopKField.focus" @input="updateNumberField(keywordTopKField, $event)" @blur="keywordTopKField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.vectorTopK" :state="vectorTopKField.state.value" :error="vectorTopKField.error.value">
						<input :value="vectorTopK" type="number" min="1" max="100" class="input-base" @focus="vectorTopKField.focus" @input="updateNumberField(vectorTopKField, $event)" @blur="vectorTopKField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.rrfK" :state="rrfKField.state.value" :error="rrfKField.error.value">
						<input :value="rrfK" type="number" min="1" max="500" class="input-base" @focus="rrfKField.focus" @input="updateNumberField(rrfKField, $event)" @blur="rrfKField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.minSimilarity" :state="minSimilarityField.state.value" :error="minSimilarityField.error.value">
						<input :value="minSimilarity" type="number" min="0" max="1" step="0.01" class="input-base" @focus="minSimilarityField.focus" @input="updateNumberField(minSimilarityField, $event)" @blur="minSimilarityField.save()"/>
					</AppField>
					<AppField :label="I18N.advanced.archiveThreshold" :state="archiveThresholdField.state.value" :error="archiveThresholdField.error.value">
						<input :value="archiveThreshold" type="number" min="0" max="1" step="0.01" class="input-base" @focus="archiveThresholdField.focus" @input="updateNumberField(archiveThresholdField, $event)" @blur="archiveThresholdField.save()"/>
					</AppField>
				</div>
				<div class="grid grid-cols-2 gap-2 md:grid-cols-4">
					<AppChip :tone="knowledgeEnabled ? 'success' : 'warning'" class="cursor-pointer" @click="() => { knowledgeEnabled = !knowledgeEnabled; knowledgeEnabledField.saveNow() }">{{ I18N.advanced.knowledge }}: {{ knowledgeEnabled ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
					<AppChip :tone="knowledgeWatch ? 'success' : 'warning'" class="cursor-pointer" @click="() => { knowledgeWatch = !knowledgeWatch; knowledgeWatchField.saveNow() }">{{ I18N.advanced.watch }}: {{ knowledgeWatch ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
					<AppChip :tone="debugRetrieval ? 'success' : 'warning'" class="cursor-pointer" @click="() => { debugRetrieval = !debugRetrieval; debugRetrievalField.saveNow() }">{{ I18N.advanced.debug }}: {{ debugRetrieval ? I18N.overview.enabled : I18N.overview.disabled }}</AppChip>
				</div>
			</AppCard>

			<!-- 3. 新增记忆 -->
			<AppCard v-if="CURRENT_SECTION === 'memories'" :title="I18N.add.title" icon="sparkles">
				<textarea
					v-model="newContent"
					class="input-base resize-y"
					rows="2"
					:placeholder="I18N.add.contentPlaceholder"
				/>

				<div class="flex items-center gap-3">
					<n-select v-model:value="newKind" :options="KIND_OPTIONS" class="w-[12rem] shrink-0"/>
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

					<AppButton variant="primary" size="sm" :disabled="!newContent.trim() || adding" :loading="adding" @click="addMemory">
						<template #icon>
							<Icon :name="adding ? 'loading' : 'check'" :size="14"/>
						</template>
						{{ I18N.add.submit }}
					</AppButton>
				</div>
			</AppCard>

			<!-- 4. 记忆库列表与搜索 -->
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
					<n-select v-model:value="kindFilter" :options="KIND_FILTER_OPTIONS" class="w-[12rem] shrink-0" @update:value="resetMemoryPage"/>
					<n-select v-if="CURRENT_SECTION === 'memories'" v-model:value="statusFilter" :options="STATUS_FILTER_OPTIONS" class="w-[12rem] shrink-0" @update:value="resetMemoryPage"/>
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

		<AppModal
			:show="selectedMemory !== null"
			:title="selectedMemory ? `${I18N.detail.title} #${selectedMemory.id}` : ''"
			:close-label="I18N.common.cancel"
			:mask-closable="false"
			panel-class="w-[min(56rem,94vw)] max-h-[86vh]"
			@close="closeMemory"
		>
			<div v-if="detailLoading" class="py-4 text-center text-text-faint">{{ I18N.detail.loading }}</div>
			<div v-else-if="selectedMemory" class="flex flex-col gap-3">
				<AppField :label="I18N.detail.content"><textarea v-model="editContent" class="input-base resize-y" rows="3"/></AppField>
				<div class="grid grid-cols-2 gap-3">
					<AppField :label="I18N.detail.canonical"><textarea v-model="editCanonical" class="input-base resize-y" rows="2"/></AppField>
					<AppField :label="I18N.detail.persona"><textarea v-model="editPersona" class="input-base resize-y" rows="2"/></AppField>
				</div>
				<div class="grid grid-cols-2 gap-3">
					<AppField :label="I18N.detail.kind">
						<n-select v-model:value="editKind" :options="KIND_OPTIONS"/>
					</AppField>
					<AppField :label="I18N.detail.tags"><input v-model="editTags" class="input-base"/></AppField>
					<AppField :label="I18N.detail.confidence"><input v-model.number="editConfidence" type="number" min="0" max="1" step="0.05" class="input-base"/></AppField>
					<AppField :label="I18N.add.importance"><input v-model.number="editImportance" type="number" min="0" max="1" step="0.05" class="input-base"/></AppField>
				</div>
				<div class="surface-card p-3"><p class="field-label">{{ I18N.detail.atoms }}</p><p v-for="atom in selectedAtoms" :key="atom.id" class="text-sm">#{{ atom.id }} · {{ atom.content }}</p><p v-if="selectedAtoms.length === 0" class="text-hint">{{ I18N.detail.empty }}</p></div>
				<div class="surface-card p-3"><p class="field-label">{{ I18N.detail.sources }}</p><p v-for="source in selectedSources" :key="source.id" class="text-sm">{{ source.role }} · {{ source.content }}</p><p v-if="selectedSources.length === 0" class="text-hint">{{ I18N.detail.empty }}</p></div>
			</div>
			<template #footer>
				<AppButton @click="closeMemory">{{ I18N.common.cancel }}</AppButton>
				<AppButton v-if="selectedMemory?.status === 'active'" variant="danger" @click="selectedMemory && archiveMemory(selectedMemory.id)">{{ I18N.list.archiveThis }}</AppButton>
				<AppButton variant="primary" :loading="savingDetail" @click="saveMemory">{{ I18N.detail.save }}</AppButton>
			</template>
		</AppModal>
	</div>
</template>
