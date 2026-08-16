<script setup lang="ts">
import {computed, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {getCurrentWindow} from "@tauri-apps/api/window"
import {openUrl} from "@tauri-apps/plugin-opener"
import {writeText} from "@tauri-apps/plugin-clipboard-manager"
import logo from "../assets/images/logo.png"
import {EkIcon} from "../components/ui"
import type {IconMode, IconName} from "../services/icon"

// 初始化步骤数量
const STEPS_COUNT = 3

// 当前步骤索引
const currentStep = ref(0)

// 切换方向: 1 = 下一步, -1 = 上一步(决定过渡动画方向)
const direction = ref(1)

// 推广链接
interface Link {
	label: string
	sub: string
	url?: string
	qq?: string
	mode?: IconMode
	icon: IconName
}

const links: Link[] = [
	{
		label: "Steam 愿望单",
		sub: "在商店页支持我们",
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
		mode: "fill",
		icon: "steam"
	},
	{
		label: "Nori 先导页",
		sub: "抢先了解 Nori 的世界",
		url: "https://os.inori.ai/landing",
		mode: "stroke",
		icon: "noriOS"
	},
	{
		label: "QQ 交流群",
		sub: "点击复制群号：1041616195",
		qq: "1041616195",
		mode: "fill",
		icon: "qq"
	},
	{
		label: "Bilibili",
		sub: "关注官方账号",
		url: "https://space.bilibili.com/326505494",
		mode: "fill",
		icon: "bilibili"
	}
]

// 第 2 页: 功能特性
const features = [
	{emoji: "💬", title: "聊天对话", desc: "随时和 Nori 聊两句, 它会记得你。"},
	{emoji: "🎤", title: "语音交互", desc: "动动嘴就能吩咐 Nori, 解放双手。"},
	{emoji: "🎭", title: "Live2D 动画", desc: "栩栩如生的角色, 常伴桌面左右。"}
]

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

// 点击链接卡片: 有 qq 属性则复制群号，否则打开网页
const handleLink = async (link: Link) => {
	if (link.qq) {
		try {
			await writeText(link.qq)
		} catch (error) {
			await invoke("write_log", {
				level: "error",
				message: `复制 QQ 群号 ${link.qq} 失败`
			})
		}
	} else if (link.url) {
		await openUrl(link.url)
	}
}

// 关闭窗口
const closeWindow = () => {
	getCurrentWindow().close()
}

// 完成初始化:写标记,Rust 侧会切换窗口(first-run → init)
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
				<!-- 第 1 页: 欢迎 —— Hero 左右分栏 -->
				<section v-if="currentStep === 0" key="welcome" class="page page-welcome">
					<div class="hero-copy">
						<span class="badge">✨ Desktop Pet</span>
						<h1 class="hero-title glow-teal">欢迎来到 Nori</h1>
						<p class="hero-desc">一只会陪你上班、学习、摸鱼的桌面伙伴。先认识一下它吧。</p>
						<div class="links">
							<button
								v-for="link in links"
								:key="link.qq || link.url"
								class="link-card"
								@click="handleLink(link)"
							>
								<ek-icon :name="link.icon" :mode="link.mode" class="link-icon"/>
								<span class="link-text">
									<span class="link-label">{{ link.label }}</span>
									<span class="link-sub">{{ link.sub }}</span>
								</span>
								<span class="link-arrow">→</span>
							</button>
						</div>
					</div>
					<div class="hero-art">
						<div class="halo"></div>
						<img class="hero-logo" :src="logo" alt="Nori"/>
						<span class="hero-hint">N O R I</span>
					</div>
				</section>

				<!-- 第 2 页: 它能做什么 —— 特性卡片网格 -->
				<section v-else-if="currentStep === 1" key="features" class="page page-features">
					<span class="badge">核心能力</span>
					<h2 class="section-title glow-teal">它会做什么</h2>
					<p class="section-desc">初始化完成后, Nori 会常驻桌面, 一步步解锁更多能力。</p>
					<div class="feature-grid">
						<div v-for="f in features" :key="f.title" class="feature-card">
							<span class="feature-emoji">{{ f.emoji }}</span>
							<h3 class="feature-title">{{ f.title }}</h3>
							<p class="feature-desc">{{ f.desc }}</p>
						</div>
					</div>
				</section>

				<!-- 第 3 页: 准备好了吗 —— 极简居中确认 -->
				<section v-else key="ready" class="page page-ready">
					<span class="ready-star">✨</span>
					<h2 class="ready-title glow-teal">准备好了吗</h2>
					<p class="ready-desc">点击「开始」完成初始化, Nori 即将与你见面。</p>
					<span class="ready-tip">🐾 初始化大约只需几秒钟</span>
				</section>
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
	width: 100vw;
	height: 100vh;
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
		background: radial-gradient(560px 340px at 88% 36%, rgba(94, 234, 212, 0.16), transparent 65%),
		linear-gradient(160deg, #10304b 0%, var(--bg-deep) 58%, var(--bg-abyss) 100%);
	}

	&.bg-step-2 {
		background: radial-gradient(620px 420px at 50% 115%, rgba(127, 212, 232, 0.18), transparent 60%),
		linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}

	&.bg-step-3 {
		background: radial-gradient(420px 340px at 50% 52%, rgba(125, 227, 255, 0.14), transparent 68%),
		linear-gradient(160deg, #0c2440 0%, var(--bg-deep) 55%, var(--bg-abyss) 100%);
	}
}

// ---------- 标题栏 ----------
.titlebar {
	height: 44px;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 12px 0 16px;
	flex-shrink: 0;
}

.titlebar-right {
	display: flex;
	align-items: center;
	gap: 10px;
}

.title {
	color: var(--text-primary);
	font-size: 13px;
	font-weight: 600;
	letter-spacing: 0.5px;
}

.steps-indicator {
	display: flex;
	gap: 4px;
}

.seg {
	width: 22px;
	height: 3px;
	border-radius: 2px;
	background: rgba(255, 255, 255, 0.14);
	transition: all 0.3s ease;

	&.active {
		background: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
		box-shadow: 0 0 6px var(--glow-teal-soft);
	}
}

.step-count {
	font-size: 11px;
	color: var(--text-faint);
	font-variant-numeric: tabular-nums;
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

// ---------- 舞台(每页切换动画容器) ----------
.stage {
	flex: 1;
	position: relative;
	min-height: 0;
}

.page {
	position: absolute;
	inset: 0;
	display: flex;
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
	transform: translateX(36px);
}

.page-next-leave-to {
	opacity: 0;
	transform: translateX(-36px);
}

.page-prev-enter-from {
	opacity: 0;
	transform: translateX(-36px);
}

.page-prev-leave-to {
	opacity: 0;
	transform: translateX(36px);
}

// ---------- 第 1 页: 欢迎(Hero 左右分栏) ----------
.page-welcome {
	flex-direction: row;
	align-items: center;
	gap: 40px;
	padding: 8px 56px 4px;
}

.hero-copy {
	flex: 1 1 auto;
	min-width: 0;
	display: flex;
	flex-direction: column;
	align-items: flex-start;
	gap: 12px;
}

.badge {
	display: inline-flex;
	align-items: center;
	padding: 4px 12px;
	border-radius: 999px;
	background: rgba(125, 227, 255, 0.08);
	border: 1px solid var(--line-subtle);
	color: var(--nori-teal);
	font-size: 11px;
	letter-spacing: 0.4px;
}

.hero-title {
	font-size: 30px;
	font-weight: 700;
	line-height: 1.2;
	color: var(--text-primary);
}

.hero-desc {
	font-size: 13px;
	line-height: 1.7;
	color: var(--text-body);
}

.links {
	display: flex;
	flex-direction: column;
	gap: 8px;
	width: 100%;
	margin-top: 2px;
}

.link-card {
	display: flex;
	align-items: center;
	gap: 12px;
	padding: 9px 14px;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.04);
	border: 1px solid var(--line-subtle);
	color: var(--text-primary);
	font-size: 13px;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.1);
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 12px var(--glow-teal-soft);
		transform: translateX(3px);
	}
}

