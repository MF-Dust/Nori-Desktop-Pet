<script setup lang="ts">
import {computed, ref, onMounted} from "vue"
import useLanguages from "../services/i18n/useLanguages.ts"
import {RUNTIME} from "../services/runtime"
import Icon from "../components/Icon.vue"
import TitleBar from "../components/TitleBar.vue"
import Welcome from "../components/firstRun/Welcome.vue"
import LanguageSelect from "../components/firstRun/LanguageSelect.vue"
import ModelSelect from "../components/firstRun/ModelSelect.vue"
import Ready from "../components/firstRun/Ready.vue"

const I18N = computed(() => useLanguages().views.firstRun)

// 首次运行期间仅保存用于展示/日志的快照信息
const appVersion = ref("0.1.0")
const selectedModel = ref("arg-nori")

// 步骤配置与名称
const STEP_LABELS = ["欢迎", "语言", "形象", "就绪"]
const STEPS_COUNT = STEP_LABELS.length

// 组件挂载后拉取后端快照
onMounted(async () => {
	await RUNTIME.init()
	appVersion.value = RUNTIME.snapshot.value?.app.appVersion ?? appVersion.value
	selectedModel.value = RUNTIME.snapshot.value?.models.selected ?? selectedModel.value
})

// 当前步骤索引
const currentStep = ref(0)

// 切换方向: 1 = 下一步, -1 = 上一步 (决定动画方向)
const direction = ref(1)

// 当前步骤是否为第一个
const isFirst = computed(() => currentStep.value === 0)

// 当前步骤是否为最后一个
const isLast = computed(() => currentStep.value === STEPS_COUNT - 1)

// 下一步
const next = () => {
	if (isLast.value) return
	direction.value = 1
	currentStep.value++
}

// 上一步
const prev = () => {
	if (isFirst.value) return
	direction.value = -1
	currentStep.value--
}

// 关闭窗口
const closeApp = () => {
	void RUNTIME.exitApp()
}

// 完成初始化
const finish = async () => {
	try {
		await RUNTIME.completeFirstRun()
		await RUNTIME.writeLog("info", `初始化完成 (v${appVersion.value}, model=${selectedModel.value})`)
	} catch (error) {
		console.error("首次运行失败:", error)
	}
}
</script>

<template>
	<div class="first-run-window" :class="`bg-step-${currentStep + 1}`">
		<TitleBar>
			<div class="titlebar-center">
				<div class="step-badge-group">
					<div
						v-for="(label, idx) in STEP_LABELS"
						:key="idx"
						class="step-badge-item"
						:class="{active: idx === currentStep, done: idx < currentStep}"
					>
						<span class="step-dot"/>
						<span class="step-text">{{ label }}</span>
					</div>
				</div>
			</div>

			<div class="titlebar-right">
				<div class="steps-progress-wrap">
					<div class="steps-indicator">
						<span
							v-for="i in STEPS_COUNT"
							:key="i"
							class="seg"
							:class="{active: i <= currentStep + 1}"
						/>
					</div>
					<span class="step-count">{{ currentStep + 1 }} / {{ STEPS_COUNT }}</span>
				</div>
				<button class="close-btn" title="关闭" @click="closeApp">
					<Icon name="close" class="close-icon"/>
				</button>
			</div>
		</TitleBar>

		<div class="stage">
			<Transition :name="direction > 0 ? 'page-next' : 'page-prev'" mode="out-in">
				<Welcome v-if="currentStep === 0"/>
				<LanguageSelect v-else-if="currentStep === 1"/>
				<ModelSelect v-else-if="currentStep === 2"/>
				<Ready v-else/>
			</Transition>
		</div>

		<!-- 底部导航 -->
		<div class="footer">
			<button v-if="!isFirst" class="btn btn-ghost" @click="prev">
				<Icon name="arrow-left" class="btn-icon"/>
				<span>{{ I18N.back }}</span>
			</button>
			<span v-else class="footer-spacer"/>

			<button v-if="!isLast" class="btn btn-primary" @click="next">
				<span>{{ I18N.next }}</span>
				<Icon name="arrow-right" class="btn-icon"/>
			</button>
			<button v-else class="btn btn-primary btn-start" @click="finish">
				<Icon name="sparkles" class="btn-icon"/>
				<span>{{ I18N.start }}</span>
			</button>
		</div>
	</div>
</template>

