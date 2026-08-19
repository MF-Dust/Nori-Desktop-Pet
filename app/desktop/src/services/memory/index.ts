import {invoke} from "../host/invoke"

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
	createdAt: string
	updatedAt: string
}

/**
 * 记忆服务
 */
export class MemoryService {
	/**
	 * 添加一条新记忆
	 */
	public async add(content: string, type = "general", importance = 0.5, tags?: string): Promise<MemoryItem> {
		return invoke<MemoryItem>("add_memory", {
			type,
			content,
			importance,
			tags,
			source: "chat",
		})
	}

	/**
	 * 获取全部记忆列表
	 */
	public async getAll(limit = 100): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("get_all_memories", {limit})
	}

	/**
	 * 搜索记忆
	 */
	public async search(keyword: string, limit = 20): Promise<MemoryItem[]> {
		return invoke<MemoryItem[]>("search_memories", {keyword, limit})
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
	 * 提取并返回与当前输入最相关的记忆片段列表 (用于 Prompt 注入)
	 */
	public async getRelevantMemories(prompt: string, limit = 5): Promise<string[]> {
		try {
			// 先拿高重要度的近期记忆
			const ALL = await this.getAll(15)
			if (ALL.length === 0) return []

			// 简易关键词与重要度匹配
			const WORDS = prompt
				.toLowerCase()
				.split(/[\s,，.。!！?？]+/)
				.filter((w) => w.length >= 2)

			const SCORED = ALL.map((item) => {
				let score = item.importance * 1.5
				for (const word of WORDS) {
					if (item.content.toLowerCase().includes(word)) {
						score += 2.0
					}
				}
				return {item, score}
			})

			SCORED.sort((a, b) => b.score - a.score)
			return SCORED.slice(0, limit).map((s) => s.item.content)
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
