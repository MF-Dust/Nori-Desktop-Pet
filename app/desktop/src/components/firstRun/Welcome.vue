<script setup lang="ts">
import {computed, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../../components/Icon.vue"
import type {IconMode, IconName} from "../../services/icon"
import logo from "../../assets/images/logo.png"

const I18N = computed(() => useLanguages().components.firstRun.welcome)

// 推广链接
interface Link {
	key: string
	label: string
	sub: string
	url?: string
	qq?: string
	mode?: IconMode
	icon: IconName
}

// 复制状态提示
const copiedQq = ref(false)
let copyTimer: ReturnType<typeof setTimeout> | null = null

// 推广链接 (响应式: 随语言重算)
const links = computed<Link[]>(() => [
	{
		key: "steam",
		label: I18N.value.links.steam.label,
		sub: I18N.value.links.steam.sub,
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
		mode: "fill",
		icon: "steam"
	},
	{
		key: "noriOS",
		label: I18N.value.links.noriOS.label,
		sub: I18N.value.links.noriOS.sub,
		url: "https://os.inori.ai/landing",
		mode: "stroke",
		icon: "noriOS"
	},
	{
		key: "qq",
		label: copiedQq.value ? "已复制群号: 1041616195" : I18N.value.links.qq.label,
		sub: copiedQq.value ? "前往 QQ 粘贴即可加入" : I18N.value.links.qq.sub,
		qq: "1041616195",
		mode: "fill",
		icon: "qq"
	},
	{
		key: "bilibili",
		label: I18N.value.links.bilibili.label,
		sub: I18N.value.links.bilibili.sub,
		url: "https://space.bilibili.com/326505494",
		mode: "fill",
		icon: "bilibili"
	}
])

// 点击链接卡片: 有 qq 属性则复制群号, 否则打开网页
const handleLink = async (link: Link) => {
	if (link.qq) {
		try {
			await RUNTIME.copyText(link.qq)
			copiedQq.value = true
			if (copyTimer) clearTimeout(copyTimer)
			copyTimer = setTimeout(() => {
				copiedQq.value = false
			}, 2500)
			await RUNTIME.writeLog("info", `复制 QQ 群号 ${link.qq} 成功`)
		} catch (error) {
			await RUNTIME.writeLog("error", `复制 QQ 群号 ${link.qq} 失败`)
		}
	} else if (link.url) {
		await RUNTIME.openUrl(link.url)
	}
}
</script>

<template>
	<section key="welcome" class="page page-welcome">
		<div class="hero-copy">
			<div class="badge-row">
				<span class="badge">
					<Icon name="sparkles" :size="12"/>
					<span>Live2D Cyber Pet</span>
				</span>
				<span class="badge badge-version">v0.1.0</span>
			</div>

			<h1 class="hero-title glow-teal">{{ I18N.title }}</h1>
			<p class="hero-desc">{{ I18N.subtitle }}</p>

			<div class="features-pills">
				<span class="feature-tag">
					<Icon name="sparkles" :size="11"/>
					<span>灵动 Live2D 交互</span>
				</span>
				<span class="feature-tag">
					<Icon name="cpu" :size="11"/>
					<span>全协议 AI 大脑</span>
				</span>
				<span class="feature-tag">
					<Icon name="package" :size="11"/>
					<span>本地私密安全</span>
				</span>
			</div>

			<div class="links-grid">
				<button
					v-for="link in links"
					:key="link.key"
					class="link-card"
					:class="{copied: link.key === 'qq' && copiedQq}"
					@click="handleLink(link)"
				>
					<div class="link-icon-wrap">
						<Icon :name="link.icon" :mode="link.mode" class="link-icon"/>
					</div>
					<div class="link-text">
						<span class="link-label">{{ link.label }}</span>
						<span class="link-sub">{{ link.sub }}</span>
					</div>
					<span class="link-arrow">
						<Icon :name="link.key === 'qq' && copiedQq ? 'check' : 'arrow-right'" :size="13"/>
					</span>
				</button>
			</div>
		</div>

		<div class="hero-art">
			<div class="halo-outer"></div>
			<div class="halo-inner"></div>
			<div class="logo-glow-wrap">
				<img class="hero-logo" :src="logo" alt="Nori"/>
			</div>
			<span class="hero-hint">- N O R I -</span>
		</div>
	</section>
</template>

<style scoped lang="less">
.page {
	width: 100%;
	height: 100%;
	display: flex;
}

.page-welcome {
	padding: 1.2rem 4.8rem 1rem;
	flex-direction: row;
	align-items: center;
	gap: 3.6rem;
}

.hero-copy {
	flex: 1 1 auto;
	min-width: 0;
	display: flex;
	flex-direction: column;
	align-items: flex-start;
	gap: 1rem;
}

.badge-row {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.badge {
	padding: 0.35rem 1rem;
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	border-radius: var(--radius-pill);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal);
	font-size: 1.1rem;
	letter-spacing: 0.04rem;

	&.badge-version {
		background: rgba(255, 255, 255, 0.05);
		color: var(--text-muted);
		font-family: monospace;
	}
}

.hero-title {
	font-size: 2.8rem;
	font-weight: 700;
	line-height: 1.15;
	color: var(--text-primary);
	letter-spacing: -0.02rem;
}

.hero-desc {
	font-size: 1.3rem;
	line-height: 1.55;
	color: var(--text-body);
}

.features-pills {
	display: flex;
	gap: 0.6rem;
	margin-top: 0.2rem;
}

.feature-tag {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	padding: 0.25rem 0.8rem;
	background: rgba(125, 227, 255, 0.06);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	font-size: 1.05rem;
	color: var(--nori-teal-soft);
}

.links-grid {
	margin-top: 0.6rem;
	width: 100%;
	display: grid;
	grid-template-columns: 1fr 1fr;
	gap: 0.8rem;
}

.link-card {
	padding: 0.85rem 1.1rem;
	display: flex;
	align-items: center;
	gap: 0.9rem;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	color: var(--text-primary);
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 1.4rem var(--glow-teal-soft);
		transform: translateY(-0.15rem);

		.link-icon-wrap {
			background: rgba(125, 227, 255, 0.2);
			border-color: var(--nori-teal-bright);
		}

		.link-arrow {
			color: var(--nori-teal-bright);
			transform: translateX(0.2rem);
		}
	}

	&.copied {
		background: rgba(32, 224, 144, 0.12);
		border-color: rgba(32, 224, 144, 0.4);

		.link-icon-wrap {
			background: rgba(32, 224, 144, 0.2);
			color: #20e090;
		}

		.link-label {
			color: #20e090;
		}
	}
}

