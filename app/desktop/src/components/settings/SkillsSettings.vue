<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {useMessage} from "naive-ui"
import Icon from "../Icon.vue"
import {RUNTIME, type SkillDto} from "../../services/runtime"

const message = useMessage()

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
	author: "我",
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
		console.error("加载技能市场失败:", error)
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
const CATEGORIES: {key: string; label: string}[] = [
	{key: "all", label: "全部技能"},
	{key: "productivity", label: "生产力与效率"},
	{key: "coding", label: "编程与架构"},
	{key: "life", label: "生活与学习"},
	{key: "roleplay", label: "情感与角色"},
	{key: "entertainment", label: "游戏与娱乐"},
]

// 切换技能启用
const toggleSkill = async (skill: SkillDto) => {
	await RUNTIME.skillsToggle(skill.id, !skill.enabled)
	await refresh()
	message.success(skill.enabled ? `已停用技能 ${skill.name}` : `已激活技能 ${skill.name}`)
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
		message.success(`成功安装技能「${item.name}」！`)
	} catch (error) {
		message.error(`安装失败: ${error instanceof Error ? error.message : String(error)}`)
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
		message.success("成功从 URL 导入并安装新技能！")
	} catch (error) {
		urlInstallError.value = error instanceof Error ? error.message : String(error)
		message.error(`导入失败: ${urlInstallError.value}`)
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
		author: "我",
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
		message.success(isEditing.value ? "技能更新成功！" : "新技能创建成功！")
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
		message.success("技能已成功卸载")
	} catch (error) {
		message.error(`卸载失败: ${error instanceof Error ? error.message : String(error)}`)
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
	<div class="skills-view">
		<!-- 头部导航与操作 -->
		<div class="skills-header">
			<div class="skills-tabs">
				<button
					class="tab-btn"
					:class="{active: activeTab === 'installed'}"
					@click="activeTab = 'installed'"
				>
					<Icon name="sparkles" :size="14"/>
					<span>已安装技能 ({{ installedSkills.length }})</span>
				</button>
				<button
					class="tab-btn"
					:class="{active: activeTab === 'market'}"
					@click="activeTab = 'market'"
				>
					<Icon name="package" :size="14"/>
					<span>技能工坊 / 市场</span>
				</button>
			</div>

			<div class="skills-top-ops">
				<button class="btn-top" @click="openUrlModal">
					<Icon name="external-link" :size="13"/>
					<span>从 URL 安装</span>
				</button>
				<button class="btn-primary-sm" @click="openNewSkillModal">
					<Icon name="plus" :size="13"/>
					<span>新建技能</span>
				</button>
			</div>
		</div>

		<!-- 搜索与分类过滤条 -->
		<div class="filter-bar">
			<div class="search-wrap">
				<input
					v-model="searchQuery"
					class="search-input"
					placeholder="搜索技能名称、描述或标签..."
				/>
			</div>

			<div class="category-chips">
				<button
					v-for="cat in CATEGORIES"
					:key="cat.key"
					class="cat-chip"
					:class="{active: selectedCategory === cat.key}"
					@click="selectedCategory = cat.key"
				>
					{{ cat.label }}
				</button>
			</div>
		</div>

		<!-- 主内容区 -->
		<div class="skills-body">
			<!-- 1. 已安装技能列表 -->
			<div v-if="activeTab === 'installed'">
				<div v-if="filteredInstalled.length === 0" class="empty-box">
					<Icon name="sparkles" class="empty-icon" :size="32"/>
					<p class="empty-title">未找到匹配的技能</p>
					<p class="empty-desc">你可以前往「技能工坊」一键安装预设技能，或点击上方「从 URL 安装」获取社区技能。</p>
					<button class="btn-outline" @click="activeTab = 'market'">浏览技能工坊</button>
				</div>

				<div v-else class="skills-grid">
					<div
						v-for="skill in filteredInstalled"
						:key="skill.id"
						class="skill-card"
						:class="{active: skill.enabled}"
					>
						<div class="skill-card-top">
							<div class="skill-main-meta">
								<div class="skill-icon-wrap">
									<Icon :name="(skill.icon || 'sparkles') as any" :size="18"/>
								</div>
								<div>
									<div class="skill-title-row">
										<h4 class="skill-name">{{ skill.name }}</h4>
										<span class="skill-version">v{{ skill.version }}</span>
										<span class="source-tag" :class="skill.source">
											{{ skill.source === 'builtin' ? '内置' : (skill.source === 'market' ? '市场' : (skill.source === 'url' ? '网络' : '自定义')) }}
										</span>
									</div>
									<span class="skill-author">by {{ skill.author }}</span>
								</div>
							</div>

							<div class="skill-switch-wrap">
								<n-switch
									:value="skill.enabled"
									@update:value="() => toggleSkill(skill)"
								/>
							</div>
						</div>

						<p class="skill-desc">{{ skill.description }}</p>

						<div class="skill-tags">
							<span v-for="tag in skill.tags" :key="tag" class="skill-tag">#{{ tag }}</span>
						</div>

						<div class="skill-card-footer">
							<button class="btn-card-action" @click="viewSkillDetail(skill)">
								<Icon name="info" :size="12"/>
								<span>查看指令</span>
							</button>
							<button class="btn-card-action" @click="openEditSkillModal(skill)">
								<Icon name="edit" :size="12"/>
								<span>编辑</span>
							</button>
							<n-popconfirm
								v-if="skill.source !== 'builtin'"
								positive-text="确定卸载"
								negative-text="取消"
								@positive-click="deleteSkill(skill.id)"
							>
								<template #trigger>
									<button class="btn-card-action btn-danger">
										<Icon name="trash" :size="12"/>
										<span>卸载</span>
									</button>
								</template>
								确定要卸载技能「{{ skill.name }}」吗？
							</n-popconfirm>
						</div>
					</div>
				</div>
			</div>

			<!-- 2. 技能工坊 / 市场 -->
			<div v-else>
				<div class="skills-grid">
					<div
						v-for="item in filteredMarket"
						:key="item.id"
						class="skill-card market-card"
					>
						<div class="skill-card-top">
							<div class="skill-main-meta">
								<div class="skill-icon-wrap">
									<Icon :name="(item.icon || 'package') as any" :size="18"/>
								</div>
								<div>
									<div class="skill-title-row">
										<h4 class="skill-name">{{ item.name }}</h4>
										<span class="skill-version">v{{ item.version }}</span>
									</div>
									<span class="skill-author">by {{ item.author }}</span>
								</div>
							</div>

							<div>
								<span v-if="installedSkillIds.has(item.id)" class="badge-installed">
									<Icon name="check" :size="11"/>
									<span>已安装</span>
								</span>
								<button
									v-else
									class="btn-install"
									:disabled="loading"
									@click="installFromMarket(item)"
								>
									<Icon name="plus" :size="12"/>
									<span>一键安装</span>
								</button>
							</div>
						</div>

						<p class="skill-desc">{{ item.description }}</p>

						<div class="skill-tags">
							<span v-for="tag in item.tags" :key="tag" class="skill-tag">#{{ tag }}</span>
						</div>

						<div class="skill-card-footer">
							<button class="btn-card-action" @click="viewSkillDetail(item)">
								<Icon name="info" :size="12"/>
								<span>查看提示词与机制</span>
							</button>
						</div>
					</div>
				</div>
			</div>
		</div>

		<!-- 从 URL 安装弹窗 -->
		<div v-if="isUrlModalOpen" class="modal-overlay" @click.self="isUrlModalOpen = false">
			<div class="modal-card">
				<div class="modal-header">
					<h3>从网络 URL 安装技能 (SKILL.md / JSON)</h3>
					<button class="btn-close" @click="isUrlModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<p class="modal-hint">
						支持输入 GitHub Raw 链接、Gist 链接或任意在线托管的 <code>SKILL.md</code> 与 JSON 格式技能清单 (由后端安全抓取)。
					</p>

					<div class="field-row">
						<label class="field-label">远程文件 URL 地址</label>
						<input
							v-model="installUrl"
							class="input"
							placeholder="https://raw.githubusercontent.com/.../SKILL.md"
						/>
					</div>

					<div v-if="urlInstallError" class="error-box">
						<Icon name="alert" :size="13"/>
						<span>{{ urlInstallError }}</span>
					</div>
				</div>

				<div class="modal-footer">
					<button class="btn-ghost" @click="isUrlModalOpen = false">取消</button>
					<button
						class="btn-primary"
						:disabled="isUrlInstalling || !installUrl.trim()"
						@click="executeUrlInstall"
					>
						<Icon v-if="isUrlInstalling" name="loading" class="spin" :size="13"/>
						<span>{{ isUrlInstalling ? '下载并解析中...' : '开始安装' }}</span>
					</button>
				</div>
			</div>
		</div>

		<!-- 新建 / 编辑技能弹窗 -->
		<div v-if="isEditModalOpen" class="modal-overlay" @click.self="isEditModalOpen = false">
			<div class="modal-card modal-large">
				<div class="modal-header">
					<h3>{{ isEditing ? '编辑技能' : '新建自定义技能' }}</h3>
					<button class="btn-close" @click="isEditModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>

				<div class="modal-body">
					<div class="form-grid-2">
						<div class="field-row">
							<label class="field-label">技能名称</label>
							<input v-model="editForm.name" class="input" placeholder="例如: 极客代码导师"/>
						</div>
						<div class="field-row">
							<label class="field-label">作者</label>
							<input v-model="editForm.author" class="input" placeholder="作者昵称"/>
						</div>
					</div>

					<div class="field-row">
						<label class="field-label">技能简介</label>
						<input v-model="editForm.description" class="input" placeholder="简短说明该技能如何辅助主人"/>
					</div>

					<div class="field-row">
						<label class="field-label">技能指令 (注入 System Prompt 的行为指引)</label>
						<textarea v-model="editForm.instructions" class="input textarea" rows="6"
							placeholder="描述该技能激活后 Nori 应遵循的行为规则..."/>
					</div>

					<div class="form-grid-2">
						<div class="field-row">
							<label class="field-label">标签 (逗号分隔)</label>
							<input v-model="tagsInput" class="input" placeholder="编程, 效率"/>
						</div>
						<div class="field-row">
							<label class="field-label">推荐工具 (逗号分隔, 可选)</label>
							<input v-model="toolsInput" class="input" placeholder="calculate, searchWeb"/>
						</div>
					</div>
				</div>

				<div class="modal-footer">
					<button class="btn-ghost" @click="isEditModalOpen = false">取消</button>
					<button
						class="btn-primary"
						:disabled="!editForm.name.trim() || !editForm.instructions.trim()"
						@click="saveSkill"
					>
						<span>{{ isEditing ? '保存修改' : '创建技能' }}</span>
					</button>
				</div>
			</div>
		</div>

		<!-- 技能详情弹窗 -->
		<div v-if="isDetailModalOpen && activeSkill" class="modal-overlay" @click.self="isDetailModalOpen = false">
			<div class="modal-card modal-large">
				<div class="modal-header">
					<h3>{{ activeSkill.name }} (v{{ activeSkill.version }})</h3>
					<button class="btn-close" @click="isDetailModalOpen = false">
						<Icon name="close" :size="16"/>
					</button>
				</div>
				<div class="modal-body">
					<p class="modal-hint">{{ activeSkill.description }}</p>
					<pre class="instructions-box">{{ activeSkill.instructions || '(无指令正文)' }}</pre>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.skills-view {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	padding: 1.4rem 2rem;
	gap: 1.2rem;
	overflow-y: auto;
}

.skills-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	gap: 1rem;
}

