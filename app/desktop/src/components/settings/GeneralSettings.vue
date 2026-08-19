<script setup lang="ts">
import {onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import {i18n} from "../../services/i18n"
import Icon from "../Icon.vue"

const currentLang = ref("zh-CN")
const autoSummon = ref(true)
const appVersion = ref("0.1.0")

onMounted(async () => {
	try {
		const [SAVED_LANG, SAVED_SUMMON, SAVED_VER] = await Promise.all([
			invoke<string | null>("get_config", {key: "app_language"}),
			invoke<string | null>("get_config", {key: "pet_auto_summon"}),
			invoke<string | null>("get_config", {key: "app_version"}),
		])
		if (SAVED_LANG) currentLang.value = SAVED_LANG
		if (SAVED_SUMMON !== null) autoSummon.value = SAVED_SUMMON === "true" || SAVED_SUMMON === "1"
		if (SAVED_VER) appVersion.value = SAVED_VER
	} catch (error) {
		console.error("读取常规设置失败:", error)
	}
})

const onLanguageChange = (lang: string) => {
	currentLang.value = lang
	i18n.global.locale.value = lang as any
	void invoke("set_config", {key: "app_language", value: lang})
}

const saveConfig = (key: string, value: string) => {
	void invoke("set_config", {key, value})
}
</script>

<template>
	<div class="general-settings">
		<header class="section-header">
			<h2 class="title glow-teal">系统与常规设置</h2>
			<p class="subtitle">管理界面显示语言、启动偏好与客户端基础信息</p>
		</header>

		<div class="settings-content">
			<!-- 1. 界面语言 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="noriOS" :size="18" class="card-icon"/>
					<span class="card-title">显示语言 (Language)</span>
				</div>
				<div class="card-body">
					<div class="radio-group">
						<label class="radio-chip" :class="{active: currentLang === 'zh-CN'}">
							<input
								v-model="currentLang"
								type="radio"
								value="zh-CN"
								@change="onLanguageChange('zh-CN')"
							/>
							🇨🇳 简体中文 (Chinese)
						</label>
						<label class="radio-chip" :class="{active: currentLang === 'en-US'}">
							<input
								v-model="currentLang"
								type="radio"
								value="en-US"
								@change="onLanguageChange('en-US')"
							/>
							🇺🇸 English (US)
						</label>
					</div>
				</div>
			</div>

			<!-- 2. 启动与窗口行为 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="settings" :size="18" class="card-icon"/>
					<span class="card-title">启动与运行行为</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">启动时自动唤出桌宠</span>
							<p class="switch-desc">软件启动完成后自动在桌面上显示 Nori 桌宠窗口</p>
						</div>
						<label class="toggle-switch">
							<input
								v-model="autoSummon"
								type="checkbox"
								@change="saveConfig('pet_auto_summon', String(autoSummon))"
							/>
							<span class="toggle-slider"/>
						</label>
					</div>
				</div>
			</div>

			<!-- 3. 应用关于信息 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="info" :size="18" class="card-icon"/>
					<span class="card-title">应用与环境信息</span>
				</div>
				<div class="card-body">
					<div class="info-row">
						<span class="info-label">客户端版本</span>
						<span class="info-val">v{{ appVersion }}</span>
					</div>
					<div class="info-row">
						<span class="info-label">开源协议</span>
						<span class="info-val">GPL-3.0 License</span>
					</div>
					<div class="info-row">
						<span class="info-label">渲染引擎</span>
						<span class="info-val">Avalonia UI + Microsoft WebView2</span>
					</div>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.general-settings {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
	padding: 1.5rem 2rem;
	gap: 1.6rem;
}

.section-header {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.title {
	margin: 0;
	font-size: 1.8rem;
	font-weight: 700;
}

.subtitle {
	margin: 0;
	font-size: 1.2rem;
	color: var(--text-muted);
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.6rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.4rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.card-header {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	color: var(--nori-teal-bright);
}

.card-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-body {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.radio-group {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
}

.radio-chip {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: 2rem;
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-body);
	font-size: 1.15rem;
	cursor: pointer;
	transition: all 0.15s ease;

	input {
		display: none;
	}

	&.active {
		border-color: transparent;
		background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
		color: #05121a;
		font-weight: 600;
	}
}

.switch-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
}

.switch-title {
	font-size: 1.25rem;
	color: var(--text-primary);
	font-weight: 500;
}

.switch-desc {
	margin: 0.2rem 0 0;
	font-size: 1.1rem;
	color: var(--text-faint);
}

.toggle-switch {
	position: relative;
	width: 4rem;
	height: 2.2rem;
	cursor: pointer;

	input {
		opacity: 0;
		width: 0;
		height: 0;
	}

	.toggle-slider {
		position: absolute;
		top: 0;
		left: 0;
		right: 0;
		bottom: 0;
		background: rgba(255, 255, 255, 0.15);
		border-radius: 2rem;
		transition: 0.2s;

		&::before {
			position: absolute;
			content: "";
			height: 1.6rem;
			width: 1.6rem;
			left: 0.3rem;
			bottom: 0.3rem;
			background: white;
			border-radius: 50%;
			transition: 0.2s;
		}
	}

	input:checked + .toggle-slider {
		background: var(--nori-teal-bright);
	}

	input:checked + .toggle-slider::before {
		transform: translateX(1.8rem);
	}
}

.info-row {
	display: flex;
	justify-content: space-between;
	align-items: center;
	padding: 0.6rem 0;
	border-bottom: 0.1rem solid rgba(255, 255, 255, 0.05);

	&:last-child {
		border-bottom: none;
	}
}

.info-label {
	font-size: 1.2rem;
	color: var(--text-muted);
}

.info-val {
	font-size: 1.2rem;
	color: var(--text-primary);
	font-family: inherit;
}
</style>
