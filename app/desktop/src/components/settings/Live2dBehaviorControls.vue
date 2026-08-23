<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {feedback} from "../../services/feedback"
import {RUNTIME} from "../../services/runtime"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const props = withDefaults(defineProps<{
	modelId?: string
}>(), {
	modelId: "",
})

const I18N = computed(() => useLanguages().views.main.model.behavior)

// ---- 状态来自后端快照 ----
const autoBlink = ref(true)
const eyeTracking = ref(true)
const idleEyeAnimation = ref(true)
const idleAnimation = ref(true)
const clickInteraction = ref(true)
const expressionEnabled = ref(true)
const lipSync = ref(true)
const shadow = ref(true)
const beatSync = ref(false)
const aiInteraction = ref(false)
const renderScale = ref(2)
const maxFps = ref(0)
const modelScale = ref(1)

// 是否已配置 AI (未配置时禁用 AI 互动开关)
const aiConfigured = computed(() => Boolean(RUNTIME.snapshot.value?.ai.configured))

// 保存辅助: 每个字段独立防抖 timer + 卸载 flush (规范要求)
const SAVE = useDebouncedSave({
	onError: (key, error) => feedback.error(key === "modelScale" ? I18N.value.scaleSaveFailed : I18N.value.saveFailed, error),
})

const saveBehavior = (key: string, value: boolean | number) => {
	void SAVE.saveNow(key, () => RUNTIME.setModelBehavior({[key]: value}))
}

const makeToggle = (key: string, state: {value: boolean}) => computed({
	get: () => state.value,
	set: (value: boolean) => {
		state.value = value
		saveBehavior(key, value)
	},
})

const autoBlinkToggle = makeToggle("autoBlink", autoBlink)
const eyeTrackingToggle = makeToggle("eyeTracking", eyeTracking)
const idleEyeToggle = makeToggle("idleEyeAnimation", idleEyeAnimation)
const idleAnimToggle = makeToggle("idleAnimation", idleAnimation)
const clickInteractionToggle = makeToggle("clickInteraction", clickInteraction)
const expressionEnabledToggle = makeToggle("expressionEnabled", expressionEnabled)
const lipSyncToggle = makeToggle("lipSync", lipSync)
const shadowToggle = makeToggle("shadow", shadow)
const beatSyncToggle = makeToggle("beatSync", beatSync)
const aiInteractionToggle = computed({
	get: () => aiInteraction.value && aiConfigured.value,
	set: (value: boolean) => {
		aiInteraction.value = value
		saveBehavior("aiInteraction", value)
	},
})

onMounted(async () => {
	await RUNTIME.init()
	const BEHAVIOR = RUNTIME.snapshot.value?.behaviors
	if (BEHAVIOR) {
		autoBlink.value = BEHAVIOR.autoBlink
		eyeTracking.value = BEHAVIOR.eyeTracking
		idleEyeAnimation.value = BEHAVIOR.idleEyeAnimation
		idleAnimation.value = BEHAVIOR.idleAnimation
		clickInteraction.value = BEHAVIOR.clickInteraction
		expressionEnabled.value = BEHAVIOR.expressionEnabled
		lipSync.value = BEHAVIOR.lipSync
		shadow.value = BEHAVIOR.shadow
		beatSync.value = BEHAVIOR.beatSync
		aiInteraction.value = BEHAVIOR.aiInteraction ?? false
		renderScale.value = BEHAVIOR.renderScale
		maxFps.value = BEHAVIOR.maxFps
	}
	if (props.modelId) {
		try { modelScale.value = (await RUNTIME.modelMeta(props.modelId)).scale } catch (error) { feedback.error(I18N.value.scaleLoadFailed, error) }
	}
})

watch(() => props.modelId, async modelId => {
	if (!modelId) return
	try { modelScale.value = (await RUNTIME.modelMeta(modelId)).scale } catch (error) { feedback.error(I18N.value.scaleLoadFailed, error) }
})

const onModelScaleUpdate = (value: number) => {
	modelScale.value = value
	SAVE.save("modelScale", () => RUNTIME.setModelDisplay(props.modelId, {scale: value}))
}

const onRenderScaleUpdate = (value: number) => {
	renderScale.value = value
	SAVE.save("renderScale", () => RUNTIME.setModelBehavior({renderScale: value}))
}