.skills-tabs {
	display: flex;
	gap: 0.6rem;
}

.tab-btn {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.55rem 1.1rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-muted);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;

	&.active {
		color: var(--nori-teal-bright);
		border-color: rgba(125, 227, 255, 0.3);
		background: rgba(125, 227, 255, 0.1);
	}
}

.skills-top-ops {
	display: flex;
	gap: 0.6rem;
}

.btn-top, .btn-primary-sm {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	padding: 0.5rem 1rem;
	border-radius: var(--radius-sm);
	font-size: 1.15rem;
	font-family: inherit;
	cursor: pointer;
}

.btn-top {
	border: 0.1rem solid var(--line-subtle);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-muted);
}

.btn-primary-sm {
	border: none;
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	font-weight: 600;
}

.filter-bar {
	display: flex;
	align-items: center;
	gap: 1rem;
	flex-wrap: wrap;
}

.search-input {
	flex: 1;
	min-width: 14rem;
	padding: 0.6rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	color: var(--text-primary);
	font-size: 1.2rem;
	font-family: inherit;
	outline: none;

	&:focus {
		border-color: var(--nori-teal);
	}
}

.category-chips {
	display: flex;
	gap: 0.5rem;
	flex-wrap: wrap;
}

.cat-chip {
	padding: 0.4rem 0.9rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: transparent;
	color: var(--text-faint);
	font-size: 1.1rem;
	font-family: inherit;
	cursor: pointer;

	&.active {
		color: var(--nori-teal-bright);
		border-color: rgba(125, 227, 255, 0.35);
		background: rgba(125, 227, 255, 0.08);
	}
}

