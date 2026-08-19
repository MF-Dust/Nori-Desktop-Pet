import {invoke} from "../host/invoke"

/**
 * 向量嵌入服务 (支持 BGE-M3 / OpenAI / Ollama 等兼容接口)
 */
export class EmbeddingService {
	private cache = new Map<string, number[]>()

	/**
	 * 获取文本的向量嵌入
	 */
	public async embed(text: string): Promise<number[] | null> {
		const TRIMMED = text.trim()
		if (!TRIMMED) return null

		if (this.cache.has(TRIMMED)) {
			return this.cache.get(TRIMMED)!
		}

		try {
			const VECTOR = await invoke<number[]>("create_embedding", {text: TRIMMED})
			if (Array.isArray(VECTOR) && VECTOR.length > 0) {
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