// 图标统一主题色
.link-icon {
	width: 22px;
	height: 22px;
	flex-shrink: 0;
	color: var(--nori-teal);
}

.link-text {
	flex: 1;
	min-width: 0;
	display: flex;
	flex-direction: column;
	gap: 1px;
}

.link-label {
	color: var(--text-primary);
	font-size: 13px;
	font-weight: 500;
}

.link-sub {
	color: var(--text-faint);
	font-size: 11px;
}

.link-arrow {
	color: var(--nori-teal);
	font-size: 13px;
	flex-shrink: 0;
}

.hero-art {
	flex: 0 0 auto;
	position: relative;
	width: 200px;
	height: 240px;
	display: flex;
	align-items: center;
	justify-content: center;
}

.halo {
	position: absolute;
	width: 190px;
	height: 190px;
	border-radius: 50%;
	background: radial-gradient(circle, rgba(94, 234, 212, 0.22) 0%, rgba(94, 234, 212, 0.06) 45%, transparent 70%);
	animation: halo-spin 9s linear infinite;

	&::before {
		content: "";
		position: absolute;
		inset: 10px;
		border-radius: 50%;
		border: 1px dashed rgba(125, 227, 255, 0.35);
	}
}

@keyframes halo-spin {
	from {
		transform: rotate(0deg);
	}
	to {
		transform: rotate(360deg);
	}
}

