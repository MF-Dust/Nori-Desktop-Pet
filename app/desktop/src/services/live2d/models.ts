import type {ModelId} from "./types"
import arNoriThumb from "../../assets/images/live2D/ARGNori.webp"
import noriThumb from "../../assets/images/live2D/Nori.webp"

export interface ModelCatalogEntry {
	id: ModelId
	name: string
	thumb: string
}

export const MODEL_CATALOG: ModelCatalogEntry[] = [
	{id: "arg-nori", name: "ARG Nori", thumb: arNoriThumb},
	{id: "nori", name: "Nori", thumb: noriThumb},
]

export const DEFAULT_MODEL: ModelId = "arg-nori"

export const MODEL_CONFIG_KEY = "selected_model"
