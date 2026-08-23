/**
 * 助手回复分段
 *
 * 产品要求保留「一次回复拆成多条气泡」的连发陪伴感, 但代码块/表格/列表这类
 * 结构化内容一拆就散, 因此含结构标记时降级为单段 (由 Markdown 整块渲染)。
 *
 * 另外流式过程中不拆 —— 边流边拆会让气泡不断重排跳动, 完成后再成型。
 */

/** 结构化 Markdown 标记: 命中任一即不拆分 */
const BLOCK_PATTERNS: RegExp[] = [
	/```/, // 围栏代码块
	/~~~/, // 波浪线代码块
	/^\s{0,3}#{1,6}\s/m, // 标题
	/^\s{0,3}>\s/m, // 引用
	/^\s{0,3}([-*+]|\d+[.)])\s/m, // 列表
	/^\s{0,3}\|.*\|/m, // 表格
	/^\s{0,3}(-{3,}|\*{3,}|_{3,})\s*$/m, // 分隔线
	/^\s{4,}\S/m, // 缩进代码块
]

/** 单段长度上限, 超过则按逗号二次切分 */
const LONG_SEGMENT = 80

/**
 * 文本是否含结构化 Markdown
 */
export const hasBlockStructure = (text: string): boolean =>
	BLOCK_PATTERNS.some(pattern => pattern.test(text))

/**
 * 把助手回复切成气泡文本
 *
 * @param text 完整回复
 * @param options.streaming 流式进行中: 不拆, 保持单气泡
 */
export const splitAssistantMessage = (
	text: string,
	options: {streaming?: boolean} = {},
): string[] => {
	const TRIMMED = text.trim()
	if (!TRIMMED) return []
	if (options.streaming || hasBlockStructure(TRIMMED)) return [TRIMMED]

	// 换行优先: 作者自己分的段落最可靠
	const LINES = TRIMMED.split(/\r?\n+/).map(line => line.trim()).filter(Boolean)
	if (LINES.length > 1) return LINES

	// 单段落: 按句末标点切
	const SENTENCES = TRIMMED.split(/(?<=[。！？!?；;])/).map(part => part.trim()).filter(Boolean)
	return SENTENCES.flatMap(sentence =>
		sentence.length > LONG_SEGMENT
			? sentence.split(/(?<=[，,、])/).map(part => part.trim()).filter(Boolean)
			: [sentence])
}
