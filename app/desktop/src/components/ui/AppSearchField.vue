<script setup lang="ts">
/**
 * 搜索输入框
 *
 * 设置面板 / 技能 / 记忆三处各写了一遍带清除按钮的搜索框, 这里统一。
 * shortcutKey 给出时会注册全局按键聚焦 (设置面板用 "/"), Esc 清空并失焦。
 */
import {onActivated, onBeforeUnmount, onDeactivated, onMounted, ref} from "vue"
import Icon from "../Icon.vue"

const props = withDefaults(defineProps<{
	modelValue: string
	placeholder: string
	/** 清除按钮的无障碍名称 */
	clearLabel: string
	/** 注册为全局聚焦快捷键的单个字符, 例如 "/" */
	shortcutKey?: string
	autofocus?: boolean
}>(), {
	autofocus: false,
})

const EMIT = defineEmits<{(event: "update:modelValue", value: string): void}>()

const INPUT = ref<HTMLInputElement | null>(null)

const clear = () => {
	EMIT("update:modelValue", "")
	INPUT.value?.focus()
}

const onKeydown = (event: KeyboardEvent) => {
	if (event.key !== "Escape") return
	if (props.modelValue) {
		event.stopPropagation()
		EMIT("update:modelValue", "")
		return
	}
	INPUT.value?.blur()
}

// 全局快捷键: 只在焦点不在别的输入控件里时才抢
const onGlobalKeydown = (event: KeyboardEvent) => {
	if (event.key !== props.shortcutKey || event.ctrlKey || event.metaKey || event.altKey) return
	const TARGET = event.target as HTMLElement | null
	if (TARGET && (TARGET.tagName === "INPUT" || TARGET.tagName === "TEXTAREA" || TARGET.isContentEditable)) return
	event.preventDefault()
	INPUT.value?.focus()
}

const bindShortcut = () => {
	if (props.shortcutKey) window.addEventListener("keydown", onGlobalKeydown)
}

const unbindShortcut = () => {
	if (props.shortcutKey) window.removeEventListener("keydown", onGlobalKeydown)
}

onMounted(() => {
	if (props.autofocus) INPUT.value?.focus()
	bindShortcut()
})

onBeforeUnmount(unbindShortcut)

// 被 KeepAlive 缓存住的页面不该继续抢全局快捷键 (设置页藏起来时 "/" 不能再抢焦点)。
// 首次挂载时 onMounted 与 onActivated 都会跑, 但 addEventListener 对同一函数引用是幂等的。
onActivated(bindShortcut)
onDeactivated(unbindShortcut)
</script>

<template>
	<div class="relative flex items-center">
		<Icon name="search" :size="14" class="absolute left-3 text-text-faint pointer-events-none"/>
		<input
			ref="INPUT"
			type="search"
			class="input-base pl-8.5"
			:class="modelValue ? 'pr-8.5' : (shortcutKey ? 'pr-9' : '')"
			:placeholder="placeholder"
			:value="modelValue"
			@input="EMIT('update:modelValue', ($event.target as HTMLInputElement).value)"
			@keydown="onKeydown"
		>
		<button
			v-if="modelValue"
			type="button"
			class="btn-icon absolute right-1.5 w-5.5 h-5.5"
			:title="clearLabel"
			:aria-label="clearLabel"
			@click="clear"
		>
			<Icon name="close" :size="12"/>
		</button>
		<kbd
			v-else-if="shortcutKey"
			class="absolute right-2.5 px-1.5 py-0.2 rounded-xs bg-overlay-6 border border-line-subtle text-xs text-text-faint mono pointer-events-none"
		>{{ shortcutKey }}</kbd>
	</div>
</template>