<style scoped lang="less">
.first-run-window {
	width: 100%;
	height: 100%;
	border-radius: var(--radius-lg);
	display: flex;
	flex-direction: column;
	overflow: hidden;
	user-select: none;
	color: var(--text-body);
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	box-shadow: 0 1.2rem 3.6rem rgba(0, 0, 0, 0.65), inset 0 0 0 0.1rem var(--line-subtle);
	transition: background 0.6s cubic-bezier(0.2, 0.8, 0.2, 1);
	position: relative;

	// 各步骤动态环境光晕
	&.bg-step-1 {
		background-image: radial-gradient(64rem 40rem at 85% 30%, rgba(94, 234, 212, 0.18), transparent 65%),
			radial-gradient(40rem 28rem at 15% 85%, rgba(125, 227, 255, 0.08), transparent 60%),
			linear-gradient(160deg, #10324e 0%, var(--bg-deep) 58%, var(--bg-abyss) 100%);
	}

	&.bg-step-2 {
		background-image: radial-gradient(62rem 42rem at 50% 115%, rgba(127, 212, 232, 0.2), transparent 60%),
			radial-gradient(38rem 26rem at 50% 0%, rgba(94, 234, 212, 0.1), transparent 60%),
			linear-gradient(160deg, #0e2e48 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}

	&.bg-step-3 {
		background-image: radial-gradient(52rem 38rem at 50% 48%, rgba(125, 227, 255, 0.16), transparent 70%),
			radial-gradient(40rem 26rem at 20% 20%, rgba(94, 234, 212, 0.1), transparent 60%),
			linear-gradient(160deg, #0c2642 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}

	&.bg-step-4 {
		background-image: radial-gradient(56rem 40rem at 50% 50%, rgba(94, 234, 212, 0.22), transparent 68%),
			radial-gradient(48rem 32rem at 50% 10%, rgba(125, 227, 255, 0.15), transparent 60%),
			linear-gradient(160deg, #123654 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}
}

.titlebar-center {
	display: flex;
	align-items: center;
	justify-content: center;
}

.step-badge-group {
	display: flex;
	align-items: center;
	gap: 1.4rem;
	padding: 0.35rem 1.2rem;
	background: rgba(0, 0, 0, 0.25);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	backdrop-filter: blur(0.8rem);
}

.step-badge-item {
	display: flex;
	align-items: center;
	gap: 0.5rem;
	font-size: 1.15rem;
	color: var(--text-faint);
	transition: all 0.3s ease;

	.step-dot {
		width: 0.5rem;
		height: 0.5rem;
		border-radius: 50%;
		background: rgba(255, 255, 255, 0.2);
		transition: all 0.3s ease;
	}

	&.active {
		color: var(--nori-teal-bright);
		font-weight: 600;

		.step-dot {
			width: 0.7rem;
			height: 0.7rem;
			background: var(--nori-teal-bright);
			box-shadow: 0 0 0.8rem var(--glow-teal);
		}
	}

	&.done {
		color: var(--nori-teal-soft);

		.step-dot {
			background: var(--nori-teal);
		}
	}
}

.titlebar-right {
	display: flex;
	align-items: center;
	gap: 1.2rem;

	.steps-progress-wrap {
		display: flex;
		align-items: center;
		gap: 0.8rem;
		background: rgba(255, 255, 255, 0.04);
		padding: 0.3rem 0.8rem;
		border-radius: var(--radius-sm);
		border: 0.1rem solid var(--line-subtle);
	}

	.steps-indicator {
		display: flex;
		gap: 0.3rem;
	}

	.seg {
		width: 1.8rem;
		height: 0.35rem;
		border-radius: 0.2rem;
		background-color: rgba(255, 255, 255, 0.12);
		transition: all 0.3s ease;

		&.active {
			background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
			box-shadow: 0 0 0.8rem var(--glow-teal-soft);
		}
	}

	.step-count {
		font-size: 1.1rem;
		color: var(--text-faint);
		font-variant-numeric: tabular-nums;
		font-family: monospace;
	}
}

// 舞台
.stage {
	flex: 1;
	width: 100%;
	height: 100%;
	min-height: 0;
	position: relative;
}

// 页面过渡: 带有细微缩放与透明度的平滑滑入
.page-next-enter-active,
.page-next-leave-active,
.page-prev-enter-active,
.page-prev-leave-active {
	transition: opacity 0.28s ease, transform 0.28s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.page-next-enter-from {
	opacity: 0;
	transform: translateX(3rem) scale(0.98);
}

.page-next-leave-to {
	opacity: 0;
	transform: translateX(-3rem) scale(0.98);
}

.page-prev-enter-from {
	opacity: 0;
	transform: translateX(-3rem) scale(0.98);
}

.page-prev-leave-to {
	opacity: 0;
	transform: translateX(3rem) scale(0.98);
}

// 底部导航
.footer {
	padding: 0 3.2rem;
	height: 6.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	flex-shrink: 0;
	background: rgba(5, 14, 26, 0.4);
	border-top: 0.1rem solid var(--line-subtle);
	backdrop-filter: blur(0.8rem);

	.footer-spacer {
		width: 1rem;
	}

	.btn-icon {
		width: 1.5rem;
		height: 1.5rem;
		flex-shrink: 0;
	}

	.btn-start {
		padding: 1rem 2.6rem;
		font-size: 1.4rem;
		box-shadow: 0 0.4rem 2rem var(--glow-teal-strong);
	}
}
</style>

