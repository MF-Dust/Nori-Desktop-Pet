/**
 * 模型动作组读取 (不依赖挂载, 直接解析 model3.json)
 * 输出格式与控制器 getMotions 一致:
 * [{group: "Idle", names: ["01_Idle_Loop", ...]}]  (与 pixi-live2d-display definitions 对齐)
 */
import {assetUrl, resolveModelFileBase} from "./config"
import type {MotionGroup} from "./index"

/**
 * 按动作组名称选择点击互动候选。
 *
 * 与原生 C# 运行时保持同一排序: TapBody -> 点击/触摸 -> 反应 -> 动作/交互 ->
 * 其他非 Idle / Background 组。返回对象保留模型声明的原始组名。
 */
export const selectInteractionMotionGroups = (groups: MotionGroup[]): MotionGroup[] => {
	return groups
		.map((group, index) => ({group, index, priority: classifyInteractionGroup(group.group)}))
		.filter((candidate) => candidate.priority >= 0 && candidate.group.names.some((name) => name.trim() !== ""))
		.sort((left, right) => left.priority - right.priority || left.index - right.index)
		.map((candidate) => candidate.group)
}

const classifyInteractionGroup = (group: string): number => {
	const NORMALIZED = group.replace(/[^a-z0-9]/gi, "").toLowerCase()
	if (NORMALIZED === "" || NORMALIZED.startsWith("idle") || NORMALIZED.startsWith("background")) return -1
	if (NORMALIZED === "tapbody") return 0
	if (NORMALIZED.includes("tap") || NORMALIZED.includes("touch") || NORMALIZED.includes("click")) return 1
	if (NORMALIZED.includes("reaction")) return 2
	if (NORMALIZED.includes("action") || NORMALIZED.includes("interaction")) return 3
	return 4
}

/**
 * 读取指定模型的全部动作组
 * 资源缺失 / 解析失败返回 null
 */
export const readMotionGroups = async (modelId: string): Promise<MotionGroup[] | null> => {
	try {
		const BASE = resolveModelFileBase(modelId)
		const RESPONSE = await fetch(`${assetUrl(`live2d/${modelId}`)}/${BASE}.model3.json`)
		if (!RESPONSE.ok) return null
		const MODEL = await RESPONSE.json()
		const MOTIONS = MODEL?.FileReferences?.Motions
		if (!MOTIONS || typeof MOTIONS !== "object") return null
		const GROUPS: MotionGroup[] = []
		for (const [GROUP, ITEMS] of Object.entries(MOTIONS)) {
			if (!Array.isArray(ITEMS)) continue
			const NAMES = ITEMS.map((item) => item?.File)
				.filter((file): file is string => typeof file === "string")
				.map((file) => file.replace(/\.motion3\.json$/i, "").replace(/^.*\//, ""))
				.filter((name) => name !== "")
			if (NAMES.length === 0) continue
			GROUPS.push({group: GROUP, names: NAMES})
		}
		return GROUPS.length > 0 ? GROUPS : null
	} catch {
		return null
	}
}
