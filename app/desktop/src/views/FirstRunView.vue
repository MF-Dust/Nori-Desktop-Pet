<script setup lang="ts">
import {computed, ref, onMounted} from "vue"
import useLanguages from "../services/i18n/useLanguages.ts"
import {RUNTIME} from "../services/runtime"
import Icon from "../components/Icon.vue"
import TitleBar from "../components/TitleBar.vue"
import AppButton from "../components/ui/AppButton.vue"
import Welcome from "../components/firstRun/Welcome.vue"
import LanguageSelect from "../components/firstRun/LanguageSelect.vue"
import ModelSelect from "../components/firstRun/ModelSelect.vue"
import Ready from "../components/firstRun/Ready.vue"
import {createWizard, WIZARD_STEPS} from "../services/firstRun/wizard"
import {APP_VERSION} from "../services/version"

const I18N = computed(() => useLanguages().views.firstRun)

// 首次运行期间仅保存用于展示/日志的快照信息
const appVersion = ref(APP_VERSION)
const selectedModel = ref("")
const telemetryEnabled = ref(true)

// 步骤名称 (i18n)
const STEP_LABELS = computed(() => [
	I18N.value.steps.welcome,
	I18N.value.steps.language,
	I18N.value.steps.model,
	I18N.value.steps.ready,
])
const STEPS_COUNT = WIZARD_STEPS.length

// 组件挂载后拉取后端快照
onMounted(async () => {
	await RUNTIME.init()
	appVersion.value = RUNTIME.snapshot.value?.app.appVersion ?? appVersion.value
	const SNAPSHOT = RUNTIME.snapshot.value
	const SELECTED = SNAPSHOT?.models.selected ?? ""
	selectedModel.value = SNAPSHOT?.models.items.some(item => item.id === SELECTED && item.installed) ? SELECTED : ""
})

// 向导状态机 (步进、守卫与提交状态全在 services/firstRun/wizard.ts)
const WIZARD = createWizard(async () => {
	await RUNTIME.completeFirstRun(selectedModel.value, telemetryEnabled.value)
	await RUNTIME.writeLog("info", `初始化完成 (${appVersion.value}, model=${selectedModel.value})`)
})

const state = ref(WIZARD.snapshot())
const sync = () => {
	state.value = WIZARD.snapshot()
}

const currentStep = computed(() => state.value.index)
const direction = computed(() => state.value.direction)
const isFirst = computed(() => state.value.isFirst)
const isLast = computed(() => state.value.isLast)
const submitting = computed(() => state.value.finishState === "submitting")
const stepError = computed(() => state.value.stepError)
const finishError = computed(() => state.value.finishError)

// 各步骤的环境光晕 (只改光晕位置与尺寸, 底色统一走深海蓝令牌)
// 注意: UnoCSS 是静态扇描, 类名必须在源码里字面出现 —— 不能用模板拼接
const STEP_GLOW = [
	"bg-[radial-gradient(64rem_40rem_at_85%_30%,var(--glow-teal-soft),transparent_65%),linear-gradient(160deg,var(--bg-panel)_0%,var(--bg-deep)_55%,var(--bg-abyss)_100%)]",
	"bg-[radial-gradient(62rem_42rem_at_50%_115%,var(--glow-teal-soft),transparent_60%),linear-gradient(160deg,var(--bg-panel)_0%,var(--bg-deep)_55%,var(--bg-abyss)_100%)]",
	"bg-[radial-gradient(52rem_38rem_at_50%_48%,var(--glow-teal-soft),transparent_70%),linear-gradient(160deg,var(--bg-panel)_0%,var(--bg-deep)_55%,var(--bg-abyss)_100%)]",
	"bg-[radial-gradient(56rem_40rem_at_50%_50%,var(--glow-teal),transparent_68%),linear-gradient(160deg,var(--bg-panel)_0%,var(--bg-deep)_55%,var(--bg-abyss)_100%)]",
]

// 下一步 / 上一步
const next = () => {
	WIZARD.next()
	sync()
}

const prev = () => {
	WIZARD.prev()
	sync()
}

// 模型选择失败: 阻止前进并把错误摆到底部
const onModelError = (message: string) => {
	if (message) WIZARD.blockStep(message)
	else WIZARD.clearStep()
	sync()
}

const onModelSelected = (modelId: string) => {
	selectedModel.value = modelId
}

const onTelemetryChanged = (enabled: boolean) => {
	telemetryEnabled.value = enabled
}

// 关闭窗口
const closeApp = () => {
	void RUNTIME.exitApp()
}

// 完成初始化 (失败保留在末步可重试)
const finish = async () => {
	await WIZARD.finish()
	sync()
}
</script>

