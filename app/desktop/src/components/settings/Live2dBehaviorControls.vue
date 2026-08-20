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

const onModelScale = (e: Event) => {
	const val = parseFloat((e.target as HTMLInputElement).value)
	if (!Number.isNaN(val)) {
		modelScale.value = val
		debouncedWrite(l2dModelKey("l2d_scale", props.modelId), val)
	}
}

const onRenderScale = (e: Event) => {
	const val = parseFloat((e.target as HTMLInputElement).value)
	if (!Number.isNaN(val)) {
		renderScale.value = val
		debouncedWrite("l2d_render_scale", val)
	}
}

const onMaxFps = (e: Event) => {
	const val = parseInt((e.target as HTMLSelectElement).value, 10)
	if (!Number.isNaN(val)) {
		maxFps.value = val
		debouncedWrite("l2d_max_fps", val)
	}
}
</script>

<template>
	<div class="behavior-controls">
		<h3 class="behavior-title">{{ I18N.title }}</h3>

		<div class="toggle-grid">
			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.clickInteraction }}</span>
				<span class="toggle-desc">{{ I18N.clickInteractionDesc }}</span>
				<input v-model="clickInteractionToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.autoBlink }}</span>
				<span class="toggle-desc">{{ I18N.autoBlinkDesc }}</span>
				<input v-model="autoBlinkToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.eyeTracking }}</span>
				<span class="toggle-desc">{{ I18N.eyeTrackingDesc }}</span>
				<input v-model="eyeTrackingToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.idleEyeAnimation }}</span>
				<span class="toggle-desc">{{ I18N.idleEyeAnimationDesc }}</span>
				<input v-model="idleEyeToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.idleAnimation }}</span>
				<span class="toggle-desc">{{ I18N.idleAnimationDesc }}</span>
				<input v-model="idleAnimToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.expressionEnabled }}</span>
				<span class="toggle-desc">{{ I18N.expressionEnabledDesc }}</span>
				<input v-model="expressionEnabledToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.lipSync }}</span>
				<span class="toggle-desc">{{ I18N.lipSyncDesc }}</span>
				<input v-model="lipSyncToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.shadow }}</span>
				<span class="toggle-desc">{{ I18N.shadowDesc }}</span>
				<input v-model="shadowToggle" type="checkbox" class="toggle-input"/>
			</label>

			<label class="toggle-row">
				<span class="toggle-label">{{ I18N.beatSync }}</span>
				<span class="toggle-desc">{{ I18N.beatSyncDesc }}</span>
				<input v-model="beatSyncToggle" type="checkbox" class="toggle-input"/>
			</label>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.modelScale }}</span>
			<span class="toggle-desc">{{ I18N.modelScaleDesc }}</span>
			<div class="scale-row">
				<input
					:value="modelScale"
					class="adjust-range"
					type="range"
					min="0.5"
					max="2"
					step="0.05"
					@input="onModelScale"
				/>
				<span class="scale-value">{{ Math.round(modelScale * 100) }}%</span>
			</div>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.renderScale }}</span>
			<span class="toggle-desc">{{ I18N.renderScaleDesc }}</span>
			<div class="scale-row">
				<input
					:value="renderScale"
					class="adjust-range"
					type="range"
					min="0.5"
					max="2"
					step="0.25"
					@input="onRenderScale"
				/>
				<span class="scale-value">{{ renderScale.toFixed(2) }}x</span>
			</div>
		</div>

		<div class="adjust-section">
			<span class="toggle-label">{{ I18N.maxFps }}</span>
			<select :value="maxFps" class="fps-select" @change="onMaxFps">
				<option :value="0">{{ I18N.maxFpsNone }}</option>
				<option :value="30">{{ I18N.maxFps30 }}</option>
				<option :value="60">{{ I18N.maxFps60 }}</option>
			</select>
		</div>
	</div>
</template>

<style scoped lang="less">
.behavior-controls {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	width: 100%;
}

.behavior-title {
	margin: 0;
	font-size: 1.5rem;
	font-weight: 700;
	color: var(--text-primary);
}

.toggle-grid {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.toggle-row {
	display: grid;
	grid-template-columns: 1fr auto;
	grid-template-rows: auto auto;
	column-gap: 1.2rem;
	row-gap: 0.2rem;
	align-items: center;
	padding: 0.6rem 0.8rem;
	border-radius: var(--radius-sm);
	cursor: pointer;
	transition: background 0.15s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.06);
	}
}

.toggle-label {
	grid-column: 1;
	grid-row: 1;
	font-size: 1.15rem;
	color: var(--text-primary);
	font-weight: 500;
}

.toggle-desc {
	grid-column: 1;
	grid-row: 2;
	font-size: 1.0rem;
	color: var(--text-faint);
	line-height: 1.3;
}

.toggle-input {
	grid-column: 2;
	grid-row: 1 / 3;
	width: 1.6rem;
	height: 1.6rem;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.adjust-section {
	display: flex;
	flex-direction: column;
	gap: 0.5rem;
}

.scale-row {
	display: flex;
	align-items: center;
	gap: 0.8rem;
}

.adjust-range {
	flex: 1;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.scale-value {
	width: 5rem;
	font-size: 1.15rem;
	color: var(--text-faint);
	font-variant-numeric: tabular-nums;
}

.fps-select {
	width: 12rem;
	padding: 0.4rem 0.8rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-body);
	font-size: 1.15rem;
	font-family: inherit;
	cursor: pointer;
}
</style>