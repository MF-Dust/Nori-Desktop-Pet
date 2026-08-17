<script setup lang="ts">
import {invoke} from "@tauri-apps/api/core"
import {openUrl} from "@tauri-apps/plugin-opener"
import {writeText} from "@tauri-apps/plugin-clipboard-manager"
import useLanguage from "../../services/i18n"
import {EkIcon} from "../ui"
import type {IconMode, IconName} from "../../services/icon"
import logo from "../../assets/images/logo.png"

const I18N = useLanguage.useLang.components.firstRun.welcome

// 推广链接
interface Link {
	label: string
	sub: string
	url?: string
	qq?: string
	mode?: IconMode
	icon: IconName
}

// 推广链接
const links: Link[] = [
	{
		label: I18N.links.steam.label,
		sub: I18N.links.steam.sub,
		url: "https://store.steampowered.com/app/4996280/I_NORI/",
		mode: "fill",
		icon: "steam"
	},
	{
		label: I18N.links.noriOS.label,
		sub: I18N.links.noriOS.sub,
		url: "https://os.inori.ai/landing",
		mode: "stroke",
		icon: "noriOS"
	},
	{
		label: I18N.links.qq.label,
		sub: I18N.links.qq.sub,
		qq: "1041616195",
		mode: "fill",
		icon: "qq"
	},
	{
		label: I18N.links.bilibili.label,
		sub: I18N.links.bilibili.sub,
		url: "https://space.bilibili.com/326505494",
		mode: "fill",
		icon: "bilibili"
	}
]

// 点击链接卡片: 有 qq 属性则复制群号, 否则打开网页
const handleLink = async (link: Link) => {
	if (link.qq) {
		try {
			await writeText(link.qq)
			await invoke("write_log", {
				level: "info",
				message: `复制 QQ 群号 ${link.qq} 成功`
			})
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
</script>

<template>
	<section key="welcome" class="page page-welcome">
		<div class="hero-copy">
			<span class="badge">✨ Desktop Pet</span>
			<h1 class="hero-title glow-teal">{{I18N.title}}</h1>
			<p class="hero-desc">{{I18N.subtitle}}</p>
			<div class="links">
				<button v-for="link in links" :key="link.qq || link.url" class="link-card" @click="handleLink(link)">
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
	padding: 0.8rem 5.6rem 0.4rem;
	flex-direction: row;
	align-items: center;
	gap: 4rem;
}

.hero-copy {
	flex: 1 1 auto;
	min-width: 0;
	display: flex;
	flex-direction: column;
	align-items: flex-start;
	gap: 1.2rem;
}

.badge {
	padding: 0.4rem 1.2rem;
	display: inline-flex;
	align-items: center;
	border-radius: 99.9rem;
	background-color: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal);
	font-size: 1.1rem;
	letter-spacing: 0.04rem;
}

.hero-title {
	font-size: 3.0rem;
	font-weight: 700;
	line-height: 1.2;
	color: var(--text-primary);
}

.hero-desc {
	font-size: 1.3rem;
	line-height: 1.7;
	color: var(--text-body);
}

.links {
	margin-top: 0.2rem;
	width: 100%;
	display: flex;
	flex-direction: column;
	gap: 0.8rem;
}

.link-card {
	padding: 0.9rem 1.4rem;
	display: flex;
	align-items: center;
	gap: 1.2rem;
	border-radius: var(--radius-sm);
	background: rgba(125, 227, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	cursor: pointer;
	text-align: left;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.1);
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
		transform: translateX(0.3rem);
	}
}

.link-icon {
	width: 2.2rem;
	height: 2.2rem;
	flex-shrink: 0;
	color: var(--nori-teal);
}

.link-text {
	flex: 1;
	min-width: 0;
	display: flex;
	flex-direction: column;
	gap: 0.1rem;
}

.link-label {
	color: var(--text-primary);
	font-size: 1.3rem;
	font-weight: 500;
}

.link-sub {
	color: var(--text-faint);
	font-size: 1.1rem;
}

.link-arrow {
	color: var(--nori-teal);
	font-size: 1.3rem;
	flex-shrink: 0;
}

.hero-art {
	flex: 0 0 auto;
	display: grid;
	grid-template-areas: "art";
	width: 20rem;
	height: 24rem;
	place-items: center center;
}

.halo,
.hero-logo,
.hero-hint {
	grid-area: art;
}

.halo {
	align-self: center;
	justify-self: center;
	display: grid;
	grid-template-areas: "ring";
	place-items: center;
	width: 19rem;
	height: 19rem;
	border-radius: 50%;
	background-image: radial-gradient(circle, rgba(94, 234, 212, 0.22) 0%, rgba(94, 234, 212, 0.06) 45%, transparent 70%);
	animation: halo-spin 9s linear infinite;

	&::before {
		content: "";
		grid-area: ring;
		width: calc(100% - 2rem);
		height: calc(100% - 2rem);
		border-radius: 50%;
		border: 0.1rem dashed rgba(125, 227, 255, 0.35);
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
	align-self: center;
	justify-self: center;
	width: 10.4rem;
	height: 10.4rem;
	object-fit: contain;
	animation: breathe 2.6s ease-in-out infinite;
	filter: drop-shadow(0 0 1.8rem rgba(94, 234, 212, 0.45));
}

.hero-hint {
	align-self: end;
	justify-self: center;
	margin-bottom: 0.6rem;
	font-size: 1.2rem;
	letter-spacing: 0.4rem;
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
</style>