<template>
	<div
		class="w-full h-full flex flex-col overflow-hidden select-none rounded-lg text-text-body
			shadow-[0_1.2rem_3.6rem_rgba(0,0,0,0.65),inset_0_0_0_0.1rem_var(--line-subtle)] transition-[background] duration-600"
		:class="STEP_GLOW[currentStep]"
	>
		<TitleBar>
			<div class="flex items-center justify-center">
				<div class="flex items-center gap-3.5 px-4 py-1.5 rounded-pill bg-bg-abyss/70 border border-line-strong backdrop-blur-[1.2rem] shadow-[0_0.4rem_1.6rem_rgba(0,0,0,0.4)]">
					<div
						v-for="(label, idx) in STEP_LABELS"
						:key="idx"
						class="flex items-center gap-1.5 text-xs transition-all duration-300"
						:class="idx === currentStep
							? 'text-nori-teal-bright font-600 [text-shadow:0_0_0.8rem_var(--glow-teal-soft)]'
							: (idx < currentStep ? 'text-nori-teal-soft' : 'text-text-faint')"
					>
						<span
							class="rounded-full transition-all duration-300"
							:class="idx === currentStep
								? 'w-[0.75rem] h-[0.75rem] bg-nori-teal-bright shadow-[0_0_1rem_var(--glow-teal)]'
								: (idx < currentStep ? 'w-1.5 h-1.5 bg-nori-teal' : 'w-1.5 h-1.5 bg-white/20')"
						/>
						<span>{{ label }}</span>
					</div>
				</div>
			</div>

			<div class="flex items-center gap-3">
				<div class="flex items-center gap-2 px-2.5 py-1 rounded-sm bg-white/4 border border-line-subtle backdrop-blur-[0.8rem]">
					<div class="flex gap-1.2">
						<span
							v-for="i in STEPS_COUNT"
							:key="i"
							class="w-[2rem] h-[0.4rem] rounded-pill transition-all duration-300"
							:class="i <= currentStep + 1
								? 'bg-gradient-to-r from-nori-teal-bright to-nori-teal shadow-[0_0_0.8rem_var(--glow-teal-soft)]'
								: 'bg-white/12'"
						/>
					</div>
					<span class="text-xs text-text-faint mono font-500">{{ currentStep + 1 }} / {{ STEPS_COUNT }}</span>
				</div>
				<button type="button" class="close-btn focus-ring" :aria-label="I18N.close" :title="I18N.close" @click="closeApp">
					<Icon name="close" class="close-icon"/>
				</button>
			</div>
		</TitleBar>

		<!-- 舞台: 720×480 固定窗口下内容可能超高, 让舞台自己滚动而不是被窗口裁掉 -->
		<div class="relative flex-1 w-full scroll-area">
			<Transition :name="direction > 0 ? 'page-next' : 'page-prev'" mode="out-in">
				<Welcome v-if="currentStep === 0"/>
				<LanguageSelect v-else-if="currentStep === 1"/>
				<ModelSelect
					v-else-if="currentStep === 2"
					@error="onModelError"
					@selected="onModelSelected"
				/>
				<Ready v-else @telemetry-changed="onTelemetryChanged"/>
			</Transition>
		</div>

		<!-- 底部导航 -->
		<div class="relative z-2 h-16 shrink-0 flex items-center justify-between gap-3 px-8 bg-bg-abyss/75 border-t border-line-subtle backdrop-blur-[1.4rem]">
			<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/22 to-transparent pointer-events-none"/>

			<AppButton v-if="!isFirst" icon="arrow-left" :disabled="submitting" @click="prev">
				{{ I18N.back }}
			</AppButton>
			<span v-else class="w-2.5"/>

			<p v-if="stepError || finishError" class="flex-1 inline-flex items-center justify-center gap-1.5 m-0 text-sm text-danger-text text-center" role="alert">
				<Icon name="info" :size="13"/>
				<span>{{ stepError || finishError }}</span>
			</p>

			<AppButton v-if="!isLast" variant="primary" :disabled="!state.canNext" @click="next">
				<span class="inline-flex items-center gap-2">
					<span>{{ I18N.next }}</span>
					<Icon name="arrow-right" :size="15"/>
				</span>
			</AppButton>
			<AppButton
				v-else
				variant="primary"
				icon="sparkles"
				class="px-7 text-md shadow-[0_0.4rem_2rem_var(--glow-teal-strong)]"
				:loading="submitting"
				@click="finish"
			>
				{{ submitting ? I18N.starting : (finishError ? I18N.retry : I18N.start) }}
			</AppButton>
		</div>
	</div>
</template>
