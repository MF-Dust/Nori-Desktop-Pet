<script setup lang="ts">
/**
 * 通用按钮
 *
 * 外观全部走 uno shortcuts (btn-primary / btn-ghost / btn-danger / btn-icon),
 * 组件本身只负责变体、尺寸、loading 与无障碍属性。
 */
import {computed} from "vue"
import Icon from "../Icon.vue"
import type {IconName} from "../../services/icon"

const props = withDefaults(defineProps<{
	/** 变体 */
	variant?: "primary" | "ghost" | "danger" | "icon"
	/** 尺寸 */
	size?: "sm" | "md"
	/** 左侧图标 */
	icon?: IconName | string
	/** 载入中: 图标换成转圈并禁用 */
	loading?: boolean
	disabled?: boolean
	/** 图标按钮必须给出可读名称 */
	label?: string
}>(), {
	variant: "ghost",
	size: "md",
	loading: false,
	disabled: false,
})

const emit = defineEmits<{click: [event: MouseEvent]}>()

const VARIANT_CLASS = computed(() => ({
	primary: "btn-primary",
	ghost: "btn-ghost",
	danger: "btn-danger",
	icon: "btn-icon",
}[props.variant]))

const SIZE_CLASS = computed(() => {
	if (props.variant === "icon") return props.size === "sm" ? "w-6 h-6" : "w-7 h-7"
	return props.size === "sm" ? "px-3 py-1.5 text-sm" : ""
})

const ICON_SIZE = computed(() => (props.size === "sm" ? 13 : 15))

const onClick = (event: MouseEvent) => {
	if (props.disabled || props.loading) return
	emit("click", event)
}
</script>

<template>
	<button
		type="button"
		:class="[VARIANT_CLASS, SIZE_CLASS]"
		:disabled="disabled || loading"
		:aria-label="label"
		:aria-busy="loading"
		@click="onClick"
	>
		<Icon
			v-if="loading || icon"
			:name="loading ? 'loading' : (icon as IconName)"
			:class="{spin: loading}"
			:size="ICON_SIZE"
		/>
		<span v-if="$slots.default" class="whitespace-nowrap"><slot/></span>
	</button>
</template>
