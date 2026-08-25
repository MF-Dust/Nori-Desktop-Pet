<script setup lang="ts">
import {computed, onBeforeUnmount, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {feedback} from "../../services/feedback"
import {
	RUNTIME,
	type MemoryAtom,
	type MemoryExportResult,
	type MemoryImportConflictStrategy,
	type MemoryImportPreviewResult,
	type MemoryItem,
	type MemoryRecallDebug,
	type MemorySource,
} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppConfirm from "../ui/AppConfirm.vue"
import AppSearchField from "../ui/AppSearchField.vue"
import AppChip from "../ui/AppChip.vue"
import AppField from "../ui/AppField.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppButton from "../ui/AppButton.vue"
import AppModal from "../ui/AppModal.vue"
import AppSegmented, {type SegmentItem} from "../ui/AppSegmented.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"
import AppStatTile from "../ui/AppStatTile.vue"
import AppEmpty from "../ui/AppEmpty.vue"

const I18N = computed(() => useLanguages().views.main.memory)
const UI_I18N = computed(() => useLanguages().components.ui.state)
const SNAPSHOT = computed(() => RUNTIME.snapshot.value)

type MemorySection = "overview" | "memories" | "atoms" | "knowledge" | "archive" | "transfer" | "debugger" | "advanced"
const CURRENT_SECTION = ref<MemorySection>("overview")
const memories = ref<MemoryItem[]>([])
const memoryTotal = ref(0)
const memoryPage = ref(0)
const MEMORY_PAGE_SIZE = 20
const atoms = ref<MemoryAtom[]>([])
const atomsLoading = ref(false)
const atomsError = ref("")
const knowledgeStatus = ref<{state?: string; processed?: number; total?: number; lastError?: string}>({})
const debugQuery = ref("")
const debugResult = ref<MemoryRecallDebug | null>(null)
const debugLoading = ref(false)
const knowledgeLoading = ref(false)
const searchKeyword = ref("")
const kindFilter = ref("")
const statusFilter = ref("")
const loading = ref(false)
const loadError = ref("")

// 破坏性操作确认与模态状态
const clearAllOpen = ref(false)
const pendingDelete = ref<MemoryItem | null>(null)
const pendingArchive = ref<MemoryItem | null>(null)
const pendingRestore = ref<MemoryItem | null>(null)
const unsavedConfirmOpen = ref(false)
const showAdvancedTrace = ref(false)

// 详情弹窗与编辑状态
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

const SECTIONS = computed<SegmentItem<MemorySection>[]>(() => [
	{key: "overview", label: I18N.value.tabs.overview},
	{key: "memories", label: I18N.value.tabs.memories},
	{key: "atoms", label: I18N.value.tabs.atoms},
	{key: "knowledge", label: I18N.value.tabs.knowledge},
	{key: "archive", label: I18N.value.tabs.archive},
	{key: "transfer", label: I18N.value.tabs.transfer},
	{key: "debugger", label: I18N.value.tabs.debugger},
	{key: "advanced", label: I18N.value.tabs.advanced},
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

const formatTimestamp = (dateStr?: string | null): string => {
	if (!dateStr) return ""
	try {
		const parsed = new Date(dateStr)
		if (isNaN(parsed.getTime())) return dateStr
		return parsed.toLocaleString()
	} catch {
		return dateStr
	}
}

const closeArchiveConfirm = (show: boolean) => {
	if (!show) pendingArchive.value = null
}

const closeRestoreConfirm = (show: boolean) => {
	if (!show) pendingRestore.value = null
}

const closeDeleteConfirm = (show: boolean) => {
	if (!show) pendingDelete.value = null
}

const isItemExpired = (item: MemoryItem): boolean => {
	if (item.status === "expired") return true
	if (item.expiresAt) {
		const expTime = new Date(item.expiresAt).getTime()
		if (!isNaN(expTime) && expTime < Date.now()) return true
	}
	return false
}

const getKindLabel = (kind?: string): string => {
	const MATCH = KIND_OPTIONS.value.find(opt => opt.value === (kind || "general"))
	return MATCH ? MATCH.label : (kind || "general")
}

const getStatusTone = (status?: string): "teal" | "neutral" | "warning" | "danger" => {
	if (status === "active") return "teal"
	if (status === "dormant") return "warning"
	if (status === "expired") return "danger"
	if (status === "archived") return "neutral"
	return "teal"
}

const getStatusLabel = (status?: string): string => {
	if (status === "active") return I18N.value.list.active
	if (status === "dormant") return I18N.value.list.dormant
	if (status === "expired") return I18N.value.list.expired
	if (status === "archived") return I18N.value.list.archived
	return status || I18N.value.list.active
}

const getSourceLabel = (source?: string): string => {
	if (source === "agent") return I18N.value.list.sourceAgent
	if (source === "manual") return I18N.value.list.sourceManual
	return source || I18N.value.list.sourceManual
}

// 检测未保存编辑
const hasUnsavedChanges = computed(() => {
	if (!selectedMemory.value) return false
	const INIT_CONTENT = selectedMemory.value.content || ""
	const INIT_CANONICAL = selectedMemory.value.canonicalSummary || selectedMemory.value.content || ""
	const INIT_PERSONA = selectedMemory.value.personaSummary || selectedMemory.value.content || ""
	const INIT_TAGS = selectedMemory.value.tags || ""
	const INIT_KIND = selectedMemory.value.kind || "general"
	const INIT_IMPORTANCE = selectedMemory.value.importance ?? 0.8
	const INIT_CONFIDENCE = selectedMemory.value.confidence ?? 0.8

	return (
		editContent.value.trim() !== INIT_CONTENT.trim() ||
		editCanonical.value.trim() !== INIT_CANONICAL.trim() ||
		editPersona.value.trim() !== INIT_PERSONA.trim() ||
		editTags.value.trim() !== INIT_TAGS.trim() ||
		editKind.value !== INIT_KIND ||
		Math.abs(editImportance.value - INIT_IMPORTANCE) > 0.01 ||
		Math.abs(editConfidence.value - INIT_CONFIDENCE) > 0.01
	)
})

// 加载记忆列表
const loadMemories = async () => {
	loading.value = true
	loadError.value = ""
	try {
		const STATUS = CURRENT_SECTION.value === "archive" ? "archived" : statusFilter.value || undefined
		const PAGE = await RUNTIME.memoryListPage(
			searchKeyword.value.trim() || undefined,
			kindFilter.value || undefined,
			STATUS,
			MEMORY_PAGE_SIZE,
			memoryPage.value * MEMORY_PAGE_SIZE
		)
		memories.value = PAGE.items
		memoryTotal.value = PAGE.total
	} catch (error) {
		loadError.value = I18N.value.list.loadError
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		loading.value = false
	}
}

const loadAtoms = async () => {
	atomsLoading.value = true
	atomsError.value = ""
	try {
		atoms.value = await RUNTIME.memoryAtoms(undefined, "active", 100, 0)
	} catch (error) {
		atomsError.value = I18N.value.toast.loadFailed
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		atomsLoading.value = false
	}
}

const openMemory = async (id: number) => {
	detailLoading.value = true
	showAdvancedTrace.value = false
	try {
		const DETAIL = await RUNTIME.memoryGet(id)
		selectedMemory.value = DETAIL.item
		selectedAtoms.value = DETAIL.atoms ?? []
		selectedSources.value = DETAIL.sources ?? []
		editContent.value = DETAIL.item.content || ""
		editCanonical.value = DETAIL.item.canonicalSummary || DETAIL.item.content || ""
		editPersona.value = DETAIL.item.personaSummary || DETAIL.item.content || ""
		editTags.value = DETAIL.item.tags || ""
		editKind.value = DETAIL.item.kind || "general"
		editImportance.value = DETAIL.item.importance ?? 0.8
		editConfidence.value = DETAIL.item.confidence ?? 0.8
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		detailLoading.value = false
	}
}

const requestCloseMemory = () => {
	if (hasUnsavedChanges.value) {
		unsavedConfirmOpen.value = true
	} else {
		forceCloseMemory()
	}
}

const forceCloseMemory = () => {
	unsavedConfirmOpen.value = false
	selectedMemory.value = null
	selectedAtoms.value = []
	selectedSources.value = []
	showAdvancedTrace.value = false
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

const confirmArchive = async () => {
	if (!pendingArchive.value) return
	const ID = pendingArchive.value.id
	pendingArchive.value = null
	try {
		await RUNTIME.memoryArchive(ID)
		if (selectedMemory.value?.id === ID) {
			forceCloseMemory()
		}
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.archiveFailed, error)
	}
}

const confirmRestore = async () => {
	if (!pendingRestore.value) return
	const ID = pendingRestore.value.id
	pendingRestore.value = null
	try {
		await RUNTIME.memoryRestore(ID)
		if (selectedMemory.value?.id === ID) {
			forceCloseMemory()
		}
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.restoreFailed, error)
	}
}

const confirmDelete = async () => {
	if (!pendingDelete.value) return
	const ID = pendingDelete.value.id
	pendingDelete.value = null
	try {
		await RUNTIME.memoryDelete(ID)
		if (selectedMemory.value?.id === ID) {
			forceCloseMemory()
		}
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.deleteFailed, error)
	}
}

const confirmClearAll = async () => {
	clearAllOpen.value = false
	try {
		await RUNTIME.memoryClear()
		forceCloseMemory()
		await loadMemories()
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(I18N.value.toast.clearFailed, error)
	}
}

const resetMemoryPage = async () => {
	memoryPage.value = 0
	await loadMemories()
}

// 搜索防抖
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(searchKeyword, () => {
	if (searchTimer) clearTimeout(searchTimer)
	searchTimer = setTimeout(() => {
		searchTimer = null
		void resetMemoryPage()
	}, 300)
})

onBeforeUnmount(() => {
	if (searchTimer) clearTimeout(searchTimer)
})

const changeSection = (section: MemorySection) => {
	if (selectedMemory.value && hasUnsavedChanges.value) {
		unsavedConfirmOpen.value = true
		return
	}
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

// ===================================================================
// 记忆数据迁移与导入导出
// ===================================================================

const MAX_IMPORT_BYTES = 5 * 1024 * 1024 // 5MB 初筛限制

// 导出状态
const isExporting = ref(false)
const exportResult = ref<MemoryExportResult | null>(null)
const exportError = ref("")
const copiedExport = ref(false)

const exportSanitizedFields = computed<string[]>(() => {
	if (exportResult.value?.sanitizedFields?.length) {
		return exportResult.value.sanitizedFields
	}
	return ["content", "canonicalSummary", "personaSummary", "kind", "importance", "confidence", "tags", "status"]
})

const runExport = async () => {
	if (isExporting.value) return
	isExporting.value = true
	exportError.value = ""
	try {
		const RESULT = await RUNTIME.memoryExport()
		exportResult.value = RESULT
		feedback.success(I18N.value.transfer.exportSuccess)
	} catch (error) {
		exportError.value = error instanceof Error ? error.message : String(error)
		feedback.error(I18N.value.toast.exportFailed, error)
	} finally {
		isExporting.value = false
	}
}

const downloadExportFile = () => {
	if (!exportResult.value) return
	try {
		const DATA_STR = exportResult.value.content || JSON.stringify(exportResult.value, null, 2)
		const BLOB = new Blob([DATA_STR], {type: "application/json"})
		const URL = window.URL.createObjectURL(BLOB)
		const LINK = document.createElement("a")
		LINK.href = URL
		LINK.download = exportResult.value.fileName || `nori-memory-export-${new Date().toISOString().slice(0, 10)}.json`
		document.body.appendChild(LINK)
		LINK.click()
		document.body.removeChild(LINK)
		window.URL.revokeObjectURL(URL)
	} catch (error) {
		feedback.error(I18N.value.toast.exportFailed, error)
	}
}

const copyExportContent = async () => {
	if (!exportResult.value) return
	try {
		const CONTENT = exportResult.value.content || JSON.stringify({
			version: exportResult.value.version ?? 1,
			totalCount: exportResult.value.totalCount,
			sanitizedFields: exportSanitizedFields.value,
			exportedAt: exportResult.value.exportedAt || new Date().toISOString(),
		}, null, 2)
		await navigator.clipboard.writeText(CONTENT)
		copiedExport.value = true
		feedback.success(I18N.value.transfer.copied)
		setTimeout(() => {
			copiedExport.value = false
		}, 2000)
	} catch (error) {
		feedback.error(I18N.value.toast.exportFailed, error)
	}
}

// 导入状态
const importFile = ref<File | null>(null)
const importFileContent = ref<string>("")
const importError = ref("")
const isPreviewing = ref(false)
const importPreview = ref<MemoryImportPreviewResult | null>(null)
const isCommitting = ref(false)
const importConfirmOpen = ref(false)
const conflictStrategy = ref<MemoryImportConflictStrategy>("skip")
const fileInputRef = ref<HTMLInputElement | null>(null)

const STRATEGY_OPTIONS = computed<SegmentItem<MemoryImportConflictStrategy>[]>(() => [
	{key: "skip", label: I18N.value.transfer.strategySkip},
	{key: "overwrite", label: I18N.value.transfer.strategyOverwrite},
	{key: "create_copy", label: I18N.value.transfer.strategyCreateCopy},
])

const formatFileSize = (bytes: number): string => {
	if (bytes < 1024) return `${bytes} B`
	if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
	return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}

const handleFileSelect = async (event: Event) => {
	const INPUT = event.target as HTMLInputElement
	const FILE = INPUT.files?.[0]
	if (!FILE) return

	importError.value = ""
	importPreview.value = null
	importFileContent.value = ""

	// 1. 文件格式初筛 (.json)
	if (!FILE.name.toLowerCase().endsWith(".json")) {
		importFile.value = null
		importError.value = I18N.value.transfer.fileInvalidFormat
		feedback.error(I18N.value.transfer.fileInvalidFormat)
		INPUT.value = ""
		return
	}

	// 2. 文件大小初筛 (<= 5MB)
	if (FILE.size > MAX_IMPORT_BYTES) {
		importFile.value = null
		importError.value = I18N.value.transfer.fileTooLarge
		feedback.error(I18N.value.transfer.fileTooLarge)
		INPUT.value = ""
		return
	}

	importFile.value = FILE

	try {
		const TEXT = await FILE.text()
		// 3. JSON 合法性初筛
		try {
			JSON.parse(TEXT)
		} catch {
			importError.value = I18N.value.transfer.jsonParseFailed
			feedback.error(I18N.value.transfer.jsonParseFailed)
			return
		}
		importFileContent.value = TEXT
	} catch (error) {
		importError.value = I18N.value.transfer.fileReadFailed
		feedback.error(I18N.value.transfer.fileReadFailed, error)
	}
}

const triggerFileInput = () => {
	fileInputRef.value?.click()
}

const runImportPreview = async () => {
	if (!importFileContent.value.trim() || isPreviewing.value) return
	isPreviewing.value = true
	importError.value = ""
	try {
		const PREVIEW = await RUNTIME.memoryImportPreview(
			importFileContent.value,
			importFile.value?.name,
			importFile.value?.size,
		)
		if (PREVIEW.valid === false && PREVIEW.errors?.length) {
			importError.value = PREVIEW.errors.join("; ")
			feedback.error(I18N.value.transfer.previewFailed)
		} else {
			importPreview.value = PREVIEW
			feedback.success(I18N.value.transfer.previewSuccess)
		}
	} catch (error) {
		importError.value = error instanceof Error ? error.message : String(error)
		feedback.error(I18N.value.transfer.previewFailed, error)
	} finally {
		isPreviewing.value = false
	}
}

const cancelImportPreview = () => {
	importFile.value = null
	importFileContent.value = ""
	importPreview.value = null
	importError.value = ""
	if (fileInputRef.value) {
		fileInputRef.value.value = ""
	}
}

const requestCommitImport = () => {
	importConfirmOpen.value = true
}

const confirmCommitImport = async () => {
	if (isCommitting.value) return
	isCommitting.value = true
	try {
		const RESULT = await RUNTIME.memoryImportCommit({
			previewToken: importPreview.value?.previewToken,
			conflictStrategy: conflictStrategy.value,
		})
		if (RESULT.success) {
			feedback.success(I18N.value.transfer.importSuccess)
			cancelImportPreview()
			importConfirmOpen.value = false
			await loadMemories()
			await RUNTIME.refresh()
		} else {
			importError.value = RESULT.message || I18N.value.transfer.importFailed
			feedback.error(I18N.value.toast.importFailed, new Error(RESULT.message || I18N.value.transfer.importFailed))
			importConfirmOpen.value = false
		}
	} catch (error) {
		importError.value = error instanceof Error ? error.message : String(error)
		feedback.error(I18N.value.toast.importFailed, error)
		importConfirmOpen.value = false
	} finally {
		isCommitting.value = false
	}
}

const getConflictTone = (conflictType?: string): "teal" | "warning" | "danger" | "neutral" => {
	if (conflictType === "conflict") return "danger"
	if (conflictType === "duplicate") return "warning"
	return "teal"
}

const getConflictLabel = (conflictType?: string): string => {
	if (conflictType === "conflict") return I18N.value.transfer.conflictLabel
	if (conflictType === "duplicate") return I18N.value.transfer.duplicateLabel
	return I18N.value.transfer.newLabel
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader
			:title="I18N.header.title"
			:subtitle="I18N.header.subtitle"
		/>

		<!-- 导航分段 -->
		<AppSegmented
			class="self-start"
			:model-value="CURRENT_SECTION"
			:items="SECTIONS"
			:label="I18N.header.title"
			@update:model-value="changeSection"
		/>

		<!-- 概览视图 -->
		<div v-if="CURRENT_SECTION === 'overview'" class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 记忆资产概览 -->
			<AppCard :title="I18N.overview.title" icon="sparkles">
				<div class="grid grid-cols-2 gap-3 md:grid-cols-4">
					<AppStatTile :label="I18N.overview.active" :value="String(SNAPSHOT?.memory?.active ?? 0)" tone="teal"/>
					<AppStatTile :label="I18N.overview.atoms" :value="String(SNAPSHOT?.memory?.atoms ?? 0)" tone="teal"/>
					<AppStatTile :label="I18N.overview.archived" :value="String(SNAPSHOT?.memory?.archived ?? 0)"/>
					<AppStatTile :label="I18N.overview.knowledge" :value="String(SNAPSHOT?.memory?.knowledgeChunks ?? 0)" tone="teal"/>
				</div>

				<div class="flex items-center gap-2 pt-2 border-t border-line-subtle text-xs text-text-muted">
					<Icon name="info" :size="13" class="text-nori-teal-soft shrink-0"/>
					<span>{{ I18N.overview.index }}: {{ SNAPSHOT?.memory?.indexState }} ({{ SNAPSHOT?.memory?.indexProcessed ?? 0 }} / {{ SNAPSHOT?.memory?.indexTotal ?? 0 }})</span>
				</div>
			</AppCard>

			<!-- 2. 核心记忆机制开关 -->
			<AppCard :title="I18N.header.title" icon="settings">
				<AppSwitchRow
					:title="I18N.header.title"
					:desc="SNAPSHOT?.memory?.enabled ? I18N.overview.enabled : I18N.overview.disabled"
					:model-value="Boolean(SNAPSHOT?.memory?.enabled)"
					@update:model-value="() => toggleMemorySetting('enabled')"
				/>

				<AppSwitchRow
					:title="I18N.overview.reflection"
					:desc="SNAPSHOT?.memory?.reflectionEnabled ? I18N.overview.enabled : I18N.overview.disabled"
					:model-value="Boolean(SNAPSHOT?.memory?.reflectionEnabled)"
					@update:model-value="() => toggleMemorySetting('reflectionEnabled')"
				/>

				<AppSwitchRow
					:title="I18N.overview.decay"
					:desc="SNAPSHOT?.memory?.decayEnabled ? I18N.overview.enabled : I18N.overview.disabled"
					:model-value="Boolean(SNAPSHOT?.memory?.decayEnabled)"
					@update:model-value="() => toggleMemorySetting('decayEnabled')"
				/>

				<AppSwitchRow
					:title="I18N.overview.archive"
					:desc="SNAPSHOT?.memory?.archiveEnabled ? I18N.overview.enabled : I18N.overview.disabled"
					:model-value="Boolean(SNAPSHOT?.memory?.archiveEnabled)"
					@update:model-value="() => toggleMemorySetting('archiveEnabled')"
				/>
			</AppCard>
		</div>

		<!-- 记忆原子视图 -->
		<div v-if="CURRENT_SECTION === 'atoms'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.atoms.title" icon="package">
				<template #actions>
					<AppButton size="sm" icon="refresh" :loading="atomsLoading" @click="loadAtoms">{{ I18N.detail.retry }}</AppButton>
				</template>
				<div v-if="atomsError" class="surface-card flex items-center justify-between p-3 text-sm text-danger-text border border-danger/24">
					<span>{{ atomsError }}</span>
					<AppButton size="sm" variant="ghost" @click="loadAtoms">{{ I18N.detail.retry }}</AppButton>
				</div>
				<div v-else-if="atomsLoading" class="py-6 text-center text-sm text-text-faint flex items-center justify-center gap-2">
					<Icon name="loading" :size="16" class="animate-spin"/>
					<span>{{ I18N.detail.loading }}</span>
				</div>
				<AppEmpty v-else-if="atoms.length === 0" icon="package" :title="I18N.atoms.empty"/>
				<div v-else class="flex flex-col gap-2 max-h-[32rem] scroll-area">
					<div v-for="atom in atoms" :key="atom.id" class="surface-card flex flex-col gap-1.5 p-3">
						<div class="flex flex-wrap items-center gap-1.5">
							<AppChip tone="teal">{{ atom.atomType }}</AppChip>
							<AppChip tone="warning">{{ I18N.add.importance }} {{ Math.round(atom.importance * 100) }}%</AppChip>
							<AppChip tone="neutral">{{ I18N.detail.confidence }} {{ Math.round(atom.confidence * 100) }}%</AppChip>
							<AppChip :tone="getStatusTone(atom.status)">{{ getStatusLabel(atom.status) }}</AppChip>
						</div>
						<p class="text-base text-text-primary leading-relaxed">{{ atom.content }}</p>
						<div class="flex flex-wrap items-center justify-between gap-2 text-xs text-text-faint pt-1 border-t border-line-subtle">
							<span>{{ I18N.atoms.parent }} #{{ atom.parentMemoryId }}</span>
							<span>{{ formatTimestamp(atom.createdAt) }}</span>
						</div>
					</div>
				</div>
			</AppCard>
		</div>

		<!-- 知识库视图 -->
		<div v-if="CURRENT_SECTION === 'knowledge'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.knowledge.title" icon="package">
				<template #actions>
					<div class="flex gap-2">
						<AppButton size="sm" @click="openKnowledge">{{ I18N.knowledge.open }}</AppButton>
						<AppButton variant="primary" size="sm" :loading="knowledgeLoading" @click="reindexKnowledge">{{ I18N.knowledge.reindex }}</AppButton>
					</div>
				</template>
				<div class="flex flex-col gap-2 text-sm text-text-muted">
					<div class="flex justify-between gap-3">
						<span>{{ I18N.knowledge.path }}</span>
						<span class="mono break-all text-right">{{ SNAPSHOT?.memory?.knowledgePath || I18N.detail.empty }}</span>
					</div>
					<div class="flex justify-between">
						<span>{{ I18N.knowledge.chunks }}</span>
						<span>{{ knowledgeStatus.total ?? SNAPSHOT?.memory?.knowledgeChunks ?? 0 }}</span>
					</div>
					<div class="flex justify-between">
						<span>{{ I18N.knowledge.status }}</span>
						<span>{{ knowledgeStatus.state ?? SNAPSHOT?.memory?.indexState }}</span>
					</div>
				</div>
				<p v-if="knowledgeStatus.lastError" class="text-sm text-danger-text">{{ knowledgeStatus.lastError }}</p>
			</AppCard>
		</div>

		<!-- 检索调试视图 -->
		<div v-if="CURRENT_SECTION === 'debugger'" class="flex flex-col gap-3.5 pb-5">
			<AppCard :title="I18N.debugger.title" icon="terminal">
				<div class="flex gap-2">
					<input v-model="debugQuery" class="input-base flex-1" :placeholder="I18N.debugger.placeholder" @keyup.enter="runDebugger"/>
					<AppButton variant="primary" size="sm" :loading="debugLoading" @click="runDebugger">{{ I18N.debugger.run }}</AppButton>
				</div>
				<div v-if="debugResult" class="flex flex-col gap-3 text-sm">
					<div class="surface-card p-3">
						<p class="field-label">{{ I18N.debugger.query }}</p>
						<p class="mono whitespace-pre-wrap text-text-muted">{{ debugResult.trace?.expandedQuery || debugQuery }}</p>
					</div>
					<div class="grid grid-cols-1 md:grid-cols-2 gap-2">
						<div class="surface-card p-3">
							<p class="field-label mb-1.5">{{ I18N.debugger.keyword }}</p>
							<div v-if="debugResult.trace?.keywordHits?.length" class="flex flex-col gap-1">
								<div v-for="hit in debugResult.trace.keywordHits" :key="`k-${hit.memoryId}`" class="flex items-center justify-between text-xs mono">
									<span>#{{ hit.memoryId }}</span>
									<span class="text-text-muted">{{ hit.score.toFixed(4) }} (rank: {{ hit.rank }})</span>
								</div>
							</div>
							<p v-else class="text-xs text-text-faint">{{ I18N.detail.empty }}</p>
						</div>
						<div class="surface-card p-3">
							<p class="field-label mb-1.5">{{ I18N.debugger.vector }}</p>
							<div v-if="debugResult.trace?.vectorHits?.length" class="flex flex-col gap-1">
								<div v-for="hit in debugResult.trace.vectorHits" :key="`v-${hit.memoryId}`" class="flex items-center justify-between text-xs mono">
									<span>#{{ hit.memoryId }}</span>
									<span class="text-text-muted">{{ hit.score.toFixed(4) }} (rank: {{ hit.rank }})</span>
								</div>
							</div>
							<p v-else class="text-xs text-text-faint">{{ I18N.detail.empty }}</p>
						</div>
						<div class="surface-card p-3">
							<p class="field-label mb-1.5">{{ I18N.debugger.atoms }}</p>
							<div v-if="debugResult.trace?.atomHits?.length" class="flex flex-col gap-1">
								<div v-for="hit in debugResult.trace.atomHits" :key="`a-${hit.memoryId}`" class="flex items-center justify-between text-xs mono">
									<span>#{{ hit.memoryId }}</span>
									<span class="text-text-muted">{{ hit.score.toFixed(4) }} (rank: {{ hit.rank }})</span>
								</div>
							</div>
							<p v-else class="text-xs text-text-faint">{{ I18N.detail.empty }}</p>
						</div>
						<div class="surface-card p-3">
							<p class="field-label mb-1.5">{{ I18N.debugger.rrf }}</p>
							<div v-if="debugResult.trace?.rrfHits?.length" class="flex flex-col gap-1">
								<div v-for="hit in debugResult.trace.rrfHits" :key="`r-${hit.memoryId}`" class="flex items-center justify-between text-xs mono">
									<span>#{{ hit.memoryId }}</span>
									<span class="text-text-muted">{{ hit.score.toFixed(4) }} (rank: {{ hit.rank }})</span>
								</div>
							</div>
							<p v-else class="text-xs text-text-faint">{{ I18N.detail.empty }}</p>
						</div>
					</div>
					<div class="surface-card p-3">
						<p class="field-label mb-1.5">{{ I18N.debugger.injected }}</p>
						<div v-if="debugResult.personal?.length" class="flex flex-col gap-1.5">
							<div v-for="item in debugResult.personal" :key="item.id" class="text-text-primary text-sm flex items-start gap-2">
								<AppChip tone="teal">#{{ item.id }}</AppChip>
								<span>{{ item.personaSummary || item.content }}</span>
							</div>
						</div>
						<p v-else class="text-xs text-text-faint">{{ I18N.detail.empty }}</p>
					</div>
					<div v-if="debugResult.trace?.filteredIds?.length" class="surface-card p-3">
						<p class="field-label">{{ I18N.debugger.filtered }}</p>
						<p class="text-text-muted mono">{{ debugResult.trace.filteredIds.join(", ") }}</p>
					</div>
					<div v-if="debugResult.knowledge?.length" class="surface-card p-3">
						<p class="field-label mb-1.5">{{ I18N.debugger.knowledge }}</p>
						<div class="flex flex-col gap-1">
							<p v-for="item in debugResult.knowledge" :key="item.id" class="text-xs">
								{{ item.heading }} · {{ item.awareness }} · {{ item.score.toFixed(4) }}
							</p>
						</div>
					</div>
					<div v-if="debugResult.echoes?.length" class="surface-card p-3">
						<p class="field-label mb-1.5">{{ I18N.debugger.echoes }}</p>
						<div class="flex flex-col gap-1">
							<p v-for="item in debugResult.echoes" :key="item.content" class="text-xs text-text-muted">
								{{ item.content }}
							</p>
						</div>
					</div>
				</div>
				<AppEmpty v-else icon="terminal" :title="I18N.debugger.empty"/>
			</AppCard>
		</div>

		<!-- 记忆迁移视图 (导入 / 导出) -->
		<div v-if="CURRENT_SECTION === 'transfer'" class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 记忆导出 -->
			<AppCard :title="I18N.transfer.exportTitle" icon="download">
				<p class="text-sm text-text-muted leading-relaxed">{{ I18N.transfer.exportDesc }}</p>

				<div class="flex items-center gap-2 pt-1">
					<AppButton
						variant="primary"
						size="sm"
						icon="download"
						:loading="isExporting"
						:disabled="isExporting"
						@click="runExport"
					>
						{{ isExporting ? I18N.transfer.exporting : I18N.transfer.exportBtn }}
					</AppButton>
				</div>

				<!-- 导出错误提示 -->
				<div v-if="exportError" class="surface-card flex items-center justify-between p-3 text-sm text-danger-text border border-danger/24" role="alert">
					<span>{{ exportError }}</span>
					<AppButton size="sm" variant="ghost" @click="runExport">{{ I18N.detail.retry }}</AppButton>
				</div>

				<!-- 导出结果概览与下载 -->
				<div v-else-if="exportResult" class="surface-card flex flex-col gap-3 p-3.5 border border-line-subtle" role="region" :aria-label="I18N.transfer.exportStatsTitle">
					<div class="flex items-center justify-between">
						<span class="text-sm font-600 text-text-primary">{{ I18N.transfer.exportStatsTitle }}</span>
						<AppChip tone="teal">{{ I18N.transfer.privacyBadge }}</AppChip>
					</div>

					<div class="grid grid-cols-2 gap-2.5 md:grid-cols-3">
						<AppStatTile :label="I18N.transfer.totalExported" :value="String(exportResult.totalCount)" tone="teal"/>
						<AppStatTile :label="I18N.transfer.activeExported" :value="String(exportResult.activeCount ?? SNAPSHOT?.memory?.active ?? exportResult.totalCount)" tone="teal"/>
						<AppStatTile :label="I18N.transfer.archivedExported" :value="String(exportResult.archivedCount ?? SNAPSHOT?.memory?.archived ?? 0)"/>
					</div>

					<div class="flex flex-col gap-1.5 pt-2 border-t border-line-subtle">
						<span class="field-label">{{ I18N.transfer.sanitizedFieldsTitle }}</span>
						<div class="flex flex-wrap gap-1.5">
							<AppChip v-for="field in exportSanitizedFields" :key="field" tone="neutral">{{ field }}</AppChip>
						</div>
					</div>

					<div class="flex items-center gap-2 pt-1 text-xs text-text-muted">
						<Icon name="shield" :size="13" class="text-nori-teal-soft shrink-0"/>
						<span>{{ I18N.transfer.sanitizedNotice }}</span>
					</div>

					<div class="flex items-center gap-2 pt-2 border-t border-line-subtle">
						<AppButton size="sm" icon="download" @click="downloadExportFile">{{ I18N.transfer.downloadFile }}</AppButton>
						<AppButton size="sm" variant="ghost" icon="copy" @click="copyExportContent">
							{{ copiedExport ? I18N.transfer.copied : I18N.transfer.copyJson }}
						</AppButton>
					</div>
				</div>
			</AppCard>

			<!-- 2. 记忆导入 -->
			<AppCard :title="I18N.transfer.importTitle" icon="upload">
				<p class="text-sm text-text-muted leading-relaxed">{{ I18N.transfer.importDesc }}</p>

				<!-- 隐藏的文件 input -->
				<input
					ref="fileInputRef"
					type="file"
					accept=".json"
					class="hidden"
					aria-hidden="true"
					@change="handleFileSelect"
				/>

				<!-- 文件选择区域 -->
				<div class="flex flex-col gap-2">
					<div
						class="surface-card flex flex-col items-center justify-center gap-2 p-5 border border-dashed border-line-subtle rounded-md cursor-pointer transition-all duration-200 hover:(border-nori-teal-bright/40 bg-nori-teal-bright/4)"
						@click="triggerFileInput"
					>
						<div class="w-10 h-10 rounded-full bg-nori-teal-bright/10 text-nori-teal-bright flex items-center justify-center">
							<Icon :name="importFile ? 'file' : 'upload'" :size="20"/>
						</div>
						<div class="text-center">
							<p class="text-sm font-500 text-text-primary">
								{{ importFile ? `${I18N.transfer.fileSelected}: ${importFile.name}` : I18N.transfer.dragDropHint }}
							</p>
							<p class="text-xs text-text-muted mt-0.5">
								{{ importFile ? `${I18N.transfer.fileSize}: ${formatFileSize(importFile.size)}` : I18N.transfer.fileLimitHint }}
							</p>
						</div>
						<AppButton size="sm" variant="ghost" class="mt-1" @click.stop="triggerFileInput">
							{{ importFile ? I18N.transfer.changeFile : I18N.transfer.selectFile }}
						</AppButton>
					</div>
				</div>

				<!-- 导入错误提示 -->
				<div v-if="importError" class="surface-card flex items-center justify-between p-3 text-sm text-danger-text border border-danger/24" role="alert">
					<span>{{ importError }}</span>
					<AppButton size="sm" variant="ghost" @click="importError = ''">{{ I18N.common.close }}</AppButton>
				</div>

				<!-- 解析与预览按钮 -->
				<div v-if="importFile && !importPreview" class="flex items-center gap-2 pt-1">
					<AppButton
						variant="primary"
						size="sm"
						icon="sparkles"
						:loading="isPreviewing"
						:disabled="isPreviewing || !importFileContent"
						@click="runImportPreview"
					>
						{{ isPreviewing ? I18N.transfer.previewing : I18N.transfer.previewBtn }}
					</AppButton>
					<AppButton size="sm" variant="ghost" @click="cancelImportPreview">
						{{ I18N.transfer.cancelPreview }}
					</AppButton>
				</div>

				<!-- 预览分析与确认区域 -->
				<div v-if="importPreview" class="surface-card flex flex-col gap-3.5 p-3.5 border border-line-subtle" role="region" :aria-label="I18N.transfer.previewSummaryTitle">
					<div class="flex items-center justify-between">
						<span class="text-sm font-600 text-text-primary">{{ I18N.transfer.previewSummaryTitle }}</span>
						<AppChip tone="teal">{{ I18N.transfer.privacyBadge }}</AppChip>
					</div>

					<!-- 统计卡片 -->
					<div class="grid grid-cols-2 gap-2.5 md:grid-cols-4">
						<AppStatTile :label="I18N.transfer.totalToImport" :value="String(importPreview.totalCount)" tone="teal"/>
						<AppStatTile :label="I18N.transfer.newItems" :value="String(importPreview.newCount)" tone="teal"/>
						<AppStatTile :label="I18N.transfer.duplicateItems" :value="String(importPreview.duplicateCount)" tone="warning"/>
						<AppStatTile :label="I18N.transfer.conflictItems" :value="String(importPreview.conflictCount)" :tone="importPreview.conflictCount > 0 ? 'danger' : 'neutral'"/>
					</div>

					<!-- 冲突处理策略 -->
					<div class="flex flex-col gap-1.5 pt-2 border-t border-line-subtle">
						<span class="field-label">{{ I18N.transfer.strategyTitle }}</span>
						<AppSegmented
							v-model="conflictStrategy"
							:items="STRATEGY_OPTIONS"
							:label="I18N.transfer.strategyTitle"
							size="sm"
						/>
					</div>

					<!-- 隐私说明 -->
					<div class="flex items-center gap-2 text-xs text-text-muted">
						<Icon name="info" :size="13" class="text-nori-teal-soft shrink-0"/>
						<span>{{ I18N.transfer.previewNotice }}</span>
					</div>

					<!-- 条目预览列表 -->
					<div class="flex flex-col gap-2 pt-2 border-t border-line-subtle">
						<span class="field-label">{{ I18N.transfer.previewListTitle }} ({{ importPreview.items?.length ?? 0 }})</span>
						<div v-if="importPreview.items && importPreview.items.length > 0" class="flex flex-col gap-2 max-h-[18rem] scroll-area">
							<div
								v-for="(item, index) in importPreview.items"
								:key="item.id ?? index"
								class="p-2.5 rounded bg-overlay-2 border border-line-subtle flex flex-col gap-1.5 text-sm"
							>
								<div class="flex flex-wrap items-center gap-1.5">
									<AppChip :tone="getConflictTone(item.conflictType)">{{ getConflictLabel(item.conflictType) }}</AppChip>
									<AppChip v-if="item.kind" tone="teal">{{ getKindLabel(item.kind) }}</AppChip>
									<AppChip v-if="item.importance !== undefined" tone="warning">{{ I18N.add.importance }} {{ Math.round(item.importance * 100) }}%</AppChip>
									<AppChip v-if="item.tags" tone="neutral">{{ item.tags }}</AppChip>
								</div>
								<p class="text-base text-text-primary leading-normal">{{ item.contentSummary }}</p>
								<p v-if="item.conflictReason" class="text-xs text-warning">{{ item.conflictReason }}</p>
							</div>
						</div>
						<p v-else class="text-xs text-text-faint py-2">{{ I18N.transfer.previewNoItems }}</p>
					</div>

					<!-- 底部操作按钮 -->
					<div class="flex items-center justify-between pt-2 border-t border-line-subtle">
						<AppButton size="sm" variant="ghost" @click="cancelImportPreview">
							{{ I18N.transfer.cancelPreview }}
						</AppButton>
						<AppButton
							variant="primary"
							size="sm"
							icon="check"
							:loading="isCommitting"
							:disabled="isCommitting || importPreview.totalCount === 0"
							@click="requestCommitImport"
						>
							{{ isCommitting ? I18N.transfer.importing : I18N.transfer.confirmImportBtn }}
						</AppButton>
					</div>
				</div>
			</AppCard>
		</div>

		<!-- 记忆列表 / 归档 / 高级设置视图 -->
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
					<div class="flex items-center gap-2">
						<AppButton
							size="sm"
							icon="refresh"
							:loading="loading"
							@click="loadMemories"
						>{{ I18N.detail.retry }}</AppButton>
						<AppButton
							v-if="memories.length > 0 && CURRENT_SECTION === 'memories'"
							variant="danger"
							size="sm"
							icon="trash"
							@click="clearAllOpen = true"
						>{{ I18N.list.clearAll }}</AppButton>
					</div>
				</template>

				<div class="flex flex-wrap gap-2">
					<div class="min-w-[12rem] flex-1">
						<AppSearchField
							v-model="searchKeyword"
							:placeholder="I18N.list.searchPlaceholder"
							:clear-label="UI_I18N.clearSearch"
						/>
					</div>
					<n-select v-model:value="kindFilter" :options="KIND_FILTER_OPTIONS" class="w-[12rem] shrink-0" @update:value="resetMemoryPage"/>
					<n-select v-if="CURRENT_SECTION === 'memories'" v-model:value="statusFilter" :options="STATUS_FILTER_OPTIONS" class="w-[12rem] shrink-0" @update:value="resetMemoryPage"/>
				</div>

				<!-- 错误重试条 -->
				<div v-if="loadError" class="surface-card flex items-center justify-between p-3 text-sm text-danger-text border border-danger/24">
					<span>{{ loadError }}</span>
					<AppButton size="sm" variant="primary" @click="loadMemories">{{ I18N.list.retryLoad }}</AppButton>
				</div>

				<!-- 加载中 -->
				<div v-else-if="loading" class="py-8 text-center text-sm text-text-faint flex items-center justify-center gap-2">
					<Icon name="loading" :size="16" class="animate-spin"/>
					<span>{{ I18N.detail.loading }}</span>
				</div>

				<!-- 空态 -->
				<AppEmpty
					v-else-if="memories.length === 0"
					icon="package"
					:title="searchKeyword ? I18N.list.emptySearch : (CURRENT_SECTION === 'archive' ? I18N.list.emptyArchive : I18N.list.empty)"
				/>

				<!-- 列表条目 -->
				<div v-else class="flex flex-col gap-2 max-h-[28rem] scroll-area">
					<div
						v-for="item in memories"
						:key="item.id"
						class="flex items-start justify-between gap-3 px-3.5 py-2.5 rounded-sm bg-overlay-4 cursor-pointer
							border border-line-subtle transition-all duration-200
							hover:(bg-nori-teal-bright/4 border-line-strong)"
						@click="openMemory(item.id)"
					>
						<div class="flex flex-1 flex-col gap-1.5 min-w-0">
							<div class="flex flex-wrap items-center gap-1.5">
								<AppChip v-if="item.tags" tone="teal">{{ item.tags }}</AppChip>
								<AppChip>{{ getSourceLabel(item.source) }}</AppChip>
								<AppChip>{{ getKindLabel(item.kind) }}</AppChip>
								<AppChip tone="warning">{{ I18N.add.importance }} {{ Math.round(item.importance * 100) }}%</AppChip>
								<AppChip :tone="getStatusTone(item.status)" :dot="true">{{ getStatusLabel(item.status) }}</AppChip>
								<AppChip v-if="isItemExpired(item)" tone="danger">{{ I18N.detail.isExpired }}</AppChip>
							</div>
							<p class="text-base text-text-primary leading-normal">{{ item.content }}</p>
							<div class="flex items-center gap-3 text-xs text-text-faint">
								<span>{{ formatTimestamp(item.createdAt) }}</span>
								<span v-if="item.lastAccessedAt">{{ I18N.detail.lastAccessedAt }}: {{ formatTimestamp(item.lastAccessedAt) }}</span>
							</div>
						</div>
						<div class="flex items-center gap-1 shrink-0">
							<AppButton
								v-if="CURRENT_SECTION === 'memories' && item.status !== 'archived'"
								variant="icon"
								size="sm"
								icon="package"
								:label="I18N.list.archiveThis"
								@click.stop="pendingArchive = item"
							/>
							<AppButton
								v-if="CURRENT_SECTION === 'archive' || item.status === 'archived'"
								variant="icon"
								size="sm"
								icon="refresh"
								:label="I18N.archive.restore"
								@click.stop="pendingRestore = item"
							/>
							<AppButton
								variant="icon"
								size="sm"
								icon="trash"
								:label="I18N.list.deleteThis"
								class="hover:(bg-danger/18 text-danger-text)"
								@click.stop="pendingDelete = item"
							/>
						</div>
					</div>
				</div>
				<div v-if="memoryTotal > MEMORY_PAGE_SIZE" class="flex items-center justify-between pt-2 text-sm text-text-muted">
					<AppButton variant="ghost" size="sm" icon="arrow-left" :disabled="memoryPage === 0" @click="memoryPage--; loadMemories()">{{ I18N.list.previous }}</AppButton>
					<span class="mono">{{ memoryPage + 1 }} / {{ Math.ceil(memoryTotal / MEMORY_PAGE_SIZE) }}</span>
					<AppButton variant="ghost" size="sm" icon="arrow-right" :disabled="(memoryPage + 1) * MEMORY_PAGE_SIZE >= memoryTotal" @click="memoryPage++; loadMemories()">{{ I18N.list.next }}</AppButton>
				</div>
			</AppCard>
		</div>

		<!-- 记忆详情与编辑模态框 (可解释、可控) -->
		<AppModal
			:show="selectedMemory !== null"
			:title="selectedMemory ? `${I18N.detail.title} #${selectedMemory.id}` : ''"
			:close-label="I18N.common.close"
			:mask-closable="false"
			panel-class="w-[min(56rem,94vw)] max-h-[88vh]"
			@close="requestCloseMemory"
		>
			<div v-if="detailLoading" class="py-8 text-center text-text-faint flex items-center justify-center gap-2">
				<Icon name="loading" :size="16" class="animate-spin"/>
				<span>{{ I18N.detail.loading }}</span>
			</div>
			<div v-else-if="selectedMemory" class="flex flex-col gap-3.5">
				<!-- 顶部状态指示行 -->
				<div class="flex flex-wrap items-center justify-between gap-2 p-2.5 rounded-md bg-overlay-4 border border-line-subtle">
					<div class="flex flex-wrap items-center gap-2">
						<AppChip :tone="getStatusTone(selectedMemory.status)" :dot="true">{{ getStatusLabel(selectedMemory.status) }}</AppChip>
						<AppChip tone="teal">{{ getKindLabel(selectedMemory.kind) }}</AppChip>
						<AppChip>{{ getSourceLabel(selectedMemory.source) }}</AppChip>
					</div>
					<div class="flex items-center gap-2 text-xs">
						<AppChip :tone="isItemExpired(selectedMemory) ? 'danger' : 'success'">
							{{ isItemExpired(selectedMemory) ? I18N.detail.isExpired : (selectedMemory.expiresAt ? `${I18N.detail.expiresAt}: ${formatTimestamp(selectedMemory.expiresAt)}` : I18N.detail.neverExpires) }}
						</AppChip>
					</div>
				</div>

				<!-- 记忆正文编辑 -->
				<AppField :label="I18N.detail.content">
					<textarea v-model="editContent" class="input-base resize-y" rows="3"/>
				</AppField>

				<!-- 摘要字段 (规范摘要 / Nori 视角摘要) -->
				<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
					<AppField :label="I18N.detail.canonical">
						<textarea v-model="editCanonical" class="input-base resize-y" rows="2"/>
					</AppField>
					<AppField :label="I18N.detail.persona">
						<textarea v-model="editPersona" class="input-base resize-y" rows="2"/>
					</AppField>
				</div>

				<!-- 核心属性微调 -->
				<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
					<AppField :label="I18N.detail.kind">
						<n-select v-model:value="editKind" :options="KIND_OPTIONS"/>
					</AppField>
					<AppField :label="I18N.detail.tags">
						<input v-model="editTags" class="input-base"/>
					</AppField>
				</div>

				<!-- 置信度与重要度 -->
				<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
					<AppField :label="`${I18N.detail.confidence}: ${Math.round(editConfidence * 100)}%`">
						<div class="flex items-center gap-3">
							<n-slider v-model:value="editConfidence" :min="0" :max="1" :step="0.05" class="flex-1"/>
							<input v-model.number="editConfidence" type="number" min="0" max="1" step="0.05" class="input-base w-[6rem] text-center shrink-0"/>
						</div>
					</AppField>
					<AppField :label="`${I18N.detail.importance}: ${Math.round(editImportance * 100)}%`">
						<div class="flex items-center gap-3">
							<n-slider v-model:value="editImportance" :min="0" :max="1" :step="0.05" class="flex-1"/>
							<input v-model.number="editImportance" type="number" min="0" max="1" step="0.05" class="input-base w-[6rem] text-center shrink-0"/>
						</div>
					</AppField>
				</div>

				<!-- 来源对话上下文 -->
				<div class="surface-card p-3 flex flex-col gap-2">
					<div class="flex items-center justify-between">
						<span class="field-label">{{ I18N.detail.sourceMessages }}</span>
						<span class="text-xs text-text-faint">{{ selectedSources.length }}</span>
					</div>
					<div v-if="selectedSources.length > 0" class="flex flex-col gap-2 max-h-[12rem] scroll-area">
						<div
							v-for="source in selectedSources"
							:key="source.id"
							class="p-2.5 rounded bg-overlay-2 border border-line-subtle flex flex-col gap-1 text-sm"
						>
							<div class="flex items-center justify-between text-xs text-text-muted">
								<div class="flex items-center gap-1.5">
									<AppChip tone="teal">{{ source.role }}</AppChip>
									<span class="mono text-text-faint">#{{ source.sequence }}</span>
								</div>
								<span v-if="source.messageTime" class="text-text-faint">{{ formatTimestamp(source.messageTime) }}</span>
							</div>
							<p class="text-text-primary whitespace-pre-wrap leading-relaxed">{{ source.content }}</p>
						</div>
					</div>
					<p v-else class="text-xs text-text-faint py-1">{{ I18N.detail.noSources }}</p>
				</div>

				<!-- 生命周期与时间戳 -->
				<div class="surface-card p-3 flex flex-col gap-2">
					<span class="field-label">{{ I18N.detail.timestamps }}</span>
					<div class="grid grid-cols-2 md:grid-cols-3 gap-2 text-xs">
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.createdAt }}</span>
							<span class="text-text-primary mono">{{ formatTimestamp(selectedMemory.createdAt) }}</span>
						</div>
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.updatedAt }}</span>
							<span class="text-text-primary mono">{{ formatTimestamp(selectedMemory.updatedAt) }}</span>
						</div>
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.lastAccessedAt }}</span>
							<span class="text-text-primary mono">{{ selectedMemory.lastAccessedAt ? formatTimestamp(selectedMemory.lastAccessedAt) : I18N.detail.neverAccessed }}</span>
						</div>
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.lastReinforcedAt }}</span>
							<span class="text-text-primary mono">{{ selectedMemory.lastReinforcedAt ? formatTimestamp(selectedMemory.lastReinforcedAt) : I18N.detail.neverReinforced }}</span>
						</div>
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.accessCount }} / {{ I18N.detail.reinforcementCount }}</span>
							<span class="text-text-primary mono">{{ selectedMemory.accessCount ?? 0 }} / {{ selectedMemory.reinforcementCount ?? 0 }}</span>
						</div>
						<div class="flex flex-col gap-0.5">
							<span class="text-text-muted">{{ I18N.detail.ttlDays }}</span>
							<span class="text-text-primary mono">{{ selectedMemory.ttlDays ? `${selectedMemory.ttlDays}d` : '-' }}</span>
						</div>
					</div>
				</div>

				<!-- 高级溯源与事实原子折叠区 -->
				<div class="surface-card p-3 flex flex-col gap-2">
					<button
						type="button"
						class="flex items-center justify-between w-full text-left cursor-pointer focus-ring"
						@click="showAdvancedTrace = !showAdvancedTrace"
					>
						<span class="field-label flex items-center gap-2">
							<Icon :name="showAdvancedTrace ? 'arrow-up' : 'arrow-down'" :size="14" class="text-nori-teal-bright"/>
							{{ I18N.detail.advancedSection }}
						</span>
						<span class="text-xs text-text-faint">{{ selectedAtoms.length }} {{ I18N.detail.atoms }}</span>
					</button>

					<div v-if="showAdvancedTrace" class="flex flex-col gap-2 pt-2 border-t border-line-subtle">
						<div v-if="selectedMemory.supersededBy" class="text-xs text-warning flex items-center gap-1.5">
							<Icon name="alert" :size="13"/>
							<span>{{ I18N.detail.supersededBy }}: #{{ selectedMemory.supersededBy }}</span>
						</div>

						<div v-if="selectedAtoms.length > 0" class="flex flex-col gap-2 max-h-[14rem] scroll-area">
							<div
								v-for="atom in selectedAtoms"
								:key="atom.id"
								class="p-2.5 rounded bg-overlay-2 border border-line-subtle flex flex-col gap-1 text-sm"
							>
								<div class="flex flex-wrap items-center gap-1.5">
									<AppChip tone="teal">{{ atom.atomType }}</AppChip>
									<AppChip tone="warning">{{ I18N.add.importance }} {{ Math.round(atom.importance * 100) }}%</AppChip>
									<AppChip tone="neutral">{{ I18N.detail.confidence }} {{ Math.round(atom.confidence * 100) }}%</AppChip>
									<AppChip :tone="getStatusTone(atom.status)">{{ getStatusLabel(atom.status) }}</AppChip>
									<span class="text-xs text-text-faint mono">#{{ atom.id }}</span>
								</div>
								<p class="text-text-primary leading-relaxed">{{ atom.content }}</p>
								<div class="flex flex-wrap items-center justify-between gap-2 text-xs text-text-faint pt-1">
									<span v-if="atom.decayType">{{ I18N.detail.decayType }}: {{ atom.decayType }}</span>
									<span v-if="atom.entities">{{ atom.entities }}</span>
								</div>
							</div>
						</div>
						<p v-else class="text-xs text-text-faint py-1">{{ I18N.detail.noAtoms }}</p>
					</div>
				</div>
			</div>

			<template #footer>
				<div class="flex items-center justify-between w-full">
					<div class="flex items-center gap-2">
						<AppButton
							v-if="selectedMemory?.status !== 'archived'"
							variant="danger"
							size="sm"
							icon="package"
							@click="selectedMemory && (pendingArchive = selectedMemory)"
						>{{ I18N.list.archiveThis }}</AppButton>
						<AppButton
							v-else
							variant="primary"
							size="sm"
							icon="refresh"
							@click="selectedMemory && (pendingRestore = selectedMemory)"
						>{{ I18N.archive.restore }}</AppButton>
						<AppButton
							variant="danger"
							size="sm"
							icon="trash"
							@click="selectedMemory && (pendingDelete = selectedMemory)"
						>{{ I18N.list.delete }}</AppButton>
					</div>

					<div class="flex items-center gap-2">
						<AppButton size="sm" @click="requestCloseMemory">{{ I18N.common.cancel }}</AppButton>
						<AppButton
							variant="primary"
							size="sm"
							icon="check"
							:loading="savingDetail"
							:disabled="!hasUnsavedChanges || !editContent.trim()"
							@click="saveMemory"
						>{{ I18N.detail.save }}</AppButton>
					</div>
				</div>
			</template>
		</AppModal>

		<!-- 放弃未保存修改确认 -->
		<AppConfirm
			:show="unsavedConfirmOpen"
			:title="I18N.detail.unsavedTitle"
			:desc="I18N.detail.unsavedDesc"
			:confirm-label="I18N.detail.discardChanges"
			:cancel-label="I18N.detail.keepEditing"
			:close-label="I18N.common.close"
			tone="primary"
			@update:show="unsavedConfirmOpen = $event"
			@confirm="forceCloseMemory"
		/>

		<!-- 归档记忆确认 -->
		<AppConfirm
			:show="pendingArchive !== null"
			:title="I18N.detail.archiveConfirmTitle"
			:desc="I18N.detail.archiveConfirmDesc"
			:confirm-label="I18N.list.archiveThis"
			:cancel-label="I18N.common.cancel"
			:close-label="I18N.common.close"
			tone="primary"
			@update:show="closeArchiveConfirm"
			@confirm="confirmArchive"
		/>

		<!-- 恢复记忆确认 -->
		<AppConfirm
			:show="pendingRestore !== null"
			:title="I18N.detail.restoreConfirmTitle"
			:desc="I18N.detail.restoreConfirmDesc"
			:confirm-label="I18N.archive.restore"
			:cancel-label="I18N.common.cancel"
			:close-label="I18N.common.close"
			tone="primary"
			@update:show="closeRestoreConfirm"
			@confirm="confirmRestore"
		/>

		<!-- 清空全部记忆确认 -->
		<AppConfirm
			:show="clearAllOpen"
			:title="I18N.list.clearAll"
			:desc="I18N.list.clearQuestion"
			:confirm-label="I18N.list.clearConfirm"
			:cancel-label="I18N.common.cancel"
			:close-label="I18N.common.close"
			tone="danger"
			@update:show="clearAllOpen = $event"
			@confirm="confirmClearAll"
		/>

		<!-- 删除单条记忆确认 -->
		<AppConfirm
			:show="pendingDelete !== null"
			:title="I18N.list.deleteThis"
			:desc="I18N.list.deleteQuestion"
			:confirm-label="I18N.list.delete"
			:cancel-label="I18N.common.cancel"
			:close-label="I18N.common.close"
			tone="danger"
			@update:show="closeDeleteConfirm"
			@confirm="confirmDelete"
		/>

		<!-- 导入记忆确认 -->
		<AppConfirm
			:show="importConfirmOpen"
			:title="I18N.transfer.confirmModalTitle"
			:desc="I18N.transfer.confirmModalDesc"
			:confirm-label="I18N.transfer.confirmCommit"
			:cancel-label="I18N.common.cancel"
			:close-label="I18N.common.close"
			tone="primary"
			@update:show="importConfirmOpen = $event"
			@confirm="confirmCommitImport"
		/>
	</div>
</template>