.hero-logo {
	position: relative;
	width: 104px;
	height: 104px;
	object-fit: contain;
	animation: breathe 2.6s ease-in-out infinite;
	filter: drop-shadow(0 0 18px rgba(94, 234, 212, 0.45));
}

.hero-hint {
	position: absolute;
	bottom: 6px;
	font-size: 12px;
	letter-spacing: 4px;
	color: var(--text-faint);
}

@keyframes breathe {
	0%, 100% {
		transform: scale(1);
	}
	50% {
		transform: scale(1.08);
	}
}

// ---------- 第 2 页: 它会做什么(特性卡片网格) ----------
.page-features {
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 12px;
	padding: 6px 48px 8px;
	text-align: center;
}

.section-title {
	font-size: 24px;
	font-weight: 700;
	color: var(--text-primary);
}

.section-desc {
	font-size: 13px;
	line-height: 1.7;
	color: var(--text-body);
}

.feature-grid {
	display: grid;
	grid-template-columns: repeat(3, 1fr);
	gap: 14px;
	width: 100%;
	margin-top: 6px;
}

.feature-card {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 8px;
	padding: 18px 14px 16px;
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.05);
	border: 1px solid var(--line-subtle);
	transition: all 0.25s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.09);
		border-color: var(--nori-teal-soft);
		transform: translateY(-3px);
		box-shadow: var(--shadow-soft);
	}
}

.feature-emoji {
	font-size: 30px;
	filter: drop-shadow(0 0 10px rgba(94, 234, 212, 0.35));
}

.feature-title {
	font-size: 15px;
	font-weight: 600;
	color: var(--text-primary);
}

.feature-desc {
	font-size: 12px;
	line-height: 1.6;
	color: var(--text-muted);
}

// ---------- 第 3 页: 准备好了吗(极简居中确认) ----------
.page-ready {
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 16px;
	padding-bottom: 6px;
}

.ready-star {
	font-size: 56px;
	animation: star-pop 2.4s ease-in-out infinite;
	filter: drop-shadow(0 0 22px rgba(125, 227, 255, 0.6));
}

@keyframes star-pop {
	0%, 100% {
		transform: scale(1) rotate(0deg);
	}
	50% {
		transform: scale(1.12) rotate(8deg);
	}
}

.ready-title {
	font-size: 28px;
	font-weight: 700;
	color: var(--text-primary);
}

.ready-desc {
	font-size: 14px;
	line-height: 1.8;
	color: var(--text-body);
	text-align: center;
}

.ready-tip {
	font-size: 12px;
	color: var(--text-faint);
	padding: 6px 14px;
	border-radius: 999px;
	background: rgba(255, 255, 255, 0.05);
	border: 1px solid var(--line-subtle);
}

// ---------- 底部导航 ----------
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
		box-shadow: 0 0 16px var(--glow-teal-soft);
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
