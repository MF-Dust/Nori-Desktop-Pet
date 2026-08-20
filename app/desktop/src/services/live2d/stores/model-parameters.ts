/**
 * 模型参数默认值
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/stores/model-parameters.ts
 */
import {reactive} from "vue"

export const defaultModelParameters = {
	angleX: 0,
	angleY: 0,
	angleZ: 0,
	leftEyeOpen: 1,
	rightEyeOpen: 1,
	leftEyeSmile: 0,
	rightEyeSmile: 0,
	leftEyebrowLR: 0,
	rightEyebrowLR: 0,
	leftEyebrowY: 0,
	rightEyebrowY: 0,
	leftEyebrowAngle: 0,
	rightEyebrowAngle: 0,
	leftEyebrowForm: 0,
	rightEyebrowForm: 0,
	mouthOpen: 0,
	mouthForm: 0,
	cheek: 0,
	bodyAngleX: 0,
	bodyAngleY: 0,
	bodyAngleZ: 0,
	breath: 0,
}

export type ModelParameters = typeof defaultModelParameters

/**
 * 全局模型参数单例
 */
export const modelParameters = reactive<ModelParameters>({...defaultModelParameters})

/**
 * 重置所有参数到默认值
 */
export const resetModelParameters = (): void => {
	Object.assign(modelParameters, defaultModelParameters)
}