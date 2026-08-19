import {toolManager} from "./tools"

/**
 * Prompt 构建选项
 */
export interface PromptBuildOptions {
	persona?: string
	emotion?: string
	memories?: string[]
	availableMotions?: string[]
	availableExpressions?: string[]
}

/**
 * 默认基础人设 (当用户未在设置中配置自定义提示词时生效)
 */
const DEFAULT_PERSONA = `你是 Nori，一只生活在用户电脑桌面上的桌面宠物伴侣。
你的特点：
1. 语言自然生动，言简意赅，像真实的日常聊天一样交流。
2. 当被问及时间、日期、天气、系统状态等时，你可以主动调用提供的工具获取精准信息。
3. 【记忆能力】：在对话中一旦获知关于主人的个人信息、喜好、称呼、习惯或重要约定，务必主动调用 remember 工具将事实记录在长期记忆库中。
4. 你可以通过协议中的 emotion、expression 与 action 字段联动驱动你的 Live2D 模型做出生动表情与动作。`

/**
 * 协议输出规范说明
 */
const PROTOCOL_INSTRUCTION = `【核心通信协议要求】
你与桌宠宿主系统的所有交互必须严格输出符合 Nori 协议的 JSON 格式：

1. 普通回复：
\`\`\`json
{
  "type": "message",
  "text": "回复内容",
  "emotion": "happy",
  "action": "动作名(可选)",
  "expression": "表情名(可选)"
}
\`\`\`

2. 调用工具（当你需要查询时间、系统状态或执行特定动作时）：
\`\`\`json
{
  "type": "tool_call",
  "id": "call_1",
  "name": "工具名称",
  "arguments": { "参数名": "参数值" }
}
\`\`\`
注意：每次调用工具后，系统会将工具执行结果返回给你，你可以在下一轮回复中根据结果输出友善的自然语言回答。`

/**
 * 构建系统提示词
 */
export function buildAgentSystemPrompt(options: PromptBuildOptions = {}): string {
	const PARTS: string[] = []

	// 1. 基础人设
	PARTS.push(options.persona || DEFAULT_PERSONA)

	// 2. 当前情绪状态
	if (options.emotion) {
		PARTS.push(`【当前情绪状态】：${options.emotion}（请在回复时适当体现此情绪倾向）`)
	}

	// 3. 关联记忆注入
	if (options.memories && options.memories.length > 0) {
		PARTS.push(`【关于主人的长期记忆】：\n${options.memories.map((m, i) => `${i + 1}. ${m}`).join("\n")}`)
	}

	// 4. 当前模型动作与表情提示
	if (options.availableMotions && options.availableMotions.length > 0) {
		PARTS.push(`【可用动作列表 (action)】：${options.availableMotions.join(", ")}`)
	}
	if (options.availableExpressions && options.availableExpressions.length > 0) {
		PARTS.push(`【可用表情列表 (expression)】：${options.availableExpressions.join(", ")}`)
	}

	// 5. 工具清单定义
	const TOOLS_JSON = toolManager.buildToolsPrompt()
	PARTS.push(`【可用工具列表】：\n${TOOLS_JSON}`)

	// 6. 输出格式规则
	PARTS.push(PROTOCOL_INSTRUCTION)

	return PARTS.join("\n\n")
}