const onMaxFpsUpdate = (value: number) => {
	maxFps.value = value
	saveBehavior("maxFps", value)
}

const fpsOptions = computed(() => [
	{label: I18N.value.maxFpsNone, value: 0},
	{label: I18N.value.maxFps30, value: 30},
	{label: I18N.value.maxFps60, value: 60},
])
</script>

<template>
	<div class="w-full flex flex-col gap-3.5">
		<h3 class="m-0 text-lg font-700 text-text-primary">{{ I18N.title }}</h3>

		<div class="flex flex-col gap-1.5">
			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.clickInteraction"
				:desc="I18N.clickInteractionDesc"
			>
				<n-switch v-model:value="clickInteractionToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.aiInteraction"
				:desc="aiConfigured ? I18N.aiInteractionDesc : I18N.aiInteractionDisabledDesc"
				:disabled="!aiConfigured"
			>
				<n-switch v-model:value="aiInteractionToggle" :disabled="!aiConfigured"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.autoBlink"
				:desc="I18N.autoBlinkDesc"
			>
				<n-switch v-model:value="autoBlinkToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.eyeTracking"
				:desc="I18N.eyeTrackingDesc"
			>
				<n-switch v-model:value="eyeTrackingToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.idleEyeAnimation"
				:desc="I18N.idleEyeAnimationDesc"
			>
				<n-switch v-model:value="idleEyeToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.idleAnimation"
				:desc="I18N.idleAnimationDesc"
			>
				<n-switch v-model:value="idleAnimToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.expressionEnabled"
				:desc="I18N.expressionEnabledDesc"
			>
				<n-switch v-model:value="expressionEnabledToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.lipSync"
				:desc="I18N.lipSyncDesc"
			>
				<n-switch v-model:value="lipSyncToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.shadow"
				:desc="I18N.shadowDesc"
			>
				<n-switch v-model:value="shadowToggle"/>
			</AppSwitchRow>

			<AppSwitchRow
				class="px-3 py-[0.9rem] rounded-sm border border-line-subtle bg-white/3 transition-all duration-200
					hover:(bg-nori-teal-bright/6 border-nori-teal-soft)"
				:title="I18N.beatSync"
				:desc="I18N.beatSyncDesc"
			>
				<n-switch v-model:value="beatSyncToggle"/>
			</AppSwitchRow>
		</div>

		<div class="flex flex-col gap-1.5 px-[1.1rem] py-2 rounded-sm border border-line-subtle bg-white/3">
			<span class="text-base font-500 text-text-primary">{{ I18N.modelScale }}</span>
			<span class="text-hint">{{ I18N.modelScaleDesc }}</span>
			<div class="flex items-center gap-2.5">
				<n-slider
					:value="modelScale"
					:min="0.5"
					:max="2"
					:step="0.05"
					:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
					class="flex-1 min-w-0"
					@update:value="onModelScaleUpdate"
				/>
				<span class="w-[5rem] shrink-0 text-sm font-600 text-right text-nori-teal-bright mono">{{ Math.round(modelScale * 100) }}%</span>
			</div>
		</div>

		<div class="flex flex-col gap-1.5 px-[1.1rem] py-2 rounded-sm border border-line-subtle bg-white/3">
			<span class="text-base font-500 text-text-primary">{{ I18N.renderScale }}</span>
			<span class="text-hint">{{ I18N.renderScaleDesc }}</span>
			<div class="flex items-center gap-2.5">
				<n-slider
					:value="renderScale"
					:min="0.5"
					:max="2"
					:step="0.25"
					:format-tooltip="(v: number) => `${v.toFixed(2)}x`"
					class="flex-1 min-w-0"
					@update:value="onRenderScaleUpdate"
				/>
				<span class="w-[5rem] shrink-0 text-sm font-600 text-right text-nori-teal-bright mono">{{ renderScale.toFixed(2) }}x</span>
			</div>
		</div>

		<div class="flex flex-col gap-1.5 px-[1.1rem] py-2 rounded-sm border border-line-subtle bg-white/3">
			<span class="text-base font-500 text-text-primary">{{ I18N.maxFps }}</span>
			<div class="w-[14rem]">
				<n-select
					:value="maxFps"
					:options="fpsOptions"
					@update:value="onMaxFpsUpdate"
				/>
			</div>
		</div>
	</div>
</template>
