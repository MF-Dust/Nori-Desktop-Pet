import nori from "../../assets/images/live2D/Nori.webp"
import arNori from "../../assets/images/live2D/ARGNori.webp"

/**
 * Live2D 模型
 */
export interface ModelInfo {
	// 资源名 (对应后端 check_resource / ensure_resource 的 name)
	id: string
	// 展示名
	name: string
	// 缩略图
	thumb: string
}

/**
 * 可选模型列表 (与引导页共用)
 */
export const MODEL_LIST: ModelInfo[] = [
	{id: "arg-nori", name: "ARG Nori", thumb: arNori},
	{id: "nori", name: "Nori", thumb: nori},
]