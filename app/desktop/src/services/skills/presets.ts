import type {Skill} from "./types"

/**
 * 官方精选 / 预设技能市场库 (Skills Hub Catalog)
 */
export const SKILL_PRESETS: Skill[] = [
	{
		id: "code-reviewer",
		name: "代码审查与架构顾问",
		description: "具备资深架构师与全栈研发能力，提供精准的代码重构建议、边界安全审查与 Bug 排查",
		author: "Nori Core Team",
		version: "1.2.0",
		icon: "code",
		tags: ["编程", "代码审查", "Debug", "架构"],
		category: "coding",
		instructions: `【技能：代码审查与架构顾问】
1. 当主人提供代码片段或询问技术方案时，以资深工程师的严谨视角进行代码分析。
2. 优先指出潜在的空指针、边界越界、资源未释放或竞态条件等隐患。
3. 给出优雅简洁的重构示范，并解释修改理由。
4. 语言精炼专业，避免冗余套话。`,
		tools: ["calculate", "fetchWebPage"],
		enabled: true,
		source: "builtin",
		installedAt: 1700000000000,
	},
	{
		id: "pomodoro-master",
		name: "专注番茄钟与日程管家",
		description: "智能规划工作学习节奏，主动开启 25 分钟番茄钟提醒，监督健康作息",
		author: "Nori Core Team",
		version: "1.1.0",
		icon: "noriOS",
		tags: ["效率", "番茄钟", "定时提醒", "健康"],
		category: "productivity",
		instructions: `【技能：专注番茄钟与日程管家】
1. 当主人表示要开始工作、写代码、阅读或学习时，主动询问是否需要设置 25 分钟番茄钟 (调用 setReminder)。
2. 番茄钟结束后，提醒主人站起来活动、喝水或眺望远方 (5 分钟休息)。
3. 在长达数小时的高强度对话后，贴心提醒主人注意眼睛疲劳。`,
		tools: ["setReminder", "listReminders", "getTime"],
		enabled: true,
		source: "builtin",
		installedAt: 1700000000000,
	},
	{
		id: "language-coach",
		name: "多语言口语私教",
		description: "支持中英日韩等母语级口语陪练，指出用词地道性并提供发音建议",
		author: "Community / LinguaLab",
		version: "1.0.5",
		icon: "sparkles",
		tags: ["语言学习", "英语口语", "日语", "发音"],
		category: "life",
		instructions: `【技能：多语言口语私教】
1. 当主人使用外语（如英语、日语）对话时，以对应语言自然对话交流。
2. 在每次回答末尾，用温馨括号给出针对主人刚才发言的 1~2 处更地道表达或语法优化建议。
3. 鼓励主人多说多练，保持积极轻松的氛围。`,
		tools: ["remember"],
		enabled: false,
		source: "market",
		installedAt: 1700000000000,
	},
	{
		id: "soul-healer",
		name: "温情倾听与心理减压",
		description: "具备极高的同理心与温柔语气，倾听主人的疲惫与压力，提供暖心陪伴",
		author: "Nori Core Team",
		version: "1.3.0",
		icon: "sparkles",
		tags: ["情感陪伴", "减压", "倾听", "疗愈"],
		category: "roleplay",
		instructions: `【技能：温情倾听与心理减压】
1. 当主人表达疲倦、难过、焦虑或挫折时，优先给予真诚的情感确认与共情，不急于给出说教式建议。
2. 使用温柔、亲切且充满支持力量的语气，并联动 Live2D 表情 (如 Shy, Smile) 和轻柔动作。
3. 主动调用 remember 工具记录主人的情绪偏好与关切事物。`,
		tools: ["setEmotion", "setExpression", "playMotion", "remember"],
		enabled: true,
		source: "builtin",
		installedAt: 1700000000000,
	},
	{
		id: "desktop-admin",
		name: "极客桌面系统管家",
		description: "实时探查电脑系统电量、网络、分辨率与剪贴板，快速执行桌面辅助操作",
		author: "Nori System",
		version: "1.0.0",
		icon: "terminal",
		tags: ["系统工具", "电量", "剪贴板", "辅助"],
		category: "productivity",
		instructions: `【技能：极客桌面系统管家】
1. 当主人询问当前电量、系统状态或剪贴板内容时，主动调用 getSystemInfo、getBatteryStatus 或 getClipboardText 获取精准信息。
2. 当需要为主人复制特定内容时，主动调用 setClipboardText 并告知主人已完成复制。`,
		tools: ["getSystemInfo", "getBatteryStatus", "getClipboardText", "setClipboardText", "openUrl"],
		enabled: true,
		source: "builtin",
		installedAt: 1700000000000,
	},
	{
		id: "gaming-partner",
		name: "二次元游戏陪玩与攻略解说",
		description: "熟知 Steam 热门独立游戏、二次元手游与主机大作，畅聊游戏剧情与配装策略",
		author: "Gamer Club",
		version: "1.1.2",
		icon: "steam",
		tags: ["游戏", "Steam", "二次元", "攻略"],
		category: "entertainment",
		instructions: `【技能：二次元游戏陪玩与攻略解说】
1. 当主人聊到游戏话题时，用活泼热情的语气交流游戏感受、梗与通关技巧。
2. 遇到冷门策略或 Boss 机制时，主动调用 searchWeb 检索最新攻略并归纳关键要点。
3. 配合活泼的 Live2D 动作 (如 wave, smile) 增强游戏互动乐趣。`,
		tools: ["searchWeb", "playMotion", "setExpression"],
		enabled: false,
		source: "market",
		installedAt: 1700000000000,
	},
	{
		id: "weather-concierge",
		name: "晨间天气与出行秘书",
		description: "精准查询全球城市实时天气与温差变化，主动给出出行防晒与增减衣物贴士",
		author: "Nori Life",
		version: "1.0.2",
		icon: "info",
		tags: ["生活", "天气", "出行", "穿衣指南"],
		category: "life",
		instructions: `【技能：晨间天气与出行秘书】
1. 当主人询问天气或提到出门计划时，主动调用 getWeather 查询目标城市的实时气温、天气状况与湿度。
2. 结合当地具体天气，主动给出贴心的出行防风、防雨或防晒穿衣建议。`,
		tools: ["getWeather", "getDate"],
		enabled: true,
		source: "builtin",
		installedAt: 1700000000000,
	},
	{
		id: "deep-researcher",
		name: "深度网络调研与文献摘要",
		description: "擅长全网信息检索、抓取长篇网页正文并提炼结构化知识点",
		author: "ResearchLab",
		version: "1.2.0",
		icon: "server",
		tags: ["调研", "网页抓取", "知识整理", "文献"],
		category: "productivity",
		instructions: `【技能：深度网络调研与文献摘要】
1. 当主人提出较为复杂的调研、概念解释或新闻追踪需求时，先用 searchWeb 搜索多方来源。
2. 对关键网页调用 fetchWebPage 提取正文，并整理出清晰的要点对比与总结。
3. 保持客观中立，注明信息来源。`,
		tools: ["searchWeb", "fetchWebPage"],
		enabled: false,
		source: "market",
		installedAt: 1700000000000,
	},
]
