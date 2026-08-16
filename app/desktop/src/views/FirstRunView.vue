<script setup lang="ts">
import {computed, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWindow} from "@tauri-apps/api/window"
import {openUrl} from "@tauri-apps/plugin-opener"

// const STEPS_COUNT = 3

// const currentStep = ref(1)

// 引导内容: 每个步骤的标题, 描述, 推广链接(如果有)
interface Step {
	title: string
	desc: string
	links?: {label: string; url: string; emoji: string}[]
}

const steps: Step[] = [
	{
		title: "欢迎来到 Nori",
		desc: "请输入文本",
		links: [
			{label: "求求了, 为了 Nori 加个愿望单吧", url: "https://store.steampowered.com/app/4996280/I_NORI/", emoji: "🎮"},
			{label: "先导", url: "https://os.inori.ai/landing", emoji: "🌐"},
			{label: "加入 QQ 交流群", url: "https://qm.qq.com/q/1041616195", emoji: "💬"}
		]
	},
	{
		title: "它会做什么",
		desc: "初始化完成后,Nori 会常驻桌面,后续将支持聊天对话、语音交互、Live2D 动画等能力。"
	},
	{
		title: "准备好了吗",
		desc: "点击「开始」完成初始化,Nori 即将与你见面。"
	}
]

const current = ref(0)
const isFirst = computed(() => current.value === 0)
const isLast = computed(() => current.value === steps.length - 1)

const next = () => {
	if (!isLast.value) current.value++
}

const prev = () => {
	if (!isFirst.value) current.value--
}

// 打开外部链接(交给系统浏览器,WebView 内 window.open 会被拦截)
const openLink = (url: string) => {
	openUrl(url)
}

// 关闭窗口
const closeWindow = () => {
	getCurrentWindow().close()
}

// 完成初始化:写标记,Rust 侧会切换窗口(first-run → init)
const finish = async () => {
	try {
		await invoke("complete_first_run")
	} catch (e) {
		console.error("finish first run failed:", e)
	}
}
</script>

<template>
	<div class="first-run-window">
		<!-- 顶部标题栏(可拖拽) -->
		<div class="titlebar" data-tauri-drag-region>
			<span class="title" data-tauri-drag-region>Nori</span>
			<button class="close-btn" title="关闭" @click="closeWindow">✕</button>
		</div>

		<!-- 内容区 -->
		<div class="body">
			<div class="steps-indicator">
				<div
					v-for="(_, i) in steps"
					:key="i"
					class="dot"
					:class="{active: i === current, done: i < current}"
				/>
			</div>

			<div class="step" :key="current">
				<h2 class="step-title glow-teal">{{ steps[current].title }}</h2>
				<p class="step-desc">{{ steps[current].desc }}</p>

				<div v-if="steps[current].links" class="links">
					<button
						v-for="link in steps[current].links"
						:key="link.url"
						class="link-card"
						@click="openLink(link.url)"
					>
						<span class="link-emoji">{{ link.emoji }}</span>
						<span class="link-label">{{ link.label }}</span>
						<span class="link-arrow">→</span>
					</button>
				</div>
			</div>
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
	width: 100vw;
	height: 100vh;
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	border-radius: var(--radius-lg);
	display: flex;
	flex-direction: column;
	overflow: hidden;
	user-select: none;
	color: var(--text-body);
}

.titlebar {
	height: 44px;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 12px 0 16px;
	flex-shrink: 0;
}

.title {
	color: var(--text-primary);
	font-size: 13px;
	font-weight: 600;
	letter-spacing: 0.5px;
}

.close-btn {
	width: 26px;
	height: 26px;
	border: none;
	border-radius: 50%;
	background: transparent;
	color: var(--text-muted);
	font-size: 12px;
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;

	&:hover {
		background: rgba(255, 255, 255, 0.08);
		color: var(--danger);
	}
}

.body {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 24px;
	padding: 0 48px 10px;
}

.steps-indicator {
	display: flex;
	gap: 8px;
}

.dot {
	width: 8px;
	height: 8px;
	border-radius: 50%;
	background: rgba(255, 255, 255, 0.12);
	transition: all 0.3s ease;

	&.active {
		background: var(--nori-teal);
		box-shadow: 0 0 8px var(--glow-teal, rgba(125, 227, 255, 0.45));
		transform: scale(1.15);
	}

	&.done {
		background: var(--nori-teal-soft);
	}
}

.step {
	display: flex;
	flex-direction: column;
	align-items: center;
	text-align: center;
	gap: 14px;
	max-width: 420px;
}

.step-title {
	color: var(--text-primary);
	font-size: 22px;
	font-weight: 600;
}

.step-desc {
	font-size: 14px;
	line-height: 1.8;
	color: var(--text-body);
}

.links {
	display: flex;
	flex-direction: column;
	gap: 10px;
	width: 100%;
	margin-top: 8px;
}

.link-card {
	display: flex;
	align-items: center;
	gap: 12px;
	padding: 12px 16px;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.05);
	border: 1px solid var(--line-subtle);
	text-decoration: none;
	color: var(--text-primary);
	font-size: 14px;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.1);
		border-color: var(--nori-teal-soft);
		transform: translateY(-1px);
	}
}

.link-emoji {
	font-size: 20px;
}

.link-label {
	flex: 1;
	text-align: left;
	font-size: 14px;
}

.link-arrow {
	color: var(--nori-teal);
}

.footer {
	height: 64px;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 32px;
	flex-shrink: 0;
}

.btn {
	border: none;
	border-radius: var(--radius-sm);
	padding: 9px 22px;
	font-size: 14px;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		transform: translateY(-1px);
	}
}

.btn-primary {
	background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	font-weight: 600;

	&:hover {
		box-shadow: 0 0 16px var(--glow-teal-soft, rgba(125, 227, 255, 0.25));
	}
}

.btn-ghost {
	background: transparent;
	color: var(--text-muted);
	border: 1px solid var(--line-subtle);

	&:hover {
		color: var(--text-primary);
		border-color: var(--line-strong);
	}
}
</style>
