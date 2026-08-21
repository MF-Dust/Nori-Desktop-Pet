<script setup lang="ts">
import {ref, watch, onMounted, computed} from "vue"
import {invoke} from "../../services/host/invoke"
import useLanguages from "../../services/i18n/useLanguages.ts"
import Icon from "../../components/Icon.vue"
import {MODEL_LIST} from "../../services/live2d/models"

const I18N = computed(() => useLanguages().components.firstRun.modelSelect)

// 可选模型列表
const models = MODEL_LIST

// 配置键名
const CONFIG_KEY = "selected_model"

// 选中的模型 id
const selected = ref("arg-nori")

// 各模型个性化副标题/标签
const MODEL_TAGS: Record<string, {tag: string; desc: string}> = {
	"arg-nori": {tag: "推荐 · 特工常服", desc: "赛博机能风，带有高精度物理摆动与多套表情"},
	"nori": {tag: "经典 · 初始造型", desc: "纯净经典的元气造型，轻快灵动"},
}

// 组件挂载时读取已保存的配置
onMounted(async () => {
	try {
		const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
		if (SAVED && models.some(m => m.id === SAVED)) {
			selected.value = SAVED
		}
	} catch (error) {
		console.error("读取模型配置失败:", error)
	}
})

// 监听选中的模型 id 变化, 写入配置和日志
watch(selected, async (newVal) => {
	try {
		await invoke("set_config", {key: CONFIG_KEY, value: newVal})
		await invoke("write_log", {level: "info", message: `切换模型: ${newVal}`})
	} catch (error) {
		console.error("保存模型配置失败:", error)
	}
})
</script>

<template>
	<section key="model-select" class="page page-model">
		<div class="model-head">
			<span class="model-badge">
				<Icon name="package" :size="12"/>
				<span>Character Selection</span>
			</span>
			<h2 class="model-title glow-teal">{{ I18N.title }}</h2>
			<p class="model-sub">{{ I18N.sub }}（在后续主面板中可随时自由更换造型与导入新模型）</p>
		</div>

		<div class="model-grid">
			<button
				v-for="model in models"
				:key="model.id"
				class="model-card"
				:class="{active: selected === model.id}"
				@click="selected = model.id"
			>
				<div class="model-thumb-wrap">
					<img class="model-thumb" :src="model.thumb" :alt="model.name"/>
					<div class="thumb-glow-overlay"></div>
					<span class="model-check">
						<Icon name="check" :size="12"/>
					</span>
				</div>

				<div class="model-info">
					<span class="model-name">{{ model.name }}</span>
					<span class="model-tag-badge">{{ MODEL_TAGS[model.id]?.tag || "Live2D 造型" }}</span>
					<p class="model-tag-desc">{{ MODEL_TAGS[model.id]?.desc || "已就绪的桌宠模型" }}</p>
				</div>
			</button>
		</div>
	</section>
</template>

<style scoped lang="less">
.page {
	width: 100%;
	height: 100%;
	padding: 1.2rem 4.8rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.8rem;
	text-align: center;
}

.model-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.5rem;
}

.model-badge {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.3rem 0.9rem;
	border-radius: var(--radius-pill);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal);
	font-size: 1.1rem;
}

.model-title {
	font-size: 2.4rem;
	font-weight: 700;
	color: var(--text-primary);
}

.model-sub {
	font-size: 1.25rem;
	color: var(--text-faint);
}

.model-grid {
	display: flex;
	flex-direction: row;
	gap: 2rem;
	justify-content: center;
}

.model-card {
	padding: 1rem 1rem 1.2rem;
	width: 19rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	border: 0.15rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.03);
	cursor: pointer;
	font-family: inherit;
	transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
	position: relative;
	overflow: hidden;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		transform: translateY(-0.3rem);
		box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.35), 0 0 1.4rem var(--glow-teal-soft);

		.model-thumb {
			transform: scale(1.03);
		}
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.12);
		box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.4), 0 0 2rem var(--glow-teal);

		.model-check {
			opacity: 1;
			transform: scale(1);
		}

		.model-name {
			color: var(--nori-teal-bright);
			font-weight: 600;
		}

		.model-tag-badge {
			background: rgba(94, 234, 212, 0.2);
			border-color: var(--nori-teal);
			color: var(--nori-teal-bright);
		}
	}
}

.model-thumb-wrap {
	position: relative;
	width: 100%;
	height: 18.5rem;
	overflow: hidden;
	border-radius: var(--radius-sm);
	background: rgba(0, 0, 0, 0.3);
	border: 0.1rem solid var(--line-subtle);
	display: flex;
	align-items: center;
	justify-content: center;
}

.model-thumb {
	width: 100%;
	height: 100%;
	object-fit: cover;
	object-position: top center;
	transition: transform 0.3s ease;
}

.thumb-glow-overlay {
	position: absolute;
	inset: 0;
	background: linear-gradient(180deg, transparent 60%, rgba(5, 14, 26, 0.8) 100%);
	pointer-events: none;
}

.model-check {
	position: absolute;
	top: 0.8rem;
	right: 0.8rem;
	width: 2.2rem;
	height: 2.2rem;
	border-radius: 50%;
	background: var(--nori-teal);
	color: #05121a;
	display: flex;
	align-items: center;
	justify-content: center;
	opacity: 0;
	transform: scale(0.6);
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	box-shadow: 0 0.2rem 0.8rem rgba(0, 0, 0, 0.4);
}

.model-info {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.35rem;
	width: 100%;
}

.model-name {
	font-size: 1.4rem;
	font-weight: 500;
	color: var(--text-primary);
}

.model-tag-badge {
	font-size: 1.05rem;
	padding: 0.15rem 0.7rem;
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.06);
	border: 0.1rem solid var(--line-subtle);
	color: var(--text-faint);
	transition: all 0.2s ease;
}

.model-tag-desc {
	font-size: 1.05rem;
	color: var(--text-faint);
	line-height: 1.35;
	display: -webkit-box;
	-webkit-line-clamp: 2;
	-webkit-box-orient: vertical;
	overflow: hidden;
}
</style>

