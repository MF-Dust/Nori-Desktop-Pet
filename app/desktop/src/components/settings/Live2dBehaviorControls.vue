<script setup lang="ts">
import {computed, onMounted, ref, watch} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {RUNTIME} from "../../services/runtime"

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
const renderScale = ref(2)
const maxFps = ref(0)
const modelScale = ref(1)
const timers = new Map<string, ReturnType<typeof setTimeout>>()

const saveBehavior = (key: string, value: boolean | number) => {
	clearTimeout(timers.get(key))
	timers.set(key, setTimeout(() => {
		timers.delete(key)
		void RUNTIME.setModelBehavior({[key]: value}).catch(error => console.error(`保存桌宠行为失败 (${key}):`, error))
	}, 400))
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
		renderScale.value = BEHAVIOR.renderScale
		maxFps.value = BEHAVIOR.maxFps
	}
	if (props.modelId) {
		try { modelScale.value = (await RUNTIME.modelMeta(props.modelId)).scale } catch (error) { console.error("读取模型缩放失败:", error) }
	}
})

watch(() => props.modelId, async modelId => {
	if (!modelId) return
	try { modelScale.value = (await RUNTIME.modelMeta(modelId)).scale } catch (error) { console.error("读取模型缩放失败:", error) }
})

const onModelScaleUpdate = (value: number) => {
	modelScale.value = value
	clearTimeout(timers.get("modelScale"))
	timers.set("modelScale", setTimeout(() => {
		timers.delete("modelScale")
		void RUNTIME.setModelDisplay(props.modelId, {scale: value}).catch(error => console.error("保存模型缩放失败:", error))
	}, 400))
}

const onRenderScaleUpdate = (value: number) => {
	renderScale.value = value
	saveBehavior("renderScale", value)
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
	<div class="behavior-controls">
		<h3 class="behavior-title">{{ I18N.title }}</h3>

		<div class="toggle-grid">
			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.clickInteraction }}</span>
					<span class="toggle-desc">{{ I18N.clickInteractionDesc }}</span>
				</div>
				<n-switch v-model:value="clickInteractionToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.autoBlink }}</span>
					<span class="toggle-desc">{{ I18N.autoBlinkDesc }}</span>
				</div>
				<n-switch v-model:value="autoBlinkToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.eyeTracking }}</span>
					<span class="toggle-desc">{{ I18N.eyeTrackingDesc }}</span>
				</div>
				<n-switch v-model:value="eyeTrackingToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.idleEyeAnimation }}</span>
					<span class="toggle-desc">{{ I18N.idleEyeAnimationDesc }}</span>
				</div>
				<n-switch v-model:value="idleEyeToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.idleAnimation }}</span>
					<span class="toggle-desc">{{ I18N.idleAnimationDesc }}</span>
				</div>
				<n-switch v-model:value="idleAnimToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.expressionEnabled }}</span>
					<span class="toggle-desc">{{ I18N.expressionEnabledDesc }}</span>
				</div>
				<n-switch v-model:value="expressionEnabledToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.lipSync }}</span>
					<span class="toggle-desc">{{ I18N.lipSyncDesc }}</span>
				</div>
				<n-switch v-model:value="lipSyncToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.shadow }}</span>
					<span class="toggle-desc">{{ I18N.shadowDesc }}</span>
				</div>
				<n-switch v-model:value="shadowToggle"/>
			</div>

			<div class="toggle-row">
				<div class="toggle-info">
					<span class="toggle-label">{{ I18N.beatSync }}</span>
					<span class="toggle-desc">{{ I18N.beatSyncDesc }}</span>
				</div>
				<n-switch v-model:value="beatSyncToggle"/>
			</div>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.modelScale }}</span>
			<span class="toggle-desc">{{ I18N.modelScaleDesc }}</span>
			<div class="scale-row">
				<n-slider
					:value="modelScale"
					:min="0.5"
					:max="2"
					:step="0.05"
					:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
					class="scale-slider"
					@update:value="onModelScaleUpdate"
				/>
				<span class="scale-value">{{ Math.round(modelScale * 100) }}%</span>
			</div>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.renderScale }}</span>
			<span class="toggle-desc">{{ I18N.renderScaleDesc }}</span>
			<div class="scale-row">
				<n-slider
					:value="renderScale"
					:min="0.5"
					:max="2"
					:step="0.25"
					:format-tooltip="(v: number) => `${v.toFixed(2)}x`"
					class="scale-slider"
					@update:value="onRenderScaleUpdate"
				/>
				<span class="scale-value">{{ renderScale.toFixed(2) }}x</span>
			</div>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.maxFps }}</span>
			<n-select
				:value="maxFps"
				:options="fpsOptions"
				class="fps-select"
				@update:value="onMaxFpsUpdate"
			/>
		</div>
	</div>
</template>

<style scoped lang="less">
.behavior-controls {
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	width: 100%;
}

.behavior-title {
	margin: 0;
	font-size: 1.6rem;
	font-weight: 700;
	color: var(--text-primary);
}

.toggle-grid {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.toggle-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
	gap: 1.4rem;
	padding: 0.9rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		background: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}
}

.toggle-info {
	display: flex;
	flex-direction: column;
	gap: 0.25rem;
	flex: 1;
}

.toggle-label {
	font-size: 1.25rem;
	color: var(--text-primary);
	font-weight: 500;
}

.toggle-desc {
	font-size: 1.1rem;
	color: var(--text-faint);
	line-height: 1.35;
}

.scale-slider {
	flex: 1;
}

.adjust-section {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
	padding: 0.8rem 1.1rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
}

.scale-row {
	display: flex;
	align-items: center;
	gap: 1rem;
}

.adjust-range {
	flex: 1;
	height: 0.6rem;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.scale-value {
	width: 5rem;
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
	font-family: monospace;
	font-weight: 600;
	text-align: right;
}

.fps-select {
	width: 14rem;
	padding: 0.6rem 1rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 1rem var(--glow-teal-soft);
	}
}
</style>