/**
 * 运动管理器插件管线
 *
 * 参考 AIRI: packages/stage-ui-live2d/src/composables/live2d/motion-manager.ts
 *
 * 提供 pre/post/final 三个执行阶段，插件按注册顺序执行。
 * 每个插件接收 MotionManagerPluginContext，可设置 handled 短路后续插件。
 * final 阶段插件忽略 handled 始终执行。
 */
import type {Cubism4InternalModel, Cubism4MotionManager} from "pixi-live2d-display/cubism4"
import {type Ref} from "vue"

// CubismModel 类型未导出, 使用 any
type CubismModel = any

export interface MotionManagerPluginContext {
	model: CubismModel
	/** 当前时间 (秒) */
	now: number
	/** 距上一帧时间差 (秒) */
	timeDelta: number
	internalModel: Cubism4InternalModel
	motionManager: Cubism4MotionManager
	modelParameters: Record<string, number>
	live2dEyeTrackingEnabled: boolean
	live2dEyeFocusSourceActive: boolean
	live2dIdleAnimationEnabled: boolean
	live2dForceIdleEyeAnimation: boolean
	live2dAutoBlinkEnabled: boolean
	live2dForceAutoBlinkEnabled: boolean
	live2dBeatSyncEnabled: boolean
	isIdleMotion: boolean
	handled: boolean
	markHandled: () => void
}

export type MotionManagerPlugin = (ctx: MotionManagerPluginContext) => void

export interface UseMotionManagerUpdateOptions {
	internalModel: Cubism4InternalModel
	modelParameters: Record<string, number>
	live2dEyeTrackingEnabled: Ref<boolean>
	live2dEyeFocusSourceActive: Ref<boolean>
	live2dIdleAnimationEnabled: Ref<boolean>
	live2dForceIdleEyeAnimation: Ref<boolean>
	live2dAutoBlinkEnabled: Ref<boolean>
	live2dForceAutoBlinkEnabled: Ref<boolean>
	live2dBeatSyncEnabled: Ref<boolean>
	lastUpdateTime: Ref<number>
}

/**
 * 创建插件管线
 */
export const useMotionManagerUpdate = (options: UseMotionManagerUpdateOptions) => {
	const {
		internalModel,
		modelParameters,
		live2dEyeTrackingEnabled,
		live2dEyeFocusSourceActive,
		live2dIdleAnimationEnabled,
		live2dForceIdleEyeAnimation,
		live2dAutoBlinkEnabled,
		live2dForceAutoBlinkEnabled,
		live2dBeatSyncEnabled,
		lastUpdateTime,
	} = options

	const prePlugins: MotionManagerPlugin[] = []
	const postPlugins: MotionManagerPlugin[] = []
	const finalPlugins: MotionManagerPlugin[] = []

	const register = (plugin: MotionManagerPlugin, stage: "pre" | "post" | "final" = "pre") => {
		if (stage === "pre") prePlugins.push(plugin)
		else if (stage === "final") finalPlugins.push(plugin)
		else postPlugins.push(plugin)
	}

	const runPlugins = (plugins: MotionManagerPlugin[], ctx: MotionManagerPluginContext) => {
		for (const plugin of plugins) {
			if (ctx.handled) break
			plugin(ctx)
		}
	}

	const hookUpdate = (
		model: CubismModel,
		now: number,
		hookedUpdate?: (model: CubismModel, now: number) => boolean,
	) => {
		const timeDelta = lastUpdateTime.value ? now - lastUpdateTime.value : 0
		const motionManager = internalModel.motionManager
		const isIdleMotion = !motionManager.state.currentGroup
			|| motionManager.state.currentGroup === motionManager.groups.idle

		const ctx: MotionManagerPluginContext = {
			model,
			now,
			timeDelta,
			internalModel,
			motionManager,
			modelParameters,
			live2dEyeTrackingEnabled: live2dEyeTrackingEnabled.value,
			live2dEyeFocusSourceActive: live2dEyeFocusSourceActive.value,
			live2dIdleAnimationEnabled: live2dIdleAnimationEnabled.value,
			live2dForceIdleEyeAnimation: live2dForceIdleEyeAnimation.value,
			live2dAutoBlinkEnabled: live2dAutoBlinkEnabled.value,
			live2dForceAutoBlinkEnabled: live2dForceAutoBlinkEnabled.value,
			live2dBeatSyncEnabled: live2dBeatSyncEnabled.value,
			isIdleMotion,
			handled: false,
			markHandled: () => { ctx.handled = true },
		}

		runPlugins(prePlugins, ctx)

		if (!ctx.handled && hookedUpdate) {
			const result = hookedUpdate.call(motionManager, model, now)
			if (result) ctx.handled = true
		}

		runPlugins(postPlugins, ctx)

		// final 阶段始终执行
		for (const plugin of finalPlugins) {
			plugin(ctx)
		}

		lastUpdateTime.value = now
		return ctx.handled
	}

	return {register, hookUpdate}
}