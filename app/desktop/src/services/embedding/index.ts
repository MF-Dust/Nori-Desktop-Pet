import {invoke} from "../host/invoke"

/**
 * 向量嵌入服务 (支持 BGE-M3 / OpenAI / Ollama 等兼容接口，带 LRU 内存缓存)
 */
export class EmbeddingService {
	private cache = new Map<string, number[]>()
	private readonly maxCacheSize = 250

	/**
	 * 获取文本的向量嵌入
	 */
	public async embed(text: string): Promise<number[] | null> {
		const TRIMMED = text.trim()
		if (!TRIMMED) return null

		if (this.cache.has(TRIMMED)) {
			// 刷新 LRU 访问热度
			const VECTOR = this.cache.get(TRIMMED)!
			this.cache.delete(TRIMMED)
			this.cache.set(TRIMMED, VECTOR)
			return VECTOR
		}

		try {
			const VECTOR = await invoke<number[]>("create_embedding", {text: TRIMMED})
			if (Array.isArray(VECTOR) && VECTOR.length > 0) {
				if (this.cache.size >= this.maxCacheSize) {
					// 淘汰最早未使用的缓存项
					const OLDEST = this.cache.keys().next().value
					if (OLDEST !== undefined) {
						this.cache.delete(OLDEST)
					}
				}
				this.cache.set(TRIMMED, VECTOR)
				return VECTOR
			}
			return null
		} catch (error) {
			console.warn("生成 Embedding 失败，将使用关键词文本回退:", error)
			return null
		}
	}

	/**
	 * 清空向量缓存
	 */
	public clearCache(): void {
		this.cache.clear()
	}
}

/**
 * 全局向量嵌入服务单例
 */
export const embeddingService = new EmbeddingService()
