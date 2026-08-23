<script setup lang="ts">
import {computed} from "vue"
import {icon, resolveIconName, type IconName, type IconMode, type IconData} from "../services/icon"

const props = withDefaults(defineProps<{
	name: IconName | string
	mode?: IconMode
	size?: number | string
	strokeWidth?: number | string
}>(), {
	mode: "stroke",
	size: 24,
	strokeWidth: 2
})

// 当前图标名 (未知名称兑底, 不让一个脏数据拖崩整个页面)
const iconName = computed<IconName>(() => resolveIconName(props.name))

// 当前图标数据
const iconData = computed<IconData>(() => icon[iconName.value])

// 当前实际使用的模式
const renderMode = computed<IconMode>(() => {
	const DATA = iconData.value
	if (DATA[props.mode]) return props.mode
	if (DATA.stroke) return "stroke"
	if (DATA.fill) return "fill"
	if (DATA.duotone) return "duotone"
	console.error(`图标 ${iconName.value} 不支持 ${props.mode} 模式`)
	return "stroke"
})

// 当前模式下的路径
const paths = computed((): string[] => {
	const DATA = iconData.value
	return DATA[renderMode.value] || []
})

// 是否为加载状态
const isLoading = computed(() => {
	return iconName.value === "loading"
})

// fill 模式
const svgFill = computed(() => {
	return renderMode.value === "fill" ? "currentColor" : "none"
})

// stroke 模式
const svgStroke = computed(() => {
	return renderMode.value === "stroke" ? "currentColor" : "none"
})

// stroke 宽度
const svgStrokeWidth = computed(() => {
	return renderMode.value === "stroke" ? props.strokeWidth : 0
})
</script>

<template>
	<svg
		class="block shrink-0"
		:class="{spin: isLoading}"
		:width="size"
		:height="size"
		viewBox="0 0 24 24"
		:fill="svgFill"
		:stroke="svgStroke"
		:stroke-width="svgStrokeWidth"
		stroke-linecap="round"
		stroke-linejoin="round"
		aria-hidden="true"
		focusable="false"
	>
		<path
			v-for="(d, i) in paths"
			:key="i"
			:d="d"
		/>
	</svg>
</template>