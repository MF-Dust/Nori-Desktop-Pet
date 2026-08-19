<script setup lang="ts">
// 窗口通用标题栏: 标题 + 拖拽区, 右侧内容通过 slot 注入
import {getCurrentWindow} from "../services/host/window"

defineSlots<{
	default: () => unknown
}>()

// WebView 会吞掉指针事件, 拿不到原来 data-tauri-drag-region 的效果,
// 改为按下时回调宿主, 由系统接管窗口拖动
const startDrag = (event: MouseEvent) => {
	// 只响应标题栏空白处的左键, 按钮等交互元素不触发
	if (event.button !== 0) return
	if ((event.target as HTMLElement).closest("button, input, a, select, textarea")) return
	void getCurrentWindow().startDragging().catch(() => {
		/* 非宿主环境忽略 */
	})
}
</script>

<template>
	<div class="titlebar" @mousedown="startDrag">
		<span class="title">Nori</span>
		<slot/>
	</div>
</template>

<style scoped lang="less">
.titlebar {
	height: 4.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 1.2rem 0 1.6rem;
	flex-shrink: 0;

	.title {
		color: var(--text-primary);
		font-size: 1.3rem;
		font-weight: 600;
		letter-spacing: 0.05rem;
	}
}
</style>
