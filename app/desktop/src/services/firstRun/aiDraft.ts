/**
 * 首次运行的 AI 配置草稿
 *
 * 向导里这一步是可跳过的, 所以草稿只在离开该步时才落盘 —— 纯逻辑收在这里,
 * 组件只管渲染, 补丁构造也就能单测。
 *
 * 注意后端授权面: `settings_update_ai_providers` 与 `llm_fetch_models` 允许
 * first-run 窗口调用, 而 `ai_test_connection` 只允许 main。所以这一步用
 * 「获取模型」来验证地址与密钥, 不做连接测试。
 */

/** 对话协议 (与设置页 AiSettings 的列表一致) */
export type AiProviderKey = "openai" | "openai_responses" | "anthropic" | "google"

/** 协议顺序 */
export const AI_PROVIDER_OPTIONS: AiProviderKey[] = ["openai", "openai_responses", "anthropic", "google"]

/** 各协议的默认 API 地址 */
export const AI_DEFAULT_BASE_URLS: Record<AiProviderKey, string> = {
	openai: "https://api.openai.com/v1",
	openai_responses: "https://api.openai.com/v1",
	anthropic: "https://api.anthropic.com/v1",
	google: "https://generativelanguage.googleapis.com/v1beta",
}

/** 这一步收集到的内容 */
export interface AiDraft {
	provider: AiProviderKey
	baseUrl: string
	apiKey: string
	model: string
}

/** 对话配置补丁 (与 settings_update_ai_providers 的 chat 字段同形) */
export type AiChatPatch = Partial<{provider: string; baseUrl: string; apiKey: string; model: string}>

/** 空草稿 */
export const emptyAiDraft = (): AiDraft => ({provider: "openai", baseUrl: "", apiKey: "", model: ""})

/** 取生效的 API 地址 (留空即用该协议默认值, 与输入框 placeholder 一致) */
export const effectiveBaseUrl = (draft: AiDraft): string =>
	draft.baseUrl.trim() || AI_DEFAULT_BASE_URLS[draft.provider]

/**
 * 是否填了东西
 *
 * 只选了协议不算 —— 没有密钥或模型的协议选择保存下去没有意义, 也不该
 * 让「下一步」多打一次后端。
 */
export const isAiDraftFilled = (draft: AiDraft): boolean =>
	Boolean(draft.apiKey.trim() || draft.model.trim() || draft.baseUrl.trim())

/**
 * 构造对话配置补丁
 *
 * 返回 null 表示无需保存。填了内容时一并写入生效地址: 这一步验证过的就是
 * 这个地址, 不写下去会出现「向导里能拉到模型, 进主界面却连不上」。
 */
export const buildAiChatPatch = (draft: AiDraft): AiChatPatch | null => {
	if (!isAiDraftFilled(draft)) return null
	const PATCH: AiChatPatch = {provider: draft.provider, baseUrl: effectiveBaseUrl(draft)}
	const API_KEY = draft.apiKey.trim()
	const MODEL = draft.model.trim()
	if (API_KEY) PATCH.apiKey = API_KEY
	if (MODEL) PATCH.model = MODEL
	return PATCH
}
