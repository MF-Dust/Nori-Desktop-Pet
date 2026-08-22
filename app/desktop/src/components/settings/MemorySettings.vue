<script setup lang="ts">
import {onMounted, ref} from "vue"
import {memoryService, type MemoryItem} from "../../services/memory"
import {invoke} from "../../services/host/invoke"
import {readStringConfig} from "../../services/config"
import Icon from "../Icon.vue"

const memories = ref<MemoryItem[]>([])
const searchKeyword = ref("")
const loading = ref(false)

// Embedding 配置
const embeddingModel = ref("BAAI/bge-m3")
const embeddingBaseUrl = ref("")
const embeddingApiKey = ref("")
const embeddingDimensions = ref("")
const isReembedding = ref(false)
const reembedMessage = ref("")

// 新建记忆
const newContent = ref("")
const newImportance = ref(0.8)
const newTags = ref("")
const adding = ref(false)

// 加载记忆列表
const loadMemories = async () => {
	loading.value = true
	try {
		if (searchKeyword.value.trim()) {
			memories.value = await memoryService.searchHybrid(searchKeyword.value.trim(), 50)
		} else {
			memories.value = await memoryService.getAll(50)
		}
	} catch (error) {
		console.error("加载记忆列表失败:", error)
	} finally {
		loading.value = false
	}
}

onMounted(async () => {
	try {
		const [SAVED_MODEL, SAVED_BASE, SAVED_KEY, SAVED_DIMS] = await Promise.all([
			readStringConfig("embedding_model", embeddingModel.value),
			readStringConfig("embedding_api_base", ""),
			readStringConfig("embedding_api_key", ""),
			readStringConfig("embedding_dimensions", ""),
		])
		if (SAVED_MODEL) embeddingModel.value = SAVED_MODEL
		if (SAVED_BASE) embeddingBaseUrl.value = SAVED_BASE
		if (SAVED_KEY) embeddingApiKey.value = SAVED_KEY
		if (SAVED_DIMS) embeddingDimensions.value = SAVED_DIMS
	} catch (error) {
		console.error("加载 Embedding 配置失败:", error)
	}
	void loadMemories()
})

const saveConfig = (key: string, value: string) => {
	void invoke("set_config", {key, value})
}

// 保存维数: 留空表示用模型默认; 非正整数一律回退为空
const saveDimensions = () => {
	const RAW = embeddingDimensions.value.trim()
	if (RAW === "") {
		saveConfig("embedding_dimensions", "")
		return
	}
	const NUM = Number.parseInt(RAW, 10)
	if (Number.isNaN(NUM) || NUM <= 0) {
		embeddingDimensions.value = ""
		saveConfig("embedding_dimensions", "")
		return
	}
	embeddingDimensions.value = String(NUM)
	saveConfig("embedding_dimensions", String(NUM))
}

// 重新计算向量嵌入
const reembedAll = async () => {
	if (isReembedding.value) return
	isReembedding.value = true
	reembedMessage.value = "正在计算向量..."
	try {
		const COUNT = await memoryService.reembedAll()
		reembedMessage.value = `成功为 ${COUNT} 条记忆生成了向量索引！`
		await loadMemories()
	} catch (error) {
		reembedMessage.value = "向量生成失败，请检查 Embedding 接口配置"
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
		await memoryService.add(
			newContent.value.trim(),
			"manual",
			newImportance.value,
			newTags.value.trim() || undefined
		)
		newContent.value = ""
		newTags.value = ""
		await loadMemories()
	} catch (error) {
		console.error("添加记忆失败:", error)
	} finally {
		adding.value = false
	}
}

// 删除记忆
const deleteMemory = async (id: number) => {
	try {
		await memoryService.delete(id)
		await loadMemories()
	} catch (error) {
		console.error("删除记忆失败:", error)
	}
}

// 清空记忆
const clearAll = async () => {
	try {
		await memoryService.clear()
		await loadMemories()
	} catch (error) {
		console.error("清空记忆失败:", error)
	}
}
</script>

