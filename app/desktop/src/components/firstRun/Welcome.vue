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
	position: absolute;
	inset: 0;
	display: flex;
}

.page-welcome {
	padding: 8px 56px 4px;
	flex-direction: row;
	align-items: center;
	gap: 40px;
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
	padding: 4px 12px;
	display: inline-flex;
	align-items: center;
	border-radius: 999px;
	background-color: rgba(125, 227, 255, 0.08);
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
	margin-top: 2px;
	width: 100%;
	display: flex;
	flex-direction: column;
	gap: 8px;
}

.link-card {
	padding: 9px 14px;
	display: flex;
	align-items: center;
	gap: 12px;
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
	background-image: radial-gradient(circle, rgba(94, 234, 212, 0.22) 0%, rgba(94, 234, 212, 0.06) 45%, transparent 70%);
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
</style>