.skills-body {
	flex: 1;
}

.empty-box {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	padding: 3rem 1rem;
	text-align: center;

	.empty-icon {
		color: var(--text-faint);
	}

	.empty-title {
		margin: 0;
		font-size: 1.4rem;
		font-weight: 600;
		color: var(--text-primary);
	}

	.empty-desc {
		margin: 0;
		font-size: 1.15rem;
		color: var(--text-faint);
		max-width: 34rem;
	}
}

.btn-outline {
	padding: 0.5rem 1.2rem;
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--nori-teal-bright);
	font-size: 1.15rem;
	font-family: inherit;
	cursor: pointer;
}

.skills-grid {
	display: grid;
	grid-template-columns: repeat(auto-fill, minmax(26rem, 1fr));
	gap: 1.2rem;
}

.skill-card {
	padding: 1.2rem 1.4rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.03);
	display: flex;
	flex-direction: column;
	gap: 0.8rem;

	&.active {
		border-color: rgba(125, 227, 255, 0.35);
	}
}

.skill-card-top {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
	gap: 0.8rem;
}

.skill-main-meta {
	display: flex;
	gap: 0.8rem;
}

.skill-icon-wrap {
	width: 3.2rem;
	height: 3.2rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.1);
	color: var(--nori-teal-bright);
	flex-shrink: 0;
}

