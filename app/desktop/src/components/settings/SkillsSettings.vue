<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import AppButton from "../ui/AppButton.vue"
import AppChip from "../ui/AppChip.vue"
import AppEmpty from "../ui/AppEmpty.vue"
import AppField from "../ui/AppField.vue"
import {feedback} from "../../services/feedback"
import {RUNTIME, type SkillDto} from "../../services/runtime"

const I18N = computed(() => useLanguages().views.main.skills)

// 当前子标签: "installed" | "market"
const activeTab = ref<"installed" | "market">("installed")

// 列表数据与加载状态
const installedSkills = ref<SkillDto[]>([])
const marketplaceSkills = ref<SkillDto[]>([])
const loading = ref(false)
const searchQuery = ref("")
const selectedCategory = ref<string>("all")

// 弹窗状态
const isUrlModalOpen = ref(false)
const installUrl = ref("")
const isUrlInstalling = ref(false)
const urlInstallError = ref("")

// 自定义/编辑技能弹窗
const isEditModalOpen = ref(false)
const isEditing = ref(false)
const editForm = ref({
	id: "",
	name: "",
	description: "",
	author: I18N.value.form.defaultAuthor,
	version: "1.0.0",
	tags: [] as string[],
	category: "productivity",
	instructions: "",
	tools: [] as string[],
	enabled: true,
	source: "custom" as string,
})
const tagsInput = ref("")
const toolsInput = ref("")

// 技能详情查看弹窗
const isDetailModalOpen = ref(false)
const activeSkill = ref<SkillDto | null>(null)

// 刷新已安装列表
const refresh = async () => {
	loading.value = true
	try {
		await RUNTIME.refresh()
		installedSkills.value = [...(RUNTIME.snapshot.value?.skills ?? [])]
	} catch (error) {
		feedback.error(I18N.value.toast.loadFailed, error)
	} finally {
		loading.value = false
	}
}

onMounted(async () => {
	await RUNTIME.init()
	await refresh()
	try {
		marketplaceSkills.value = await RUNTIME.skillsMarketplace()
	} catch (error) {
		feedback.error(I18N.value.toast.marketLoadFailed, error)
	}
})

// 已安装技能 ID 集合
const installedSkillIds = computed<Set<string>>(() => new Set(installedSkills.value.map(s => s.id)))

// 过滤器
const matchesFilter = (s: SkillDto): boolean => {
	const matchCategory = selectedCategory.value === "all" || s.category === selectedCategory.value
	const matchSearch = !searchQuery.value.trim() ||
		s.name.toLowerCase().includes(searchQuery.value.toLowerCase()) ||
		s.description.toLowerCase().includes(searchQuery.value.toLowerCase()) ||
		s.tags.some((t: string) => t.toLowerCase().includes(searchQuery.value.toLowerCase()))
	return matchCategory && matchSearch
}

const filteredInstalled = computed<SkillDto[]>(() => installedSkills.value.filter(matchesFilter))
const filteredMarket = computed<SkillDto[]>(() => marketplaceSkills.value.filter(matchesFilter))

// 分类列表
const CATEGORIES = computed<{key: string; label: string}[]>(() => [
	{key: "all", label: I18N.value.category.all},
	{key: "productivity", label: I18N.value.category.productivity},
	{key: "coding", label: I18N.value.category.coding},
	{key: "life", label: I18N.value.category.life},
	{key: "roleplay", label: I18N.value.category.roleplay},
	{key: "entertainment", label: I18N.value.category.entertainment},
])

// 切换技能启用
const toggleSkill = async (skill: SkillDto) => {
	try {
		await RUNTIME.skillsToggle(skill.id, !skill.enabled)
		await refresh()
		const LABEL = skill.enabled ? I18N.value.toast.disabled : I18N.value.toast.enabled
		feedback.success(`${LABEL} ${skill.name}`)
	} catch (error) {
		feedback.error(I18N.value.toast.toggleFailed, error)
	}
}

// 从市场安装
const installFromMarket = async (item: SkillDto) => {
	loading.value = true
	try {
		await RUNTIME.skillsSaveCustom({
			...item,
			enabled: true,
			installedAt: Date.now(),
		})
		await refresh()
		feedback.success(`${I18N.value.toast.installed} ${item.name}`)
	} catch (error) {
		feedback.error(I18N.value.toast.installFailed, error)
	} finally {
		loading.value = false
	}
}

// 打开从 URL 安装弹窗
const openUrlModal = () => {
	installUrl.value = ""
	urlInstallError.value = ""
	isUrlModalOpen.value = true
}

