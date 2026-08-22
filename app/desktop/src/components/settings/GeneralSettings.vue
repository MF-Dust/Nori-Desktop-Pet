<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import useLanguage from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"

const currentLang = ref("zh-CN")
const autoSummon = ref(true)
const appVersion = ref("0.1.0")
const TEXT = computed(() => useLanguages().views.main.general)

let synced = false
onMounted(async () => {
	await RUNTIME.init()
	const SNAPSHOT = RUNTIME.snapshot.value
	if (!SNAPSHOT || synced) return
	synced = true
	currentLang.value = SNAPSHOT.general.language
	autoSummon.value = SNAPSHOT.general.petAutoSummon
	appVersion.value = SNAPSHOT.app.appVersion
})

// 切换语言: 本地立即生效, 持久化交给后端 (其他窗口经广播刷新)
const onLanguageChange = (lang: string) => {
	currentLang.value = lang
	void useLanguage.setLanguage(lang)
	void RUNTIME.updateGeneral({language: lang})
}

const onAutoSummonChange = (val: boolean) => {
	autoSummon.value = val
	void RUNTIME.updateGeneral({petAutoSummon: val})
}
</script>

<template>
	<div class="general-settings">
		<header class="section-header">
			<h2 class="title glow-teal">{{ TEXT.title }}</h2>
			<p class="subtitle">{{ TEXT.subtitle }}</p>
		</header>

		<div class="settings-content">
			<!-- 1. 界面语言 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="noriOS" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.language.title }}</span>
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
							🇨🇳 {{ TEXT.language.chinese }}
						</label>
						<label class="radio-chip" :class="{active: currentLang === 'en-US'}">
							<input
								v-model="currentLang"
								type="radio"
								value="en-US"
								@change="onLanguageChange('en-US')"
							/>
							🇺🇸 {{ TEXT.language.english }}
						</label>
					</div>
				</div>
			</div>

			<!-- 2. 启动与窗口行为 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="settings" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.startup.title }}</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">{{ TEXT.startup.autoSummon }}</span>
							<p class="switch-desc">{{ TEXT.startup.autoSummonDesc }}</p>
						</div>
						<n-switch
							:value="autoSummon"
							@update:value="(val: boolean) => onAutoSummonChange(val)"
						/>
					</div>
				</div>
			</div>

			<!-- 3. 应用关于信息 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="info" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.about.title }}</span>
				</div>
				<div class="card-body">
					<div class="info-row">
						<span class="info-label">{{ TEXT.about.version }}</span>
						<span class="info-val">v{{ appVersion }}</span>
					</div>
					<div class="info-row">
						<span class="info-label">{{ TEXT.about.license }}</span>
						<span class="info-val">GPL-3.0 License</span>
					</div>
					<div class="info-row">
						<span class="info-label">{{ TEXT.about.renderer }}</span>
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
	padding: 1.6rem 2.4rem;
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
	color: var(--text-primary);
}

.subtitle {
	margin: 0;
	font-size: 1.2rem;
	color: var(--text-faint);
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	transition: all 0.2s ease;

	&:hover {
		border-color: var(--line-strong);
	}
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
	padding: 0.7rem 1.4rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-body);
	font-size: 1.2rem;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	input {
		display: none;
	}

	&:hover {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: transparent;
		background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		color: #03101c;
		font-weight: 600;
		box-shadow: 0 0.2rem 1.2rem var(--glow-teal-soft);
	}
}

.switch-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
}

.switch-title {
	font-size: 1.3rem;
	color: var(--text-primary);
	font-weight: 500;
}

.switch-desc {
	margin: 0.2rem 0 0;
	font-size: 1.15rem;
	color: var(--text-faint);
}

.info-row {
	display: flex;
	justify-content: space-between;
	align-items: center;
	padding: 0.8rem 0;
	border-bottom: 0.1rem solid var(--line-subtle);

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
	font-family: monospace;
}
</style>
