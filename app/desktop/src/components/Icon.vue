<script setup lang="ts">
import {computed} from "vue"
import {invoke} from "@tauri-apps/api/core"
import {icon, type IconName, type IconMode, type IconData} from "../services/icon"
import {i18n} from "../services/i18n"

const props = withDefaults(defineProps<{
	name: IconName
	mode?: IconMode
	size?: number | string
	strokeWidth?: number | string
}>(), {
	mode: "stroke",
	size: 24,
	strokeWidth: 2
})

// 当前图标数据
const iconData = computed<IconData>(() => {
	return icon[props.name]
})

// 当前实际使用的模式
const renderMode = computed<IconMode>(() => {
	const DATA = iconData.value
	if (DATA[props.mode]) return props.mode
	if (DATA.stroke) return "stroke"
	if (DATA.fill) return "fill"
	if (DATA.duotone) 	return "duotone"
	try {
		invoke("write_log", {
			level: "warn",
			message: i18n.global.t("log.icon.unsupportedMode", {name: props.name, mode: props.mode})
		})
	} catch (error) {
		console.error(i18n.global.t("log.icon.logWriteFailed", {error: String(error)}))
	}
	return "stroke"
})

// 当前模式下的路径
const paths = computed((): string[] => {
	const DATA = iconData.value
	return DATA[renderMode.value] || []
})

// 是否为加载状态
const isLoading = computed(() => {
	return props.name === "loading"
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
		:class="{
			loading: isLoading,
			[`icon-${renderMode}`]: true
		}"
		:width="size"
		:height="size"
		viewBox="0 0 24 24"
		:fill="svgFill"
		:stroke="svgStroke"
		:stroke-width="svgStrokeWidth"
		stroke-linecap="round"
		stroke-linejoin="round"
	>
		<path
			v-for="(d, i) in paths"
			:key="i"
			:d="d"
		/>
	</svg>
</template>

<style scoped lang="less">
svg {
	display: block;
	flex-shrink: 0;
}

.loading {
	animation: rotate 1s linear infinite;
}

@keyframes rotate {
	from {
		transform: rotate(0deg);
	}
	to {
		transform: rotate(360deg);
	}
}
</style>