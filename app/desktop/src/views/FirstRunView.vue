<script setup lang="ts">
import {computed, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWindow} from "@tauri-apps/api/window"
import Welcome from "../components/firstRun/Welcome.vue"
import LanguageSelect from "../components/firstRun/LanguageSelect.vue"
import ModelSelect from "../components/firstRun/ModelSelect.vue"
import Ready from "../components/firstRun/Ready.vue"

// 初始化步骤数量
const STEPS_COUNT = 3

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
const closeWindow = () => {
	getCurrentWindow().close()
}

// 完成初始化:写标记, Rust 侧会切换窗口 (first-run → init)
const finish = async () => {
	try {
		await invoke("complete_first_run")
	} catch (error) {
		console.error("finish first run failed:", error)
	}
}
</script>

<template>
	<div class="first-run-window" :class="`bg-step-${currentStep + 1}`">
		<div class="titlebar" data-tauri-drag-region>
			<span class="title" data-tauri-drag-region>Nori</span>
			<div class="titlebar-right">
				<div class="steps-indicator">
					<span
						v-for="i in STEPS_COUNT"
						:key="i"
						class="seg"
						:class="{active: i <= currentStep + 1}"
					/>
				</div>
				<span class="step-count">{{ currentStep + 1 }} / {{ STEPS_COUNT }}</span>
				<button class="close-btn" title="关闭" @click="closeWindow">✕</button>
			</div>
		</div>

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
			<button v-if="!isFirst" class="btn btn-ghost" @click="prev">← 上一步</button>
			<span v-else/>
			<button v-if="!isLast" class="btn btn-primary" @click="next">下一步 →</button>
			<button v-else class="btn btn-primary" @click="finish">开始 ✨</button>
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
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	transition: background 0.6s ease;

	// 每页不同的背景: 渐变 + 位置/明度不同的光晕
	&.bg-step-1 {
		background-image: radial-gradient(56rem 34rem at 88% 36%, rgba(94, 234, 212, 0.16), transparent 65%),
		linear-gradient(160deg, #10304b 0%, var(--bg-deep) 58%, var(--bg-abyss) 100%);
	}

	&.bg-step-2 {
		background-image: radial-gradient(62rem 42rem at 50% 115%, rgba(127, 212, 232, 0.18), transparent 60%),
		linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}

	&.bg-step-3 {
		background-image: radial-gradient(42rem 34rem at 50% 52%, rgba(125, 227, 255, 0.14), transparent 68%),
		linear-gradient(160deg, #0c2440 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}
}

.titlebar {
	padding: 0 1.2rem 0 1.6rem;
	height: 4.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	flex-shrink: 0;

	.title {
		color: var(--text-primary);
		font-size: 1.3rem;
		font-weight: 600;
		letter-spacing: 0.05rem;
	}

	.titlebar-right {
		display: flex;
		align-items: center;
		gap: 1rem;

		.steps-indicator {
			display: flex;
			gap: 0.24rem;
		}

		.seg {
			width: 2.2rem;
			height: 0.3rem;
			border-radius: 0.02rem;
			background-color: rgba(255, 255, 255, 0.14);
			transition: all 0.3s ease;

			&.active {
				background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
				box-shadow: 0 0 0.6rem var(--glow-teal-soft);
			}
		}

		.step-count {
			font-size: 1.1rem;
			color: var(--text-faint);
			font-variant-numeric: tabular-nums;
			letter-spacing: 0.05rem;
		}

		.close-btn {
			width: 2.6rem;
			height: 2.6rem;
			border: none;
			border-radius: 50%;
			background-color: transparent;
			color: var(--text-muted);
			font-size: 1.2rem;
			cursor: pointer;
			display: flex;
			align-items: center;
			justify-content: center;

			&:hover {
				background-color: rgba(255, 255, 255, 0.08);
				color: var(--danger);
			}
		}
	}
}

// 舞台
.stage {
	flex: 1;
	width: 100%;
	height: 100%;
	min-height: 0;
}

// 页面过渡: 下一步向右滑入, 上一步向左滑入
.page-next-enter-active,
.page-next-leave-active,
.page-prev-enter-active,
.page-prev-leave-active {
	transition: opacity 0.32s ease, transform 0.32s cubic-bezier(0.4, 0, 0.2, 1);
}

.page-next-enter-from {
	opacity: 0;
	transform: translateX(3.6rem);
}

.page-next-leave-to {
	opacity: 0;
	transform: translateX(-3.6rem);
}

.page-prev-enter-from {
	opacity: 0;
	transform: translateX(-3.6rem);
}

.page-prev-leave-to {
	opacity: 0;
	transform: translateX(3.6rem);
}

// 底部导航
.footer {
	padding: 0 3.2rem;
	height: 6.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	flex-shrink: 0;

	.btn {
		padding: 0.9rem 2.2rem;
		border: none;
		border-radius: var(--radius-sm);
		font-size: 1.4rem;
		cursor: pointer;
		transition: all 0.2s ease;

		&:hover {
			transform: translateY(-0.1rem);
		}
	}

	.btn-primary {
		background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
		color: #05121a;
		font-weight: 600;

		&:hover {
			box-shadow: 0 0 1.6rem var(--glow-teal-soft);
		}
	}

	.btn-ghost {
		background-color: transparent;
		color: var(--text-muted);
		border: 0.1rem solid var(--line-subtle);

		&:hover {
			color: var(--text-primary);
			border-color: var(--line-strong);
		}
	}
}
</style>
