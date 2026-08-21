<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {l2dModelKey, readBehaviorConfig, readModelConfig, writeBehaviorConfig, type L2DBehaviorKey} from "../../services/live2d/config"

const props = withDefaults(defineProps<{
	modelId?: string
}>(), {
	modelId: "",
})

const I18N = computed(() => useLanguages().views.main.model.behavior)

// ---- 状态 ----
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

// 模型缩放 (按模型存储)
const modelScale = ref(1)

// 防抖定时器
const timers: Partial<Record<string, ReturnType<typeof setTimeout>>> = {}

const debouncedWrite = (key: string, value: string | number | boolean) => {
	if (timers[key]) clearTimeout(timers[key])
	timers[key] = setTimeout(() => {
		timers[key] = undefined
		if (key.startsWith("l2d_scale")) {
			void writeBehaviorConfig(key as any, String(value))
			// 广播全局缩放键让桌宠窗口热更新
			void writeBehaviorConfig("l2d_scale" as any, String(value))
		} else {
			void writeBehaviorConfig(key as L2DBehaviorKey, value)
		}
	}, 400)
}

const makeToggle = (key: L2DBehaviorKey, refVal: {value: boolean}) => computed({
	get: () => refVal.value,
	set: (v: boolean) => {
		refVal.value = v
		debouncedWrite(key, v)
	},
})

const autoBlinkToggle = makeToggle("l2d_auto_blink", autoBlink)
const eyeTrackingToggle = makeToggle("l2d_eye_tracking", eyeTracking)
const idleEyeToggle = makeToggle("l2d_idle_eye_animation", idleEyeAnimation)
const idleAnimToggle = makeToggle("l2d_idle_animation", idleAnimation)
const clickInteractionToggle = makeToggle("l2d_click_interaction", clickInteraction)
const expressionEnabledToggle = makeToggle("l2d_expression_enabled", expressionEnabled)
const lipSyncToggle = makeToggle("l2d_lip_sync", lipSync)
const shadowToggle = makeToggle("l2d_shadow", shadow)
const beatSyncToggle = makeToggle("l2d_beat_sync", beatSync)

onMounted(async () => {
	autoBlink.value = (await readBehaviorConfig("l2d_auto_blink")) === true
	eyeTracking.value = (await readBehaviorConfig("l2d_eye_tracking")) !== false
	idleEyeAnimation.value = (await readBehaviorConfig("l2d_idle_eye_animation")) !== false
	idleAnimation.value = (await readBehaviorConfig("l2d_idle_animation")) !== false
	clickInteraction.value = (await readBehaviorConfig("l2d_click_interaction")) !== false
	expressionEnabled.value = (await readBehaviorConfig("l2d_expression_enabled")) !== false
	lipSync.value = (await readBehaviorConfig("l2d_lip_sync")) !== false
	shadow.value = (await readBehaviorConfig("l2d_shadow")) !== false
	beatSync.value = (await readBehaviorConfig("l2d_beat_sync")) === true
	renderScale.value = (await readBehaviorConfig("l2d_render_scale")) as number || 2
	maxFps.value = (await readBehaviorConfig("l2d_max_fps")) as number || 0
	if (props.modelId) {
		modelScale.value = await readModelConfig(props.modelId, "l2d_scale", (v) => {
			if (typeof v === "number") return v
			if (typeof v === "string") { const n = parseFloat(v); return Number.isNaN(n) ? null : n }
			return null
		}, 1) as number
	}
})

const onModelScaleUpdate = (val: number) => {
	modelScale.value = val
	debouncedWrite(l2dModelKey("l2d_scale", props.modelId), val)
}

const onRenderScaleUpdate = (val: number) => {
	renderScale.value = val
	debouncedWrite("l2d_render_scale", val)
}

const onMaxFpsUpdate = (val: number) => {
	maxFps.value = val
	debouncedWrite("l2d_max_fps", val)
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