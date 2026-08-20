/**
 * 模型动作组读取 (不依赖挂载, 直接解析 model3.json)
 * 输出格式与控制器 getMotions 一致:
 * [{group: "Idle", names: ["01_Idle_Loop", ...]}]  (与 pixi-live2d-display definitions 对齐)
 */
import {assetUrl, resolveModelFileBase} from "./config"
import type {MotionGroup} from "./index"

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
