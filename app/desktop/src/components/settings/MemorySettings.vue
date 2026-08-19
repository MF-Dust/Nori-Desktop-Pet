<script setup lang="ts">
import {onMounted, ref} from "vue"
import {memoryService, type MemoryItem} from "../../services/memory"
import Icon from "../Icon.vue"

const memories = ref<MemoryItem[]>([])
const searchKeyword = ref("")
const loading = ref(false)

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
			memories.value = await memoryService.search(searchKeyword.value.trim(), 50)
		} else {
			memories.value = await memoryService.getAll(50)
		}
	} catch (error) {
		console.error("加载记忆列表失败:", error)
	} finally {
		loading.value = false
	}
}

onMounted(() => {
	void loadMemories()
})

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
	if (confirm("确定要清空全部长期记忆吗？此操作不可恢复。")) {
		try {
			await memoryService.clear()
			await loadMemories()
		} catch (error) {
			console.error("清空记忆失败:", error)
		}
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
			<!-- 1. 新增记忆 -->
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
								<input
									v-model.number="newImportance"
									type="range"
									min="0.1"
									max="1.0"
									step="0.1"
									class="range-slider"
								/>
							</div>
						</div>

						<button class="btn-primary" :disabled="!newContent.trim() || adding" @click="addMemory">
							<Icon :name="adding ? 'loading' : 'check'" :size="14"/>
							<span>保存记忆</span>
						</button>
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
					<button v-if="memories.length > 0" class="btn-danger-text" @click="clearAll">
						清空所有记忆
					</button>
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
							<button class="btn-del" title="删除此记忆" @click="deleteMemory(item.id)">
								<Icon name="close" :size="14"/>
							</button>
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
	padding: 1.5rem 2rem;
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
	color: var(--text-muted);
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.6rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.4rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
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
	font-size: 1.15rem;
	color: var(--text-muted);
}

.input {
	padding: 0.8rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.25rem;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
	}
}

.textarea {
	padding: 0.8rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.25rem;
	font-family: inherit;
	resize: vertical;
	outline: none;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
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
}

.range-slider {
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.btn-primary {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.8rem 1.4rem;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	font-weight: 600;
	font-size: 1.2rem;
	cursor: pointer;
	white-space: nowrap;
	transition: all 0.2s ease;

	&:hover:not(:disabled) {
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}

	&:disabled {
		opacity: 0.5;
		cursor: default;
	}
}

.btn-danger-text {
	background: none;
	border: none;
	color: var(--danger);
	font-size: 1.15rem;
	cursor: pointer;
	opacity: 0.8;
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
	font-size: 1.15rem;
	color: var(--text-faint);
	padding: 1.2rem 0;
	text-align: center;
}

.memory-item {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
	padding: 1rem 1.2rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	gap: 1rem;
}

.item-main {
	display: flex;
	flex-direction: column;
	gap: 0.5rem;
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
	border-radius: 0.4rem;
}

.tag-badge {
	background: rgba(125, 227, 255, 0.12);
	color: var(--nori-teal-bright);
}

.source-badge {
	background: rgba(255, 255, 255, 0.08);
	color: var(--text-muted);
}

.imp-badge {
	background: rgba(255, 180, 50, 0.15);
	color: #ffb432;
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
