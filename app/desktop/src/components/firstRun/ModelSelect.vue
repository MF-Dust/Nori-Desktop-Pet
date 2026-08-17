<script setup lang="ts">
import {ref, watch, onMounted} from "vue"
import {invoke} from "@tauri-apps/api/core"
import nori from "../../assets/images/live2D/Nori.webp"
import arNori from "../../assets/images/live2D/ARGNori.webp"

// 可选模型列表
interface Model {
	id: string
	name: string
	thumb: string
}

// 模型列表
const models: Model[] = [
	{id: "nori", name: "Nori", thumb: nori},
	{id: "arg-nori", name: "ARG Nori", thumb: arNori}
]

// 配置键名
const CONFIG_KEY = "selected_model"

// 选中的模型 id
const selected = ref("nori")

// 组件挂载时读取已保存的配置
onMounted(async () => {
	try {
		const SAVED = await invoke<{ String?: string }>("get_config", {key: CONFIG_KEY})
		if (SAVED?.String && models.some(m => m.id === SAVED.String)) {
			selected.value = SAVED.String
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
			<h2 class="model-title glow-teal">选择模型</h2>
			<p class="model-sub">可后期更改</p>
		</div>
		<div class="model-grid">
			<button
				v-for="model in models"
				:key="model.id"
				class="model-card"
				:class="{active: selected === model.id}"
				@click="selected = model.id"
			>
				<span class="model-thumb-wrap">
					<img class="model-thumb" :src="model.thumb" :alt="model.name"/>
					<span class="model-check">✓</span>
				</span>
				<span class="model-name">{{ model.name }}</span>
			</button>
		</div>
	</section>
</template>

<style scoped lang="less">
.page {
	width: 100%;
	height: 100%;
	padding: 0.6rem 5.6rem 0.8rem;
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
	gap: 0.6rem;
}

.model-title {
	font-size: 2.4rem;
	font-weight: 700;
	color: var(--text-primary);
}

.model-sub {
	font-size: 1.2rem;
	color: var(--text-faint);
}

.model-grid {
	display: flex;
	flex-direction: row;
	gap: 2.4rem;
}

.model-card {
	padding: 0.8rem 0.8rem 1.0rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	border: 0.2rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.04);
	cursor: pointer;
	font-family: inherit;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		transform: translateY(-0.2rem);
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.1);
		box-shadow: 0 0 1.6rem var(--glow-teal-soft);
	}
}

// 图片分辨率 300x512, 保持较小尺寸避免放大模糊
.model-thumb-wrap {
	display: grid;
	grid-template-areas: "thumb";
	place-items: center;
	overflow: hidden;
	border-radius: var(--radius-sm);
}

.model-thumb {
	grid-area: thumb;
	width: 12.8rem;
	height: 21.2rem;
	object-fit: contain;
}

.model-name {
	font-size: 1.3rem;
	font-weight: 500;
	color: var(--text-primary);
}

.model-check {
	grid-area: thumb;
	align-self: start;
	justify-self: end;
	margin: 0.6rem;
	width: 1.8rem;
	height: 1.8rem;
	border-radius: 50%;
	background: var(--nori-teal);
	color: #05121a;
	font-size: 1.1rem;
	line-height: 1.8rem;
	text-align: center;
	opacity: 0;
	transform: scale(0.6);
	transition: all 0.2s ease;

	.active & {
		opacity: 1;
		transform: scale(1);
	}
}
</style>
