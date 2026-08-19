import {invoke} from "../host/invoke"
import {embeddingService} from "../embedding"

/**
 * 记忆条目定义
 */
export interface MemoryItem {
	id: number
	type: string
	content: string
	importance: number
	source: string
	tags?: string
	embedding?: string
	createdAt: string
	updatedAt: string
}

/**
 * 记忆服务 (集成通用向量嵌入、语义相似度与关键词混合搜索)
 */
export class MemoryService {
	/**
	 * 添加一条新记忆 (自动计算向量嵌入)
	 */
	public async add(content: string, type = "general", importance = 0.5, tags?: string): Promise<MemoryItem> {
		let embeddingStr: string | undefined

		try {
			const VEC = await embeddingService.embed(content)
			if (VEC) {
				embeddingStr = JSON.stringify(VEC)
			}
		} catch (error) {
			console.warn("生成记忆向量失败:", error)
		}

		return invoke<MemoryItem>("add_memory", {
			type,
			content,
			importance,
			tags,
			source: "chat",
			embedding: embeddingStr,
		})
	}

	/**
	 * 获取全部记忆列表
	 */
	public async getAll(limit = 100): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("get_all_memories", {limit})
	}

	/**
	 * 按关键词搜索记忆
	 */
	public async search(keyword: string, limit = 20): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("search_memories", {keyword, limit})
	}

	/**
	 * 混合语义检索 (向量相似度 + 关键词融合)
	 */
	public async searchHybrid(keyword: string, limit = 10): Promise<MemoryItem[]> {
		try {
			const VEC = await embeddingService.embed(keyword)
			return await invoke<MemoryItem[]>("search_memories_hybrid", {
				keyword,
				vector: VEC || undefined,
				limit,
			})
		} catch (error) {
			console.warn("混合检索失败，回退到普通文本搜索:", error)
			return this.search(keyword, limit)
		}
	}

	/**
	 * 重新为所有未嵌入向量的记忆生成 Embedding
	 */
	public async reembedAll(): Promise<number> {
		const ALL = await this.getAll(500)
		let count = 0

		for (const item of ALL) {
			if (!item.embedding) {
				try {
					const VEC = await embeddingService.embed(item.content)
					if (VEC) {
						await invoke("update_memory_embedding", {
							id: item.id,
							embedding: JSON.stringify(VEC),
						})
						count++
					}
				} catch (error) {
					console.warn(`为记忆 #${item.id} 生成向量失败:`, error)
				}
			}
		}

		return count
	}

	/**
	 * 更新记忆
	 */
	public async update(id: number, content: string, importance?: number, tags?: string): Promise<boolean> {
		return invoke<boolean>("update_memory", {id, content, importance, tags})
	}

	/**
	 * 删除单条记忆
	 */
	public async delete(id: number): Promise<boolean> {
		return invoke<boolean>("delete_memory", {id})
	}

	/**
	 * 清空全部记忆
	 */
	public async clear(): Promise<void> {
		return invoke<void>("clear_memories")
	}

	/**
	 * 提取并返回与当前用户输入最相关的记忆片段列表 (用于 Prompt 注入)
	 */
	public async getRelevantMemories(prompt: string, limit = 5): Promise<string[]> {
		try {
			const RESULTS = await this.searchHybrid(prompt, limit)
			return RESULTS.map((item) => item.content)
		} catch (error) {
			console.error("获取相关记忆失败:", error)
			return []
		}
	}
}

/**
 * 全局记忆服务单例
 */
export const memoryService = new MemoryService()