.skill-title-row {
	display: flex;
	align-items: center;
	gap: 0.6rem;
	flex-wrap: wrap;
}

.skill-name {
	margin: 0;
	font-size: 1.3rem;
	font-weight: 600;
	color: var(--text-primary);
}

.skill-version {
	font-size: 1.05rem;
	color: var(--text-faint);
	font-family: monospace;
}

.source-tag {
	font-size: 1rem;
	padding: 0.1rem 0.5rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.06);
	color: var(--text-muted);
}

.skill-author {
	font-size: 1.05rem;
	color: var(--text-faint);
}

.skill-desc {
	margin: 0;
	font-size: 1.15rem;
	color: var(--text-muted);
	line-height: 1.45;
	flex: 1;
}

.skill-tags {
	display: flex;
	gap: 0.4rem;
	flex-wrap: wrap;
}

.skill-tag {
	font-size: 1.05rem;
	color: var(--nori-teal-soft);
}

.skill-card-footer {
	display: flex;
	gap: 0.5rem;
	flex-wrap: wrap;
}

.btn-card-action {
	display: inline-flex;
	align-items: center;
	gap: 0.35rem;
	padding: 0.35rem 0.8rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-muted);
	font-size: 1.1rem;
	font-family: inherit;
	cursor: pointer;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}

	&.btn-danger:hover {
		color: var(--danger);
		border-color: rgba(251, 60, 68, 0.4);
	}
}

