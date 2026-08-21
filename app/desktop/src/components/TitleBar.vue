<script setup lang="ts">
// 窗口通用标题栏: 标题 + 拖拽区, 右侧内容通过 slot 注入
import {getCurrentWindow} from "../services/host/window"
import Icon from "./Icon.vue"

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
		<div class="title-wrap">
			<span class="title-logo-icon">
				<Icon name="sparkles" :size="12"/>
			</span>
			<span class="title">Nori</span>
		</div>
		<slot/>
	</div>
</template>

<style scoped lang="less">
.titlebar {
	height: 4.4rem;
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0 1.4rem 0 1.6rem;
	flex-shrink: 0;
	user-select: none;
	border-bottom: 0.1rem solid rgba(125, 227, 255, 0.06);

	.title-wrap {
		display: flex;
		align-items: center;
		gap: 0.6rem;
	}

	.title-logo-icon {
		display: inline-flex;
		align-items: center;
		justify-content: center;
		color: var(--nori-teal-bright);
		filter: drop-shadow(0 0 0.6rem var(--glow-teal));
	}

	.title {
		color: var(--text-primary);
		font-size: 1.35rem;
		font-weight: 700;
		letter-spacing: 0.08rem;
		background: linear-gradient(135deg, var(--text-primary) 0%, var(--nori-teal-soft) 100%);
		-webkit-background-clip: text;
		-webkit-text-fill-color: transparent;
	}
}
</style>

