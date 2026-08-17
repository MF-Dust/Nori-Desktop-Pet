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
		const SAVED = await invoke<{String?: string}>("get_config", {key: CONFIG_KEY})
		if (SAVED?.String && models.some(m => m.id === SAVED.String)) {
			selected.value = SAVED.String
		}
	} catch (error) {
		console.error("读取模型配置失败:", error)
	}
})

// 监听选中的模型 id 变化，写入配置和日志
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
				</span>
				<span class="model-name">{{ model.name }}</span>
				<span class="model-check">✓</span>
			</button>
		</div>
	</section>
</template>

<style scoped lang="less">
.page {
	position: absolute;
	inset: 0;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 18px;
	padding: 6px 56px 8px;
	text-align: center;
}

.model-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 6px;
}

.model-title {
	font-size: 24px;
	font-weight: 700;
	color: var(--text-primary);
}

.model-sub {
	font-size: 12px;
	color: var(--text-faint);
}

.model-grid {
	display: flex;
	flex-direction: row;
	gap: 24px;
}

.model-card {
	position: relative;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 8px;
	padding: 8px 8px 10px;
	border: 2px solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.04);
	cursor: pointer;
	font-family: inherit;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		transform: translateY(-2px);
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.1);
		box-shadow: 0 0 16px var(--glow-teal-soft);
	}
}

// 图片分辨率 300x512, 保持较小尺寸避免放大模糊
.model-thumb-wrap {
	display: flex;
	align-items: center;
	justify-content: center;
	overflow: hidden;
	border-radius: var(--radius-sm);
}

.model-thumb {
	width: 128px;
	height: 212px;
	object-fit: contain;
}

.model-name {
	font-size: 13px;
	font-weight: 500;
	color: var(--text-primary);
}

.model-check {
	position: absolute;
	top: 6px;
	right: 6px;
	width: 18px;
	height: 18px;
	border-radius: 50%;
	background: var(--nori-teal);
	color: #05121a;
	font-size: 11px;
	line-height: 18px;
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
