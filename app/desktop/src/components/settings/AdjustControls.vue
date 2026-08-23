<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"

const props = withDefaults(defineProps<{
	// 模型 id (资源名)
	modelId: string
	// 展示名
	modelName?: string
	// 是否可调整表情 (未启用/未安装的模型不可播放)
	expressionEnabled?: boolean
	// 初始缩放
	initialScale?: number
	// 初始表情选择
	initialExpressions?: string[]
}>(), {
	modelName: "",
	expressionEnabled: true,
	initialScale: 1,
	initialExpressions: () => [],
})

const emit = defineEmits<{
	scale: [value: number]
	expressions: [list: string[]]
}>()

const I18N = computed(() => useLanguages().views.main.model)

// ---- 大小 ----
const scale = ref(props.initialScale)
const scalePercent = computed(() => Math.round(scale.value * 100))

const onScaleInput = () => {
	emit("scale", scale.value)
}

// ---- 表情 ----
interface ExpressionItem {
	name: string
}

const expressions = ref<ExpressionItem[]>([])
const selected = ref<string[]>([...props.initialExpressions])
const loading = ref(true)

// 表情展示名: 优先取已知映射, 否则原样显示
const expressionLabel = (name: string): string =>
	I18N.value.expressionNames[name as keyof typeof I18N.value.expressionNames] ?? name

const selectedCountText = computed(() =>
	selected.value.length > 0
		? `${I18N.value.expressionSelected}${selected.value.length}${I18N.value.expressionCount}`
		: ""
)

// 选择表情: 单选 (再点一次取消)
const toggleExpression = (name: string) => {
	selected.value = selected.value.includes(name) ? [] : [name]
	emit("expressions", [...selected.value])
}

// 清空表情
const clearExpressions = () => {
	selected.value = []
	emit("expressions", [])
}

// 模型元数据由后端读取, 前端只渲染名称
const loadExpressions = async () => {
	if (!props.expressionEnabled) {
		loading.value = false
		return
	}
	try {
		await RUNTIME.init()
		const META = await RUNTIME.modelMeta(props.modelId)
		expressions.value = META.expressions.map(name => ({name}))
	} catch (error) {
		feedback.error(I18N.value.expressionLoadFailed, error)
	} finally {
		loading.value = false
	}
}

onMounted(() => {
	void loadExpressions()
})
</script>

<template>
	<!-- 根容器保持 width:100% + 纵向流, 供 ModelManagement 的调整面板量取宽度 -->
	<div class="w-full flex flex-col gap-[1.8rem]">
		<h3 class="m-0 text-lg font-700 text-text-primary text-left">
			{{ I18N.adjustTitle }}
			<span class="ml-2 text-xs font-500 text-nori-teal-bright">{{ modelName || modelId }}</span>
		</h3>

		<div class="flex flex-col items-start gap-[0.9rem]">
			<span class="text-sm font-500 text-text-body">{{ I18N.scale }}</span>
			<div class="w-full flex items-center gap-3">
				<n-slider
					v-model:value="scale"
					:min="0.5"
					:max="2"
					:step="0.05"
					:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
					class="flex-1 min-w-0"
					@update:value="onScaleInput"
				/>
				<span class="w-[4.8rem] shrink-0 text-sm font-600 text-right text-nori-teal-bright mono">{{ scalePercent }}%</span>
			</div>
		</div>

		<div class="flex flex-col items-start gap-[0.9rem]">
			<span class="text-sm font-500 text-text-body">{{ I18N.expression }}</span>
			<div v-if="expressionEnabled" class="w-full flex flex-wrap gap-2">
				<button
					type="button"
					class="px-3 py-[0.55rem] rounded-pill border text-xs font-inherit cursor-pointer transition-all duration-200 focus-ring
						hover:(text-nori-teal-bright border-nori-teal-soft bg-nori-teal-bright/8 -translate-y-[0.1rem])"
					:class="selected.length === 0
						? 'font-600 text-on-teal border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal shadow-[0_0.2rem_1rem_var(--glow-teal-soft)]'
						: 'text-text-body border-line-subtle bg-white/4'"
					:aria-pressed="selected.length === 0"
					@click="clearExpressions"
				>
					{{ I18N.expressionNone }}
				</button>
				<button
					v-for="item in expressions"
					:key="item.name"
					type="button"
					class="px-3 py-[0.55rem] rounded-pill border text-xs font-inherit cursor-pointer transition-all duration-200 focus-ring
						hover:(text-nori-teal-bright border-nori-teal-soft bg-nori-teal-bright/8 -translate-y-[0.1rem])"
					:class="selected.includes(item.name)
						? 'font-600 text-on-teal border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal shadow-[0_0.2rem_1rem_var(--glow-teal-soft)]'
						: 'text-text-body border-line-subtle bg-white/4'"
					:aria-pressed="selected.includes(item.name)"
					@click="toggleExpression(item.name)"
				>
					{{ expressionLabel(item.name) }}
				</button>
				<p v-if="!loading && expressions.length === 0" class="m-0 text-hint">{{ I18N.expressionNone }}</p>
			</div>
			<div v-else>
				<p class="m-0 text-hint">{{ I18N.expressionHint }}</p>
			</div>
			<p v-if="expressionEnabled && selected.length > 0" class="m-0 text-xs font-600 text-nori-teal-bright">{{ selectedCountText }}</p>
			<p v-if="expressionEnabled" class="m-0 text-hint">{{ I18N.expressionSelectHint }}</p>
		</div>
	</div>
</template>