// 执行 URL 安装
const executeUrlInstall = async () => {
	if (!installUrl.value.trim()) return
	isUrlInstalling.value = true
	urlInstallError.value = ""

	try {
		await RUNTIME.skillsInstallUrl(installUrl.value.trim())
		isUrlModalOpen.value = false
		await refresh()
		feedback.success(I18N.value.toast.urlInstalled)
	} catch (error) {
		urlInstallError.value = error instanceof Error ? error.message : String(error)
		feedback.error(I18N.value.toast.urlInstallFailed, error)
	} finally {
		isUrlInstalling.value = false
	}
}

// 打开新建技能弹窗
const openNewSkillModal = () => {
	editForm.value = {
		id: `skill_custom_${Date.now().toString(36)}`,
		name: "",
		description: "",
		author: I18N.value.form.defaultAuthor,
		version: "1.0.0",
		tags: [],
		category: "productivity",
		instructions: "",
		tools: [],
		enabled: true,
		source: "custom",
	}
	tagsInput.value = ""
	toolsInput.value = ""
	isEditing.value = false
	isEditModalOpen.value = true
}

// 打开编辑技能弹窗 (指令正文按需从后端导出)
const openEditSkillModal = async (skill: SkillDto) => {
	let instructions = skill.instructions
	try {
		const EXPORTED = JSON.parse(await RUNTIME.skillsExport(skill.id)) as {instructions?: string}
		if (EXPORTED.instructions) instructions = EXPORTED.instructions
	} catch {
		/* 导出失败时使用快照中的占位 */
	}
	editForm.value = {
		id: skill.id,
		name: skill.name,
		description: skill.description,
		author: skill.author,
		version: skill.version,
		tags: [...skill.tags],
		category: skill.category,
		instructions,
		tools: [],
		enabled: skill.enabled,
		source: skill.source,
	}
	tagsInput.value = skill.tags.join(", ")
	toolsInput.value = ""
	isEditing.value = true
	isEditModalOpen.value = true
}

// 保存自定义/编辑技能
const saveSkill = async () => {
	if (!editForm.value.name.trim() || !editForm.value.instructions.trim()) return

	editForm.value.tags = tagsInput.value
		.split(/[,，\s]+/)
		.map(t => t.trim())
		.filter(Boolean)

	editForm.value.tools = toolsInput.value
		.split(/[,，\s]+/)
		.map(t => t.trim())
		.filter(Boolean)

	loading.value = true
	try {
		await RUNTIME.skillsSaveCustom({
			...editForm.value,
			icon: "sparkles",
			installedAt: Date.now(),
		})
		isEditModalOpen.value = false
		await refresh()
		feedback.success(isEditing.value ? I18N.value.toast.updated : I18N.value.toast.created)
	} catch (error) {
		feedback.error(I18N.value.toast.saveFailed, error)
	} finally {
		loading.value = false
	}
}

// 卸载技能
const deleteSkill = async (id: string) => {
	loading.value = true
	try {
		await RUNTIME.skillsUninstall(id)
		await refresh()
		feedback.success(I18N.value.toast.uninstalled)
	} catch (error) {
		feedback.error(I18N.value.toast.uninstallFailed, error)
	} finally {
		loading.value = false
	}
}