.link-icon-wrap {
	width: 3.2rem;
	height: 3.2rem;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.06);
	border: 0.1rem solid var(--line-subtle);
	display: flex;
	align-items: center;
	justify-content: center;
	flex-shrink: 0;
	color: var(--nori-teal);
	transition: all 0.2s ease;
}

.link-icon {
	width: 1.8rem;
	height: 1.8rem;
}

.link-text {
	flex: 1;
	min-width: 0;
	display: flex;
	flex-direction: column;
	gap: 0.15rem;
}

.link-label {
	color: var(--text-primary);
	font-size: 1.25rem;
	font-weight: 500;
	white-space: nowrap;
	overflow: hidden;
	text-overflow: ellipsis;
}

.link-sub {
	color: var(--text-faint);
	font-size: 1.05rem;
	white-space: nowrap;
	overflow: hidden;
	text-overflow: ellipsis;
}

.link-arrow {
	color: var(--text-muted);
	flex-shrink: 0;
	display: inline-flex;
	align-items: center;
	transition: all 0.2s ease;
}

// 右侧 Hero 艺术图
.hero-art {
	flex: 0 0 auto;
	display: grid;
	grid-template-areas: "art";
	width: 22rem;
	height: 25rem;
	place-items: center center;
	position: relative;
}

.halo-outer,
.halo-inner,
.logo-glow-wrap,
.hero-hint {
	grid-area: art;
}

.halo-outer {
	align-self: center;
	justify-self: center;
	width: 21rem;
	height: 21rem;
	border-radius: 50%;
	background-image: radial-gradient(circle, rgba(94, 234, 212, 0.18) 0%, rgba(94, 234, 212, 0.04) 50%, transparent 70%);
	border: 0.1rem dashed rgba(125, 227, 255, 0.25);
	animation: halo-spin 14s linear infinite;
}

.halo-inner {
	align-self: center;
	justify-self: center;
	width: 16rem;
	height: 16rem;
	border-radius: 50%;
	border: 0.1rem solid rgba(125, 227, 255, 0.15);
	background: radial-gradient(circle, rgba(125, 227, 255, 0.12) 0%, transparent 60%);
	animation: halo-spin-reverse 10s linear infinite;
}

@keyframes halo-spin {
	from {
		transform: rotate(0deg);
	}
	to {
		transform: rotate(360deg);
	}
}

@keyframes halo-spin-reverse {
	from {
		transform: rotate(360deg);
	}
	to {
		transform: rotate(0deg);
	}
}

.logo-glow-wrap {
	align-self: center;
	justify-self: center;
	display: flex;
	align-items: center;
	justify-content: center;
}

.hero-logo {
	width: 10.8rem;
	height: 10.8rem;
	object-fit: contain;
	animation: breathe 2.8s ease-in-out infinite;
}

.hero-hint {
	align-self: end;
	justify-self: center;
	margin-bottom: 0.4rem;
	font-size: 1.2rem;
	letter-spacing: 0.45rem;
	color: var(--nori-teal-soft);
	font-weight: 600;
	opacity: 0.85;
}
</style>