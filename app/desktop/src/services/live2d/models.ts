import nori from "../../assets/images/live2D/Nori.webp"
import arNori from "../../assets/images/live2D/ARGNori.webp"

/**
 * Live2D 模型
 */
export interface ModelInfo {
	// 资源目录名 (对应后端模型目录 ID)
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