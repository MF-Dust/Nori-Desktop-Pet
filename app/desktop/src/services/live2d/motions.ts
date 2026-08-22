/** Live2D 预览用的纯动作组筛选, 不读取配置、不执行设备或网络业务。 */
import type {MotionGroup} from "./index"

export const selectInteractionMotionGroups = (groups: MotionGroup[]): MotionGroup[] =>
	groups
		.map((group, index) => ({group, index, priority: classifyInteractionGroup(group.group)}))
		.filter(candidate => candidate.priority >= 0 && candidate.group.names.some(name => name.trim() !== ""))
		.sort((left, right) => left.priority - right.priority || left.index - right.index)
		.map(candidate => candidate.group)

const classifyInteractionGroup = (group: string): number => {
	const NORMALIZED = group.replace(/[^a-z0-9]/gi, "").toLowerCase()
	if (NORMALIZED === "" || NORMALIZED.startsWith("idle") || NORMALIZED.startsWith("background")) return -1
	if (NORMALIZED === "tapbody") return 0
	if (NORMALIZED.includes("tap") || NORMALIZED.includes("touch") || NORMALIZED.includes("click")) return 1
	if (NORMALIZED.includes("reaction")) return 2
	if (NORMALIZED.includes("action") || NORMALIZED.includes("interaction")) return 3
	return 4
}
