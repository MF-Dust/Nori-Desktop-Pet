<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import {emit as emitEvent} from "../../services/host/event"
import {openUrl, writeText} from "../../services/host/shell"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../Icon.vue"
import type {IconMode, IconName} from "../../services/icon"
import {MODEL_LIST} from "../../services/live2d/models"

const props = defineProps<{
	petVisible: boolean
}>()

const emit = defineEmits<{
	"toggle-pet": []
	navigate: [tab: "talk" | "model" | "settings" | "about"]
}>()

const I18N = computed(() => useLanguages().views.main.home)

// ---- 当前选中的模型 ----
const selectedModelId = ref("arg-nori")
const currentModel = computed(() =>
	MODEL_LIST.find((m) => m.id === selectedModelId.value) ?? MODEL_LIST[0]
)

// ---- AI 配置状态 ----
const aiConfigured = ref(false)
const aiProvider = ref("")
const aiModel = ref("")

// ---- 模型总数与已安装状态 ----
const installedCount = ref(1)

// ---- 快捷动作提示反馈 ----
const motionFeedback = ref(false)
let feedbackTimer: ReturnType<typeof setTimeout> | null = null

// 复制 QQ 提示
const qqCopied = ref(false)
let qqTimer: ReturnType<typeof setTimeout> | null = null

// 社区外链列表
interface CommunityLink {
	key: string
	label: string
	icon: IconName
	mode?: IconMode
	url?: string
	qq?: string
}

const communityLinks = computed<CommunityLink[]>(() => [
	{
		key: "steam",
		label: I18N.value.links.steam,
		icon: "steam",
		mode: "fill",
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
	},
	{
		key: "noriOS",
		label: I18N.value.links.noriOS,
		icon: "noriOS",
		mode: "stroke",
		url: "https://os.inori.ai/landing",
	},
	{
		key: "qq",
		label: qqCopied.value ? "已复制群号" : I18N.value.links.qq,
		icon: "qq",
		mode: "fill",
		qq: "1041616195",
	},
	{
		key: "bilibili",
		label: I18N.value.links.bilibili,
		icon: "bilibili",
		mode: "fill",
		url: "https://space.bilibili.com/326505494",
	},
])

// 读取基础状态
const loadDashboardState = async () => {
	try {
		const [SAVED_MODEL, PROVIDER, BASE, KEY, MODEL] = await Promise.all([
			invoke<string | null>("get_config", {key: "selected_model"}),
			invoke<string | null>("get_config", {key: "llm_provider"}),
			invoke<string | null>("get_config", {key: "llm_api_base"}),
			invoke<string | null>("get_config", {key: "llm_api_key"}),
			invoke<string | null>("get_config", {key: "llm_model"}),
		])

		if (typeof SAVED_MODEL === "string" && SAVED_MODEL.trim().length > 0) {
			selectedModelId.value = SAVED_MODEL.trim()
		}
		if (PROVIDER) aiProvider.value = PROVIDER
		if (MODEL) aiModel.value = MODEL
		aiConfigured.value = !!(BASE && KEY && MODEL)

		// 检测已安装模型数量
		let count = 0
		for (const m of MODEL_LIST) {
			const installed = await invoke<boolean>("check_resource", {
				resourceType: "live2d",
				name: m.id,
			}).catch(() => false)
			if (installed) count++
		}
		installedCount.value = Math.max(1, count)
	} catch (error) {
		console.error("加载主页仪表盘状态失败:", error)
	}
}

// 快速动作: 触发打招呼/随机动作
const triggerQuickMotion = async () => {
	try {
		// 广播随机动作或 Idle 动作
		await emitEvent("nori:play-motion", {group: "TapBody", no: 0})
		motionFeedback.value = true
		if (feedbackTimer) clearTimeout(feedbackTimer)
		feedbackTimer = setTimeout(() => {
			motionFeedback.value = false
		}, 1500)
	} catch (error) {
		console.error("触发动作失败:", error)
	}
}

// 处理社区外链点击
const handleCommunityClick = async (link: CommunityLink) => {
	if (link.qq) {
		try {
			await writeText(link.qq)
			qqCopied.value = true
			if (qqTimer) clearTimeout(qqTimer)
			qqTimer = setTimeout(() => {
				qqCopied.value = false
			}, 2000)
			await invoke("write_log", {
				level: "info",
				message: `已复制 QQ 交流群号: ${link.qq}`,
			})
		} catch (error) {
			console.error("复制 QQ 群号失败:", error)
		}
		return
	}
	if (link.url) {
		await openUrl(link.url)
	}
}

