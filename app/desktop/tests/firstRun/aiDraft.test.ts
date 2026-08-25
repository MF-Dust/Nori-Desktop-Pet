import {describe, expect, it} from "vitest"
import {
	AI_DEFAULT_BASE_URLS,
	buildAiChatPatch,
	effectiveBaseUrl,
	emptyAiDraft,
	isAiDraftFilled,
} from "../../src/services/firstRun/aiDraft"
import type {AiDraft} from "../../src/services/firstRun/aiDraft"

const draftOf = (patch: Partial<AiDraft>): AiDraft => ({...emptyAiDraft(), ...patch})

describe("首次运行 AI 配置草稿", () => {
	it("空草稿视为未填, 不产生补丁", () => {
		expect(isAiDraftFilled(emptyAiDraft())).toBe(false)
		expect(buildAiChatPatch(emptyAiDraft())).toBeNull()
	})

	it("只换协议不算填写 (没有密钥或模型的协议选择没有意义)", () => {
		const DRAFT = draftOf({provider: "anthropic"})
		expect(isAiDraftFilled(DRAFT)).toBe(false)
		expect(buildAiChatPatch(DRAFT)).toBeNull()
	})

	it("空白字符不算填写", () => {
		expect(isAiDraftFilled(draftOf({apiKey: "   ", model: "\t"}))).toBe(false)
	})

	it("地址留空时取该协议默认值", () => {
		expect(effectiveBaseUrl(draftOf({provider: "google"}))).toBe(AI_DEFAULT_BASE_URLS.google)
		expect(effectiveBaseUrl(draftOf({baseUrl: " https://proxy.local/v1 "}))).toBe("https://proxy.local/v1")
	})

	it("填了密钥就带上生效地址与协议 (向导里验证过的地址必须落盘)", () => {
		expect(buildAiChatPatch(draftOf({provider: "anthropic", apiKey: " sk-ant-x "}))).toEqual({
			provider: "anthropic",
			baseUrl: AI_DEFAULT_BASE_URLS.anthropic,
			apiKey: "sk-ant-x",
		})
	})

	it("模型与自定义地址原样落盘, 未填的字段不出现在补丁里", () => {
		const PATCH = buildAiChatPatch(draftOf({baseUrl: "https://proxy.local/v1 ", model: " gpt-4o-mini "}))
		expect(PATCH).toEqual({
			provider: "openai",
			baseUrl: "https://proxy.local/v1",
			model: "gpt-4o-mini",
		})
		expect(PATCH && "apiKey" in PATCH).toBe(false)
	})
})
