import {invoke} from "../host/invoke"

/**
 * 历史消息定义
 */
export interface PersistedChatMessage {
	id: number
	role: "user" | "assistant"
	content: string
	createdAt: string
}

/**
 * 对话历史记录服务
 */
export class ChatHistoryService {
	/**
	 * 保存一条对话消息
	 */
	public async save(role: "user" | "assistant", content: string): Promise<PersistedChatMessage> {
		return invoke<PersistedChatMessage>("save_chat_message", {role, content})
	}

	/**
	 * 获取最近的对话消息
	 */
	public async getRecent(limit = 50): Promise<PersistedChatMessage[]> {
		return invoke<PersistedChatMessage[]>("get_chat_history", {limit})
	}

	/**
	 * 清空全部对话历史
	 */
	public async clear(): Promise<void> {
		return invoke<void>("clear_chat_history")
	}
}

/**
 * 全局对话历史单例
 */
export const chatHistoryService = new ChatHistoryService()