// 查看技能指令详情
const viewSkillDetail = async (skill: SkillDto) => {
	let instructions = skill.instructions
	try {
		const EXPORTED = JSON.parse(await RUNTIME.skillsExport(skill.id)) as {instructions?: string}
		if (EXPORTED.instructions) instructions = EXPORTED.instructions
	} catch {
		/* 忽略导出失败 */
	}
	activeSkill.value = {...skill, instructions}
	isDetailModalOpen.value = true
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-3 px-5 py-3.5 scroll-area">
		<!-- 头部导航与操作 -->
		<div class="flex items-center justify-between gap-2.5">
			<div class="flex gap-1.5">
				<button
					type="button"
					class="inline-flex items-center gap-1.5 px-[1.1rem] py-[0.55rem] rounded-sm border text-sm font-inherit
						cursor-pointer transition-all duration-200 focus-ring"
					:class="activeTab === 'installed'
						? 'text-nori-teal-bright border-nori-teal-bright/30 bg-nori-teal-bright/10'
						: 'text-text-muted border-line-subtle bg-white/3 hover:(text-text-primary border-nori-teal-soft)'"
					:aria-pressed="activeTab === 'installed'"
					@click="activeTab = 'installed'"
				>
					<Icon name="sparkles" :size="14"/>
					<span>{{ I18N.tab.installed }} ({{ installedSkills.length }})</span>
				</button>
				<button
					type="button"
					class="inline-flex items-center gap-1.5 px-[1.1rem] py-[0.55rem] rounded-sm border text-sm font-inherit
						cursor-pointer transition-all duration-200 focus-ring"
					:class="activeTab === 'market'
						? 'text-nori-teal-bright border-nori-teal-bright/30 bg-nori-teal-bright/10'
						: 'text-text-muted border-line-subtle bg-white/3 hover:(text-text-primary border-nori-teal-soft)'"
					:aria-pressed="activeTab === 'market'"
					@click="activeTab = 'market'"
				>
					<Icon name="package" :size="14"/>
					<span>{{ I18N.tab.market }}</span>
				</button>
			</div>

			<div class="flex gap-1.5">
				<AppButton variant="ghost" size="sm" icon="external-link" @click="openUrlModal">{{ I18N.action.installFromUrl }}</AppButton>
				<AppButton variant="primary" size="sm" icon="plus" @click="openNewSkillModal">{{ I18N.action.newSkill }}</AppButton>
			</div>
		</div>

		<!-- 搜索与分类过滤条 -->
		<div class="flex items-center gap-2.5 flex-wrap">
			<div class="flex-1 min-w-[14rem]">
				<input
					v-model="searchQuery"
					class="input-base rounded-pill text-sm"
					:placeholder="I18N.search.placeholder"
				/>
			</div>

			<div class="flex gap-1.5 flex-wrap">
				<button
					v-for="cat in CATEGORIES"
					:key="cat.key"
					type="button"
					class="px-[0.9rem] py-1 rounded-pill border text-xs font-inherit cursor-pointer transition-all duration-200 focus-ring"
					:class="selectedCategory === cat.key
						? 'text-nori-teal-bright border-nori-teal-bright/35 bg-nori-teal-bright/8'
						: 'text-text-faint border-line-subtle bg-transparent hover:(text-text-body border-nori-teal-soft)'"
					:aria-pressed="selectedCategory === cat.key"
					@click="selectedCategory = cat.key"
				>
					{{ cat.label }}
				</button>
			</div>
		</div>

		<!-- 主内容区 -->
		<div class="flex-1 min-h-0">
			<!-- 1. 已安装技能列表 -->
			<div v-if="activeTab === 'installed'" class="h-full flex flex-col min-h-0">
				<AppEmpty
					v-if="filteredInstalled.length === 0"
					icon="sparkles"
					:title="I18N.empty.title"
					:desc="I18N.empty.desc"
				>
					<AppButton variant="ghost" size="sm" @click="activeTab = 'market'">{{ I18N.empty.action }}</AppButton>
				</AppEmpty>

				<div v-else class="grid gap-3 grid-cols-[repeat(auto-fill,minmax(26rem,1fr))] scroll-area flex-1">
					<div
						v-for="skill in filteredInstalled"
						:key="skill.id"
						class="surface-card flex flex-col gap-2 px-3.5 py-3 border transition-all duration-200
							hover:(border-nori-teal-soft bg-nori-teal-bright/6)"
						:class="skill.enabled ? 'border-nori-teal-bright/35' : 'border-line-subtle'"
					>
						<div class="flex items-start justify-between gap-2">
							<div class="flex gap-2 min-w-0">
								<div class="w-8 h-8 shrink-0 flex items-center justify-center rounded-sm bg-nori-teal-bright/10 text-nori-teal-bright">
									<Icon :name="skill.icon || 'sparkles'" :size="18"/>
								</div>
								<div class="min-w-0">
									<div class="flex items-center gap-1.5 flex-wrap">
										<h4 class="m-0 text-base font-600 text-text-primary">{{ skill.name }}</h4>
										<span class="text-xs text-text-faint mono">v{{ skill.version }}</span>
										<AppChip>
											{{ skill.source === 'builtin' ? I18N.source.builtin : (skill.source === 'market' ? I18N.source.market : (skill.source === 'url' ? I18N.source.url : I18N.source.custom)) }}
										</AppChip>
									</div>
									<span class="text-xs text-text-faint">by {{ skill.author }}</span>
								</div>
							</div>

							<div class="shrink-0">
								<n-switch
									:value="skill.enabled"
									@update:value="() => toggleSkill(skill)"
								/>
							</div>
						</div>

						<p class="m-0 flex-1 text-xs text-text-muted leading-relaxed">{{ skill.description }}</p>

						<div class="flex gap-1 flex-wrap">
							<span v-for="tag in skill.tags" :key="tag" class="text-xs text-nori-teal-soft">#{{ tag }}</span>
						</div>

						<div class="flex gap-1.5 flex-wrap">
							<AppButton variant="ghost" size="sm" icon="info" @click="viewSkillDetail(skill)">{{ I18N.action.viewInstructions }}</AppButton>
							<AppButton variant="ghost" size="sm" icon="edit" @click="openEditSkillModal(skill)">{{ I18N.action.edit }}</AppButton>
							<n-popconfirm
								v-if="skill.source !== 'builtin'"
								:positive-text="I18N.uninstall.confirm"
								:negative-text="I18N.common.cancel"
								@positive-click="deleteSkill(skill.id)"
							>
								<template #trigger>
									<button type="button" class="btn-danger px-3 py-1.5 text-sm">
										<Icon name="trash" :size="13"/>
										<span>{{ I18N.action.uninstall }}</span>
									</button>
								</template>
								{{ I18N.uninstall.questionPrefix }}{{ skill.name }}{{ I18N.uninstall.questionSuffix }}
							</n-popconfirm>
						</div>
					</div>
				</div>
			</div>

			<!-- 2. 技能工坊 / 市场 -->
			<div v-else class="h-full flex flex-col min-h-0">
				<div class="grid gap-3 grid-cols-[repeat(auto-fill,minmax(26rem,1fr))] scroll-area flex-1">
					<div
						v-for="item in filteredMarket"
						:key="item.id"
						class="surface-card flex flex-col gap-2 px-3.5 py-3 border border-line-subtle transition-all duration-200
							hover:(border-nori-teal-soft bg-nori-teal-bright/6)"
					>
						<div class="flex items-start justify-between gap-2">
							<div class="flex gap-2 min-w-0">
								<div class="w-8 h-8 shrink-0 flex items-center justify-center rounded-sm bg-nori-teal-bright/10 text-nori-teal-bright">
									<Icon :name="item.icon || 'package'" :size="18"/>
								</div>
								<div class="min-w-0">
									<div class="flex items-center gap-1.5 flex-wrap">
										<h4 class="m-0 text-base font-600 text-text-primary">{{ item.name }}</h4>
										<span class="text-xs text-text-faint mono">v{{ item.version }}</span>
									</div>
									<span class="text-xs text-text-faint">by {{ item.author }}</span>
								</div>
							</div>

							<div class="shrink-0">
								<AppChip v-if="installedSkillIds.has(item.id)" tone="success" icon="check">{{ I18N.market.installedChip }}</AppChip>
								<AppButton
									v-else
									variant="primary"
									size="sm"
									icon="plus"
									class="rounded-pill"
									:disabled="loading"
									@click="installFromMarket(item)"
								>
									{{ I18N.market.install }}
								</AppButton>
							</div>
						</div>

						<p class="m-0 flex-1 text-xs text-text-muted leading-relaxed">{{ item.description }}</p>

						<div class="flex gap-1 flex-wrap">
							<span v-for="tag in item.tags" :key="tag" class="text-xs text-nori-teal-soft">#{{ tag }}</span>
						</div>

						<div class="flex gap-1.5 flex-wrap">
							<AppButton variant="ghost" size="sm" icon="info" @click="viewSkillDetail(item)">{{ I18N.market.viewPrompt }}</AppButton>
						</div>
					</div>
				</div>
			</div>
		</div>

		<!-- 从 URL 安装弹窗 -->
		<div
			v-if="isUrlModalOpen"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/70 backdrop-blur-[0.4rem]"
			@click.self="isUrlModalOpen = false"
		>
			<div class="w-[min(46rem,92vw)] max-h-[84vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg shadow-[0_1.6rem_4.8rem_rgba(0,0,0,0.7)]">
				<div class="flex items-center justify-between gap-2 px-4 py-3 border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ I18N.urlModal.title }}</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="isUrlModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="flex flex-col gap-[1.1rem] px-4 py-3.5 scroll-area">
					<p class="m-0 text-xs text-text-muted leading-relaxed">
						{{ I18N.urlModal.hintBefore }} <code class="mono">SKILL.md</code> {{ I18N.urlModal.hintAfter }}
					</p>

					<AppField :label="I18N.urlModal.urlLabel">
						<input
							v-model="installUrl"
							class="input-base text-sm"
							placeholder="https://raw.githubusercontent.com/.../SKILL.md"
						/>
					</AppField>

					<div
						v-if="urlInstallError"
						class="flex items-center gap-1.5 px-2.5 py-[0.7rem] rounded-sm text-xs text-danger-text bg-danger/10 border border-danger/30"
						role="alert"
					>
						<Icon name="alert" :size="13"/>
						<span>{{ urlInstallError }}</span>
					</div>
				</div>

				<div class="flex justify-end gap-2 px-4 py-[1.1rem] border-t border-line-subtle">
					<AppButton variant="ghost" @click="isUrlModalOpen = false">{{ I18N.common.cancel }}</AppButton>
					<AppButton
						variant="primary"
						:loading="isUrlInstalling"
						:disabled="isUrlInstalling || !installUrl.trim()"
						@click="executeUrlInstall"
					>
						{{ isUrlInstalling ? I18N.urlModal.downloading : I18N.urlModal.submit }}
					</AppButton>
				</div>
			</div>
		</div>

		<!-- 新建 / 编辑技能弹窗 -->
		<div
			v-if="isEditModalOpen"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/70 backdrop-blur-[0.4rem]"
			@click.self="isEditModalOpen = false"
		>
			<div class="w-[min(60rem,94vw)] max-h-[84vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg shadow-[0_1.6rem_4.8rem_rgba(0,0,0,0.7)]">
				<div class="flex items-center justify-between gap-2 px-4 py-3 border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ isEditing ? I18N.editModal.titleEdit : I18N.editModal.titleCreate }}</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="isEditModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="flex flex-col gap-[1.1rem] px-4 py-3.5 scroll-area">
					<div class="grid grid-cols-2 gap-2.5">
						<AppField :label="I18N.editModal.name">
							<input v-model="editForm.name" class="input-base text-sm" :placeholder="I18N.editModal.namePlaceholder"/>
						</AppField>
						<AppField :label="I18N.editModal.author">
							<input v-model="editForm.author" class="input-base text-sm" :placeholder="I18N.editModal.authorPlaceholder"/>
						</AppField>
					</div>

					<AppField :label="I18N.editModal.description">
						<input v-model="editForm.description" class="input-base text-sm" :placeholder="I18N.editModal.descriptionPlaceholder"/>
					</AppField>

					<AppField :label="I18N.editModal.instructions">
						<textarea
							v-model="editForm.instructions"
							class="input-base text-sm resize-y leading-relaxed"
							rows="6"
							:placeholder="I18N.editModal.instructionsPlaceholder"
						/>
					</AppField>

					<div class="grid grid-cols-2 gap-2.5">
						<AppField :label="I18N.editModal.tags">
							<input v-model="tagsInput" class="input-base text-sm" :placeholder="I18N.editModal.tagsPlaceholder"/>
						</AppField>
						<AppField :label="I18N.editModal.tools">
							<input v-model="toolsInput" class="input-base text-sm" placeholder="calculate, searchWeb"/>
						</AppField>
					</div>
				</div>

				<div class="flex justify-end gap-2 px-4 py-[1.1rem] border-t border-line-subtle">
					<AppButton variant="ghost" @click="isEditModalOpen = false">{{ I18N.common.cancel }}</AppButton>
					<AppButton
						variant="primary"
						:disabled="!editForm.name.trim() || !editForm.instructions.trim()"
						@click="saveSkill"
					>
						{{ isEditing ? I18N.editModal.submitEdit : I18N.editModal.submitCreate }}
					</AppButton>
				</div>
			</div>
		</div>

		<!-- 技能详情弹窗 -->
		<div
			v-if="isDetailModalOpen && activeSkill"
			class="fixed inset-0 z-100 flex items-center justify-center bg-bg-abyss/70 backdrop-blur-[0.4rem]"
			@click.self="isDetailModalOpen = false"
		>
			<div class="w-[min(60rem,94vw)] max-h-[84vh] flex flex-col bg-bg-glass-modal border border-line-strong rounded-lg shadow-[0_1.6rem_4.8rem_rgba(0,0,0,0.7)]">
				<div class="flex items-center justify-between gap-2 px-4 py-3 border-b border-line-subtle">
					<h3 class="m-0 text-md text-text-primary">{{ activeSkill.name }} (v{{ activeSkill.version }})</h3>
					<button type="button" class="btn-close" :aria-label="I18N.common.close" @click="isDetailModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>
				<div class="flex flex-col gap-[1.1rem] px-4 py-3.5 scroll-area">
					<p class="m-0 text-xs text-text-muted leading-relaxed">{{ activeSkill.description }}</p>
					<pre class="m-0 p-2.5 rounded-sm bg-white/4 border border-line-subtle text-sm text-text-body leading-relaxed whitespace-pre-wrap font-inherit">{{ activeSkill.instructions || I18N.detail.emptyInstructions }}</pre>
				</div>
			</div>
		</div>
	</div>
</template>
