/**
 * 表情系统插件
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/expression-controller.ts
 *
 * 逐帧从 expression-store 读取表情值并应用到模型参数。
 * 在 final 阶段执行，忽略 handled 状态。
 */
import type {MotionManagerPlugin} from "./index"
import {expressionStore} from "../stores/expression-store"

export interface ExpressionControllerOptions {
	// 内部模型引用, 仅用于获取默认参数值
	internalModel: any
	modelId?: string
}

/**
 * 创建表情控制器
 */
export const useExpressionController = (options: ExpressionControllerOptions) => {
	const {modelId} = options

	const activeLastFrame = new Set<string>()

	/**
	 * 从 model3.json 的 Expressions 中解析并注册表情
	 */
	const initialise = async (
		expressionRefs: {Name: string; File: string}[],
		readExpFile: (path: string) => Promise<string>,
	) => {
		const groups: {name: string; parameters: {parameterId: string; blend: string; value: number}[]}[] = []
		const entryMap = new Map<string, {
			name: string
			parameterId: string
			blend: string
			currentValue: number
			defaultValue: number
			modelDefault: number
			targetValue: number
		}>()

		for (const expRef of expressionRefs) {
			try {
				const raw = await readExpFile(expRef.File)
				const exp3 = JSON.parse(raw) as {
					Type: string
					Parameters: {Id: string; Value: number; Blend: string}[]
				}

				const groupParams: {parameterId: string; blend: string; value: number}[] = []

				for (const param of exp3.Parameters) {
					groupParams.push({
						parameterId: param.Id,
						blend: param.Blend,
						value: param.Value,
					})

					if (!entryMap.has(param.Id)) {
						const modelDefault = getModelParameterDefault(param.Id)
						entryMap.set(param.Id, {
							name: param.Id,
							parameterId: param.Id,
							blend: param.Blend,
							currentValue: modelDefault,
							defaultValue: modelDefault,
							modelDefault,
							targetValue: param.Value,
						})
					} else if (param.Value !== 0) {
						const existing = entryMap.get(param.Id)!
						if (existing.targetValue === 0) existing.targetValue = param.Value
					}
				}

				groups.push({name: expRef.Name, parameters: groupParams})
			} catch {
				// 跳过解析失败的表情
			}
		}

		// 注册到 expression-store
		expressionStore.registerExpressions(
			modelId ?? "unknown",
			groups.map((g) => ({
				name: g.name,
				parameters: g.parameters.map((p) => ({
					parameterId: p.parameterId,
					blend: normaliseBlend(p.blend),
					value: p.value,
				})),
			})),
			Array.from(entryMap.values()).map((e) => ({
				name: e.name,
				parameterId: e.parameterId,
				blend: normaliseBlend(e.blend),
				currentValue: e.currentValue,
				defaultValue: e.defaultValue,
				modelDefault: e.modelDefault,
				targetValue: e.targetValue,
			})),
		)
	}

	/**
	 * 逐帧应用表情 (final 阶段)
	 */
	const applyExpressions: MotionManagerPlugin = (ctx) => {
		const coreModel = ctx.internalModel.coreModel as any
		const activeThisFrame = new Set<string>()

		for (const entry of expressionStore.expressions.values()) {
			if (isNoopValue(entry)) continue

			const blendedValue = computeTargetValue(entry, coreModel)
			coreModel.setParameterValueById(entry.parameterId, blendedValue)
			activeThisFrame.add(entry.parameterId)
		}

		// 重置上一帧活跃但本帧不活跃的参数
		for (const paramId of activeLastFrame) {
			if (!activeThisFrame.has(paramId)) {
				const entry = findEntry(paramId)
				if (entry) coreModel.setParameterValueById(paramId, entry.modelDefault)
			}
		}

		activeLastFrame.clear()
		for (const id of activeThisFrame) activeLastFrame.add(id)
	}

	const isNoopValue = (entry: {blend: string; currentValue: number; modelDefault: number}): boolean => {
		switch (entry.blend) {
			case "Add": return entry.currentValue === 0
			case "Multiply": return entry.currentValue === 1
			default: return entry.currentValue === entry.modelDefault
		}
	}

	const computeTargetValue = (entry: {blend: string; currentValue: number; modelDefault: number; parameterId: string}, coreModel: any): number => {
		switch (entry.blend) {
			case "Add": return entry.modelDefault + entry.currentValue
			case "Multiply": {
				const currentFrame = coreModel.getParameterValueById(entry.parameterId) as number
				return currentFrame * entry.currentValue
			}
			default: return entry.currentValue
		}
	}

	const findEntry = (paramId: string) => {
		for (const entry of expressionStore.expressions.values()) {
			if (entry.parameterId === paramId) return entry
		}
		return undefined
	}

	const getModelParameterDefault = (parameterId: string): number => {
		try {
			const coreModel = options.internalModel.coreModel as any
			const defaultApi = coreModel.getParameterDefaultValueById
			if (typeof defaultApi === "function") {
				const val = defaultApi.call(coreModel, parameterId)
				if (val != null) return val as number
			}
			return (coreModel.getParameterValueById(parameterId) as number) ?? 0
		} catch {
			return 0
		}
	}

	const dispose = () => {
		expressionStore.dispose()
		activeLastFrame.clear()
	}

	return {initialise, applyExpressions, dispose}
}

const normaliseBlend = (raw: string): "Add" | "Multiply" | "Overwrite" => {
	switch (raw) {
		case "Add": return "Add"
		case "Multiply": return "Multiply"
		default: return "Overwrite"
	}
}