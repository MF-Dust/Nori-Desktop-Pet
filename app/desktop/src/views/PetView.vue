<script setup lang="ts">
import {onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {createLive2D} from "../services/live2d"
import {resolveModelFileBase} from "../services/live2d/config"

const L2D = createLive2D()

const modelName = ref("arg-nori")
const canvasRef = ref<HTMLCanvasElement>()

const onMouseMove = (e: MouseEvent) => {
	L2D.lookAt(e).catch(() => {
		/* 未加载完成时忽略 */
	})
}

onMounted(async () => {
	try {
		const saved = await invoke<string | null>("get_config", {key: "selected_model"})
		if (saved) modelName.value = saved
	} catch {
	}

	try {
		await L2D.mount({
			directory: modelName.value,
			fileBase: resolveModelFileBase(modelName.value),
		})
	} catch (error) {
		console.error("加载 Live2D 模型失败:", error)
	}
})

onBeforeUnmount(() => {
	void L2D.destroy()
})
</script>

<template>
	<div class="pet-stage" @mousemove="onMouseMove">
		<canvas ref="canvasRef" class="pet-canvas"/>
	</div>
</template>

<style scoped lang="less">
.pet-stage {
	position: relative;
	width: 100%;
	height: 100%;
	overflow: visible;
	background: transparent;
	cursor: pointer;
	user-select: none;
}

.pet-canvas {
	position: absolute;
	top: 0;
	left: 0;
	width: 100%;
	height: 100%;
	pointer-events: none;
	display: block;
}
</style>