onMounted(() => {
	void loadDashboardState()
})
</script>

<template>
	<div class="home-panel">
		<!-- 顶部 Hero 卡片: 桌宠形象与状态控制 -->
		<section class="hero-card">
			<div class="hero-left">
				<div class="avatar-wrap" :class="{active: props.petVisible}">
					<img :src="currentModel.thumb" :alt="currentModel.name" class="pet-thumb"/>
					<span class="status-indicator" :class="{online: props.petVisible}"/>
				</div>
				<div class="hero-info">
					<div class="hero-header-row">
						<h2 class="pet-name glow-teal">{{ currentModel.name }}</h2>
						<span class="status-tag" :class="{online: props.petVisible}">
							<span class="status-dot"/>
							{{ props.petVisible ? I18N.petStatusOnline : I18N.petStatusOffline }}
						</span>
					</div>
					<p class="pet-desc">
						{{ props.petVisible ? I18N.petStatusDescOnline : I18N.petStatusDescOffline }}
					</p>
				</div>
			</div>

			<div class="hero-actions">
				<button
					class="btn-action"
					:class="props.petVisible ? 'btn-pet-hide' : 'btn-pet-summon'"
					@click="emit('toggle-pet')"
				>
					<Icon :name="props.petVisible ? 'close' : 'sparkles'" :size="16"/>
					<span>{{ props.petVisible ? I18N.hidePet : I18N.summonPet }}</span>
				</button>
				<button
					v-if="props.petVisible"
					class="btn-action btn-motion"
					:disabled="motionFeedback"
					@click="triggerQuickMotion"
				>
					<Icon name="sparkles" :size="16"/>
					<span>{{ motionFeedback ? I18N.quickMotionDone : I18N.quickMotion }}</span>
				</button>
			</div>
		</section>

		<!-- 中部磁贴导航网格 -->
		<section class="nav-grid">
			<!-- AI 对话卡片 -->
			<div class="grid-card chat-card" @click="emit('navigate', 'talk')">
				<div class="card-icon-wrap icon-chat">
					<Icon name="send" :size="22"/>
				</div>
				<div class="card-body">
					<h3 class="card-title">{{ I18N.cards.chat.title }}</h3>
					<p class="card-desc">{{ I18N.cards.chat.desc }}</p>
					<div class="card-status">
						<span class="status-pill" :class="{ok: aiConfigured}">
							{{ aiConfigured ? I18N.cards.chat.statusConfigured : I18N.cards.chat.statusNotConfigured }}
						</span>
					</div>
				</div>
				<button class="card-btn">
					<span>{{ I18N.cards.chat.action }}</span>
					<Icon name="arrow-right" :size="14"/>
				</button>
			</div>

			<!-- 模型换装卡片 -->
			<div class="grid-card model-card" @click="emit('navigate', 'model')">
				<div class="card-icon-wrap icon-model">
					<Icon name="package" :size="22"/>
				</div>
				<div class="card-body">
					<h3 class="card-title">{{ I18N.cards.model.title }}</h3>
					<p class="card-desc">{{ I18N.cards.model.desc }}</p>
					<div class="card-status">
						<span class="status-pill ok">
							{{ I18N.cards.model.current }}: {{ currentModel.name }}
						</span>
					</div>
				</div>
				<button class="card-btn">
					<span>{{ I18N.cards.model.action }}</span>
					<Icon name="arrow-right" :size="14"/>
				</button>
			</div>

			<!-- AI 大脑配置卡片 -->
			<div class="grid-card ai-card" @click="emit('navigate', 'settings')">
				<div class="card-icon-wrap icon-ai">
					<Icon name="cpu" :size="22"/>
				</div>
				<div class="card-body">
					<h3 class="card-title">{{ I18N.cards.ai.title }}</h3>
					<p class="card-desc">{{ I18N.cards.ai.desc }}</p>
					<div class="card-status">
						<span class="status-pill" :class="{ok: aiConfigured}">
							{{ aiProvider ? `${I18N.cards.ai.provider}: ${aiProvider}` : I18N.cards.chat.statusNotConfigured }}
						</span>
					</div>
				</div>
				<button class="card-btn">
					<span>{{ I18N.cards.ai.action }}</span>
					<Icon name="arrow-right" :size="14"/>
				</button>
			</div>
		</section>

		<!-- 底部生态社区与系统状态 -->
		<section class="footer-section">
			<div class="community-block">
				<h4 class="section-title">{{ I18N.links.title }}</h4>
				<div class="links-row">
					<button
						v-for="item in communityLinks"
						:key="item.key"
						class="community-chip"
						:class="{copied: item.key === 'qq' && qqCopied}"
						@click="handleCommunityClick(item)"
					>
						<Icon :name="item.icon" :mode="item.mode" :size="16"/>
						<span>{{ item.label }}</span>
					</button>
				</div>
			</div>

			<div class="system-block">
				<span class="sys-item">
					<span class="sys-label">{{ I18N.system.appVersion }}:</span>
					<span class="sys-value">v0.1.0</span>
				</span>
				<span class="sys-divider">/</span>
				<span class="sys-item">
					<span class="sys-label">{{ I18N.system.webview }}:</span>
					<span class="sys-value">Avalonia + WebView2</span>
				</span>
				<span class="sys-divider">/</span>
				<span class="sys-item status-ok">
					<span class="sys-dot"/>
					<span>{{ I18N.system.statusNormal }}</span>
				</span>
			</div>
		</section>
	</div>
