<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {assetUrl, resolveModelFileBase} from "../../services/live2d/config"

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
	params: string[]
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

// 读取 model3.json 中的表情列表与各表情参数
const loadExpressions = async () => {
	if (!props.expressionEnabled) return
	try {
		const URL = assetUrl(`live2d/${props.modelId}/${resolveModelFileBase(props.modelId)}.model3.json`)
		const RESPONSE = await fetch(URL)
		const JSON_DATA = await RESPONSE.json()
		const LIST: {Name?: unknown; File?: unknown}[] = JSON_DATA?.FileReferences?.Expressions ?? []
		const ITEMS = LIST.filter(
			(item): item is {Name: string; File: string} =>
				typeof item?.Name === "string" && item?.Name !== "" && typeof item?.File === "string"
		)

		const PARAMS = await Promise.all(
			ITEMS.map(async (item) => {
				try {
					const FILE_RESPONSE = await fetch(assetUrl(`live2d/${props.modelId}/${item.File}`))
					const FILE_JSON = await FILE_RESPONSE.json()
					const PARAMS: {Id?: unknown}[] = FILE_JSON?.Parameters ?? []
					return {
						name: item.Name,
						params: PARAMS.filter((param): param is {Id: string} => typeof param?.Id === "string").map((param) => param.Id),
					}
				} catch {
					return {name: item.Name, params: []}
				}
			})
		)
		expressions.value = PARAMS
	} catch (error) {
		console.error("读取表情列表失败:", error)
	} finally {
		loading.value = false
	}
}

onMounted(() => {
	void loadExpressions()
})
</script>

<template>
	<div class="adjust-controls">
		<h3 class="adjust-title">
			{{ I18N.adjustTitle }}
			<span class="adjust-model">{{ modelName || modelId }}</span>
		</h3>

		<div class="adjust-section">
			<span class="adjust-label">{{ I18N.scale }}</span>
			<div class="adjust-scale-row">
				<n-slider
					v-model:value="scale"
					:min="0.5"
					:max="2"
					:step="0.05"
					:format-tooltip="(v: number) => `${Math.round(v * 100)}%`"
					class="adjust-slider"
					@update:value="onScaleInput"
				/>
				<span class="adjust-value">{{ scalePercent }}%</span>
			</div>
		</div>

		<div class="adjust-section">
			<span class="adjust-label">{{ I18N.expression }}</span>
			<div v-if="expressionEnabled" class="expression-list">
				<button class="expression-chip" :class="{active: selected.length === 0}" @click="clearExpressions">
					{{ I18N.expressionNone }}
				</button>
				<button
					v-for="item in expressions"
					:key="item.name"
					class="expression-chip"
					:class="{active: selected.includes(item.name)}"
					@click="toggleExpression(item.name)"
				>
					{{ expressionLabel(item.name) }}
				</button>
				<p v-if="!loading && expressions.length === 0" class="adjust-hint">{{ I18N.expressionNone }}</p>
			</div>
			<div v-else>
				<p class="adjust-hint">{{ I18N.expressionHint }}</p>
			</div>
			<p v-if="expressionEnabled && selected.length > 0" class="adjust-count">{{ selectedCountText }}</p>
			<p v-if="expressionEnabled" class="adjust-hint">{{ I18N.expressionSelectHint }}</p>
		</div>
	</div>
</template>

<style scoped lang="less">
.adjust-controls {
	display: flex;
	flex-direction: column;
	gap: 1.8rem;
	width: 100%;
}

.adjust-title {
	margin: 0;
	font-size: 1.6rem;
	font-weight: 700;
	color: var(--text-primary);
	text-align: left;
}

.adjust-model {
	margin-left: 0.8rem;
	font-size: 1.15rem;
	font-weight: 500;
	color: var(--nori-teal-bright);
}

.adjust-section {
	display: flex;
	flex-direction: column;
	align-items: flex-start;
	gap: 0.9rem;
}

.adjust-label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-body);
}

.adjust-scale-row {
	display: flex;
	align-items: center;
	gap: 1.2rem;
	width: 100%;
}

.adjust-range {
	flex: 1;
	min-width: 0;
	height: 0.6rem;
	accent-color: var(--nori-teal-bright);
	cursor: pointer;
}

.adjust-value {
	width: 4.8rem;
	flex-shrink: 0;
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
	font-family: monospace;
	font-weight: 600;
	text-align: right;
}

.expression-list {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
	width: 100%;
}

.expression-chip {
	padding: 0.55rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-body);
	font-size: 1.15rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.08);
		transform: translateY(-0.1rem);
	}

	&.active {
		color: #05121a;
		border-color: transparent;
		background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		box-shadow: 0 0.2rem 1rem var(--glow-teal-soft);
		font-weight: 600;
	}
}

.adjust-count {
	margin: 0;
	font-size: 1.15rem;
	font-weight: 600;
	color: var(--nori-teal-bright);
}

.adjust-hint {
	margin: 0;
	font-size: 1.1rem;
	color: var(--text-faint);
	line-height: 1.4;
}
</style>