<template>
	<div class="memory-settings">
		<header class="section-header">
			<h2 class="title glow-teal">长期记忆库管理</h2>
			<p class="subtitle">查看并管理 Nori 记录的关于主人的偏好、重要事实与约定事项</p>
		</header>

		<div class="settings-content">
			<!-- 1. Embedding 向量嵌入配置 -->
			<div class="setting-card">
				<div class="card-header space-between">
					<div class="header-left">
						<Icon name="sparkles" :size="18" class="card-icon"/>
						<span class="card-title">向量嵌入与语义检索配置 (Embedding)</span>
					</div>
					<n-button type="primary" :loading="isReembedding" :disabled="isReembedding" @click="reembedAll">
						<template #icon>
							<Icon :name="isReembedding ? 'loading' : 'sparkles'" :size="14"/>
						</template>
						{{ isReembedding ? "正在索引..." : "重新计算记忆向量" }}
					</n-button>
				</div>
				<div class="card-body">
					<div class="form-row">
						<div class="form-item flex-1">
							<label class="label">Embedding 向量模型</label>
							<input
								v-model="embeddingModel"
								class="input"
								placeholder="BAAI/bge-m3, text-embedding-3-small, bge-large-zh-v1.5..."
								@blur="saveConfig('embedding_model', embeddingModel)"
							/>
						</div>
						<div class="form-item flex-1">
							<label class="label">API 地址 (留空复用 AI 大脑配置)</label>
							<input
								v-model="embeddingBaseUrl"
								class="input"
								placeholder="https://api.openai.com/v1"
								@blur="saveConfig('embedding_api_base', embeddingBaseUrl)"
							/>
						</div>
					</div>

					<div class="form-row">
						<div class="form-item flex-1">
							<label class="label">API Key (留空复用 AI 大脑配置)</label>
							<input
								v-model="embeddingApiKey"
								type="password"
								class="input"
								placeholder="sk-..."
								@blur="saveConfig('embedding_api_key', embeddingApiKey)"
							/>
						</div>
						<div class="form-item dims-item">
							<label class="label">向量维数</label>
							<input
								v-model="embeddingDimensions"
								type="number"
								min="1"
								class="input"
								placeholder="默认"
								@blur="saveDimensions"
							/>
						</div>
					</div>

					<p class="dims-hint">留空使用模型默认维数；仅部分模型支持自定义 (如 text-embedding-3-small/large)，修改后请点击右上角“重新计算记忆向量”，否则新旧向量维数不一致无法参与语义检索。</p>

					<p v-if="reembedMessage" class="status-tip">{{ reembedMessage }}</p>
				</div>
			</div>

			<!-- 2. 新增记忆 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="sparkles" :size="18" class="card-icon"/>
					<span class="card-title">添加长期记忆</span>
				</div>
				<div class="card-body">
					<div class="form-item">
						<textarea
							v-model="newContent"
							class="textarea"
							rows="2"
							placeholder="输入需要让 Nori 记住的内容（如: 主人最喜欢的饮料是冰美式）..."
						/>
					</div>

					<div class="add-meta-row">
						<div class="form-item flex-1">
							<input
								v-model="newTags"
								class="input"
								placeholder="标签 (可选, 如: 偏好, 饮食)"
							/>
						</div>

						<div class="form-item">
							<div class="importance-wrap">
								<span class="label">重要度: {{ Math.round(newImportance * 100) }}%</span>
								<n-slider
									v-model:value="newImportance"
									:min="0.1"
									:max="1.0"
									:step="0.1"
									:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
									style="width: 12rem;"
								/>
							</div>
						</div>

						<n-button type="primary" :disabled="!newContent.trim() || adding" :loading="adding" @click="addMemory">
							<template #icon>
								<Icon :name="adding ? 'loading' : 'check'" :size="14"/>
							</template>
							保存记忆
						</n-button>
					</div>
				</div>
			</div>

			<!-- 2. 记忆库列表与搜索 -->
			<div class="setting-card flex-1-card">
				<div class="card-header space-between">
					<div class="header-left">
						<Icon name="package" :size="18" class="card-icon"/>
						<span class="card-title">记忆列表 (共 {{ memories.length }} 条)</span>
					</div>
					<n-popconfirm
						v-if="memories.length > 0"
						positive-text="确定清空"
						negative-text="取消"
						@positive-click="clearAll"
					>
						<template #trigger>
							<button class="btn-danger-text">
								清空所有记忆
							</button>
						</template>
						确定要清空全部长期记忆吗？此操作不可恢复。
					</n-popconfirm>
				</div>

				<div class="card-body">
					<div class="search-row">
						<input
							v-model="searchKeyword"
							class="input flex-1"
							placeholder="搜索记忆关键词..."
							@input="loadMemories"
						/>
					</div>

					<div class="memory-list">
						<div v-if="memories.length === 0" class="empty-hint">
							{{ searchKeyword ? "未搜索到匹配的记忆条目" : "暂无已保存的长期记忆" }}
						</div>

						<div
							v-for="item in memories"
							:key="item.id"
							class="memory-item"
						>
							<div class="item-main">
								<div class="item-tags">
									<span v-if="item.tags" class="tag-badge">{{ item.tags }}</span>
									<span class="source-badge">{{ item.source === "agent" ? "AI 自动记忆" : "手动记录" }}</span>
									<span class="imp-badge">重要度 {{ Math.round(item.importance * 100) }}%</span>
								</div>
								<p class="item-content">{{ item.content }}</p>
								<span class="item-time">{{ new Date(item.createdAt).toLocaleString("zh-CN") }}</span>
							</div>
							<n-popconfirm
								positive-text="删除"
								negative-text="取消"
								@positive-click="deleteMemory(item.id)"
							>
								<template #trigger>
									<button class="btn-del" title="删除此记忆">
										<Icon name="close" :size="14"/>
									</button>
								</template>
								确定删除这条记忆吗？
							</n-popconfirm>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.memory-settings {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
	padding: 1.6rem 2.4rem;
	gap: 1.6rem;
}