</template>

<style scoped lang="less">
.home-panel {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	gap: 1.6rem;
	overflow-y: auto;
	padding: 0.4rem 0.6rem;
}

// ---- Hero 顶部卡片 ----
.hero-card {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1.8rem 2.4rem;
	background: linear-gradient(135deg, rgba(125, 227, 255, 0.08) 0%, rgba(5, 14, 26, 0.6) 100%);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-lg);
	box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.25);
	backdrop-filter: blur(1.2rem);
	position: relative;
	overflow: hidden;

	&::before {
		content: "";
		position: absolute;
		top: -50%;
		left: -20%;
		width: 40rem;
		height: 20rem;
		background: radial-gradient(circle, var(--glow-teal-soft) 0%, transparent 70%);
		opacity: 0.35;
		pointer-events: none;
	}
}

.hero-left {
	display: flex;
	align-items: center;
	gap: 1.8rem;
	z-index: 1;
}

.avatar-wrap {
	position: relative;
	width: 6.4rem;
	height: 6.4rem;
	border-radius: 50%;
	background: rgba(8, 22, 38, 0.8);
	border: 0.2rem solid var(--line-strong);
	display: flex;
	align-items: center;
	justify-content: center;
	overflow: hidden;
	transition: all 0.3s ease;

	&.active {
		border-color: var(--nori-teal-bright);
		box-shadow: 0 0 1.6rem var(--glow-teal-soft);
	}
}

.pet-thumb {
	width: 5.6rem;
	height: 5.6rem;
	object-fit: cover;
	border-radius: 50%;
}

.status-indicator {
	position: absolute;
	bottom: 0.2rem;
	right: 0.2rem;
	width: 1.2rem;
	height: 1.2rem;
	border-radius: 50%;
	background: #7a8c9e;
	border: 0.2rem solid #050e1a;

	&.online {
		background: #20e090;
		box-shadow: 0 0 0.8rem #20e090;
	}
}

.hero-info {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.hero-header-row {
	display: flex;
	align-items: center;
	gap: 1rem;
}

.pet-name {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--text-primary);
	letter-spacing: 0.05rem;
}

.status-tag {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.3rem 0.8rem;
	border-radius: 1.2rem;
	font-size: 1.1rem;
	background: rgba(255, 255, 255, 0.06);
	color: var(--text-muted);
	border: 0.1rem solid rgba(255, 255, 255, 0.08);

	.status-dot {
		width: 0.6rem;
		height: 0.6rem;
		border-radius: 50%;
		background: #7a8c9e;
	}

	&.online {
		background: rgba(32, 224, 144, 0.12);
		color: #20e090;
		border-color: rgba(32, 224, 144, 0.3);

		.status-dot {
			background: #20e090;
			box-shadow: 0 0 0.6rem #20e090;
		}
	}
}

.pet-desc {
	font-size: 1.2rem;
	color: var(--text-faint);
}

.hero-actions {
	display: flex;
	align-items: center;
	gap: 1rem;
	z-index: 1;
}

