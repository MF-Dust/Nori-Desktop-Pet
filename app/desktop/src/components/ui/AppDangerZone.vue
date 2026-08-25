<script setup lang="ts">
/**
 * 危险操作区
 *
 * 破坏性操作 (崩溃测试、清空数据、卸载) 必须与普通设置卡片有结构性区分,
 * 不能只靠颜色: 独立分区 + 明确标题 + 红色描边, 且默认折叠。
 */
import {ref} from "vue"
import Icon from "../Icon.vue"

const PROPS = withDefaults(defineProps<{
	title: string
	desc?: string
	/** 展开/收起按钮的无障碍名称 */
	toggleLabel: string
	/** 默认展开 (缺省折叠, 逼用户多一次确认动作) */
	defaultOpen?: boolean
}>(), {
	defaultOpen: false,
})

const OPEN = ref(PROPS.defaultOpen)
</script>

<template>
	<section class="flex flex-col gap-3 rounded-md border border-danger/35 bg-danger/6 p-4">
		<div class="flex items-start justify-between gap-3">
			<div class="flex items-start gap-2.5 min-w-0">
				<span class="flex shrink-0 items-center justify-center w-7 h-7 rounded-full bg-danger/15 text-danger-text">
					<Icon name="alert" :size="16"/>
				</span>
				<div class="flex flex-col gap-0.5 min-w-0">
					<h3 class="m-0 text-md font-600 text-danger-text">{{ title }}</h3>
					<p v-if="desc" class="m-0 text-hint">{{ desc }}</p>
				</div>
			</div>
			<button
				type="button"
				class="btn-icon shrink-0"
				:title="toggleLabel"
				:aria-label="toggleLabel"
				:aria-expanded="OPEN"
				@click="OPEN = !OPEN"
			>
				<Icon :name="OPEN ? 'arrow-up' : 'arrow-down'" :size="14"/>
			</button>
		</div>

		<div v-if="OPEN" class="flex flex-col gap-3">
			<slot/>
		</div>
	</section>
</template>