.section-header {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.title {
	margin: 0;
	font-size: 1.8rem;
	font-weight: 700;
	color: var(--text-primary);
}

.subtitle {
	margin: 0;
	font-size: 1.2rem;
	color: var(--text-faint);
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	transition: all 0.2s ease;

	&:hover {
		border-color: var(--line-strong);
	}
}

.card-header {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	color: var(--nori-teal-bright);

	&.space-between {
		justify-content: space-between;
	}
}

.header-left {
	display: flex;
	align-items: center;
	gap: 0.8rem;
}

.card-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-body {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.form-item {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-muted);
}

.input {
	padding: 0.9rem 1.4rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
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

.textarea {
	padding: 1rem 1.4rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	resize: vertical;
	outline: none;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:focus {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.add-meta-row {
	display: flex;
	align-items: center;
	gap: 1.2rem;
}

.flex-1 {
	flex: 1;
}

.importance-wrap {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	font-size: 1.2rem;
	color: var(--text-muted);
}

.range-slider {
	height: 0.6rem;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.form-row {
	display: flex;
	gap: 1.2rem;
}

.dims-item {
	width: 11rem;
	flex-shrink: 0;
}

.dims-hint {
	margin: 0;
	font-size: 1.1rem;
	color: var(--text-faint);
	line-height: 1.5;
}

.status-tip {
	margin: 0;
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
}

.btn-secondary {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.75rem 1.6rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	color: var(--nori-teal-bright);
	font-size: 1.25rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover:not(:disabled) {
		background: rgba(125, 227, 255, 0.18);
		box-shadow: 0 0 1.4rem var(--glow-teal-soft);
		transform: translateY(-0.1rem);
	}

	&:disabled {
		opacity: 0.6;
		cursor: default;
	}
}

.btn-primary {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.85rem 1.8rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	font-weight: 600;
	font-size: 1.3rem;
	font-family: inherit;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover:not(:disabled) {
		box-shadow: 0 0.4rem 1.6rem var(--glow-teal-strong);
		transform: translateY(-0.15rem);
	}

	&:disabled {
		opacity: 0.5;
		cursor: default;
		filter: grayscale(0.5);
	}
}

.btn-danger-text {
	background: none;
	border: none;
	color: var(--danger);
	font-size: 1.2rem;
	cursor: pointer;
	opacity: 0.85;
	transition: opacity 0.2s;

	&:hover {
		opacity: 1;
		text-decoration: underline;
	}
}

.search-row {
	display: flex;
	gap: 0.8rem;
}

.memory-list {
	display: flex;
	flex-direction: column;
	gap: 0.8rem;
	max-height: 28rem;
	overflow-y: auto;
}

.empty-hint {
	font-size: 1.2rem;
	color: var(--text-faint);
	padding: 1.6rem 0;
	text-align: center;
}

.memory-item {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
	padding: 1.1rem 1.4rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	gap: 1.2rem;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.04);
		border-color: var(--line-strong);
	}
}

.item-main {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
	flex: 1;
}

.item-tags {
	display: flex;
	gap: 0.6rem;
	flex-wrap: wrap;
}

.tag-badge, .source-badge, .imp-badge {
	font-size: 1.05rem;
	padding: 0.2rem 0.6rem;
	border-radius: var(--radius-pill);
}

.tag-badge {
	background: rgba(125, 227, 255, 0.12);
	color: var(--nori-teal-bright);
	border: 0.1rem solid rgba(125, 227, 255, 0.2);
}

.source-badge {
	background: rgba(255, 255, 255, 0.06);
	color: var(--text-muted);
}

.imp-badge {
	background: rgba(255, 180, 50, 0.15);
	color: #ffb432;
	border: 0.1rem solid rgba(255, 180, 50, 0.25);
}

.item-content {
	margin: 0;
	font-size: 1.25rem;
	color: var(--text-primary);
	line-height: 1.4;
}

.item-time {
	font-size: 1.05rem;
	color: var(--text-faint);
}

.btn-del {
	width: 2.8rem;
	height: 2.8rem;
	flex-shrink: 0;
	border: none;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.06);
	color: var(--text-muted);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(255, 75, 75, 0.2);
		color: #ff4b4b;
	}
}
</style>