.btn-action {
	display: flex;
	align-items: center;
	gap: 0.7rem;
	padding: 0.9rem 1.6rem;
	border-radius: var(--radius-sm);
	font-size: 1.3rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	border: none;
	transition: all 0.2s ease;

	&.btn-pet-summon {
		background: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		color: #03101c;
		font-weight: 600;
		box-shadow: 0 0.4rem 1.6rem var(--glow-teal-soft);

		&:hover {
			box-shadow: 0 0.6rem 2.2rem var(--glow-teal-strong);
			transform: translateY(-0.15rem);
		}
	}

	&.btn-pet-hide {
		background: rgba(255, 255, 255, 0.08);
		color: var(--text-body);
		border: 0.1rem solid var(--line-subtle);

		&:hover {
			background: rgba(255, 80, 80, 0.15);
			border-color: rgba(255, 80, 80, 0.4);
			color: #ff6b6b;
		}
	}

	&.btn-motion {
		background: rgba(125, 227, 255, 0.1);
		color: var(--nori-teal-bright);
		border: 0.1rem solid var(--line-strong);

		&:hover:not(:disabled) {
			background: rgba(125, 227, 255, 0.18);
			transform: translateY(-0.15rem);
		}

		&:disabled {
			opacity: 0.6;
			cursor: not-allowed;
		}
	}
}

// ---- 导航网格 ----
.nav-grid {
	display: grid;
	grid-template-columns: repeat(3, 1fr);
	gap: 1.4rem;
}

.grid-card {
	display: flex;
	flex-direction: column;
	justify-content: space-between;
	padding: 1.6rem;
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	cursor: pointer;
	transition: all 0.25s ease;
	min-height: 16rem;

	&:hover {
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0.6rem 2rem rgba(0, 0, 0, 0.3);
		transform: translateY(-0.25rem);

		.card-btn {
			color: var(--nori-teal-bright);
			border-color: var(--nori-teal-soft);
		}
	}
}

.card-icon-wrap {
	width: 4rem;
	height: 4rem;
	border-radius: var(--radius-sm);
	display: flex;
	align-items: center;
	justify-content: center;
	margin-bottom: 1.2rem;

	&.icon-chat {
		background: rgba(125, 227, 255, 0.12);
		color: var(--nori-teal-bright);
	}

	&.icon-model {
		background: rgba(180, 140, 255, 0.12);
		color: #c49eff;
	}

	&.icon-ai {
		background: rgba(255, 180, 80, 0.12);
		color: #ffb86c;
	}
}

.card-body {
	flex: 1;
	display: flex;
	flex-direction: column;
	gap: 0.5rem;
}

.card-title {
	font-size: 1.5rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-desc {
	font-size: 1.15rem;
	color: var(--text-muted);
	line-height: 1.45;
}

.card-status {
	margin-top: 0.6rem;
}

.status-pill {
	display: inline-block;
	font-size: 1.05rem;
	padding: 0.2rem 0.6rem;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.05);
	color: var(--text-faint);

	&.ok {
		background: rgba(125, 227, 255, 0.1);
		color: var(--nori-teal-soft);
	}
}

.card-btn {
	margin-top: 1.2rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.6rem 0.8rem;
	background: transparent;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-muted);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;
}

// ---- 底部区域 ----
.footer-section {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	padding-top: 0.6rem;
	border-top: 0.1rem solid var(--line-subtle);
}

.section-title {
	font-size: 1.2rem;
	color: var(--text-faint);
	margin-bottom: 0.8rem;
	letter-spacing: 0.04rem;
}

.links-row {
	display: flex;
	flex-wrap: wrap;
	gap: 1rem;
}

.community-chip {
	display: flex;
	align-items: center;
	gap: 0.7rem;
	padding: 0.7rem 1.3rem;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	color: var(--text-body);
	font-size: 1.2rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--line-strong);
		color: var(--nori-teal-bright);
		transform: translateY(-0.1rem);
	}

	&.copied {
		background: rgba(32, 224, 144, 0.15);
		border-color: rgba(32, 224, 144, 0.4);
		color: #20e090;
	}
}

.system-block {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	font-size: 1.1rem;
	color: var(--text-faint);
	padding-top: 0.4rem;
}

.sys-label {
	color: var(--text-faint);
}

.sys-value {
	color: var(--text-muted);
	margin-left: 0.3rem;
}

.sys-divider {
	opacity: 0.3;
}

.status-ok {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	color: #20e090;

	.sys-dot {
		width: 0.5rem;
		height: 0.5rem;
		border-radius: 50%;
		background: #20e090;
	}
}
</style>