.badge-installed {
	display: inline-flex;
	align-items: center;
	gap: 0.35rem;
	padding: 0.35rem 0.8rem;
	border-radius: var(--radius-pill);
	background: rgba(32, 224, 144, 0.12);
	color: #20e090;
	font-size: 1.1rem;
}

.btn-install {
	display: inline-flex;
	align-items: center;
	gap: 0.35rem;
	padding: 0.4rem 1rem;
	border: none;
	border-radius: var(--radius-pill);
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	font-size: 1.15rem;
	font-weight: 600;
	font-family: inherit;
	cursor: pointer;

	&:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
}

.modal-overlay {
	position: fixed;
	inset: 0;
	background: rgba(2, 8, 16, 0.7);
	backdrop-filter: blur(0.4rem);
	display: flex;
	align-items: center;
	justify-content: center;
	z-index: 100;
}

.modal-card {
	width: min(46rem, 92vw);
	max-height: 84vh;
	display: flex;
	flex-direction: column;
	background: #0a1a2c;
	border: 0.1rem solid var(--line-strong);
	border-radius: var(--radius-lg);
	box-shadow: 0 1.6rem 4.8rem rgba(0, 0, 0, 0.7);

	&.modal-large {
		width: min(60rem, 94vw);
	}
}

.modal-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1.2rem 1.6rem;
	border-bottom: 0.1rem solid var(--line-subtle);

	h3 {
		margin: 0;
		font-size: 1.4rem;
		color: var(--text-primary);
	}
}

.btn-close {
	border: none;
	background: transparent;
	color: var(--text-faint);
	cursor: pointer;
	display: flex;
	padding: 0.2rem;

	&:hover {
		color: var(--text-primary);
	}
}

.modal-body {
	padding: 1.4rem 1.6rem;
	overflow-y: auto;
	display: flex;
	flex-direction: column;
	gap: 1.1rem;
}

.modal-hint {
	margin: 0;
	font-size: 1.15rem;
	color: var(--text-muted);
	line-height: 1.5;
}

.instructions-box {
	margin: 0;
	padding: 1rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-body);
	font-size: 1.2rem;
	line-height: 1.6;
	white-space: pre-wrap;
	font-family: inherit;
}

.field-row {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.field-label {
	font-size: 1.15rem;
	color: var(--text-muted);
}

.input {
	padding: 0.7rem 1rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.2rem;
	font-family: inherit;
	outline: none;

	&:focus {
		border-color: var(--nori-teal);
	}
}

.textarea {
	resize: vertical;
	line-height: 1.5;
}

.form-grid-2 {
	display: grid;
	grid-template-columns: 1fr 1fr;
	gap: 1rem;
}

.error-box {
	display: flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.7rem 1rem;
	background: rgba(251, 60, 68, 0.1);
	border: 0.1rem solid rgba(251, 60, 68, 0.3);
	border-radius: var(--radius-sm);
	color: var(--danger);
	font-size: 1.15rem;
}

.modal-footer {
	display: flex;
	justify-content: flex-end;
	gap: 0.8rem;
	padding: 1.1rem 1.6rem;
	border-top: 0.1rem solid var(--line-subtle);
}

.btn-ghost {
	padding: 0.55rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: transparent;
	color: var(--text-muted);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;
}

.btn-primary {
	padding: 0.55rem 1.4rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	font-size: 1.2rem;
	font-weight: 600;
	font-family: inherit;
	cursor: pointer;

	&:disabled {
		opacity: 0.5;
		cursor: not-allowed;
	}
}

.spin {
	animation: spin 1s linear infinite;
}

@keyframes spin {
	to { transform: rotate(360deg); }
}
</style>
