<script setup lang="ts">
import {computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages"
import {validateRegionBindings} from "../../services/live2d/interactions"
import {RUNTIME} from "../../services/runtime"
import type {
	InteractionAction,
	InteractionActionMode,
	InteractionReactionMode,
	InteractionRegion,
} from "../../services/runtime/types"
import Icon from "../Icon.vue"
import AppButton from "../ui/AppButton.vue"
import AppChip from "../ui/AppChip.vue"
import AppEmpty from "../ui/AppEmpty.vue"
import AppField from "../ui/AppField.vue"

const props = withDefaults(defineProps<{
	modelId: string
	regions: InteractionRegion[]
	selectedId?: string | null
	availableMotions?: {group: string; names: string[]}[]
	availableExpressions?: string[]
	editing?: boolean
	creating?: boolean
}>(), {
	selectedId: null,
	availableMotions: () => [],
	availableExpressions: () => [],
	editing: true,
	creating: false,
})

const emit = defineEmits<{
	"update:selectedId": [id: string | null]
	"update:regions": [regions: InteractionRegion[]]
	"update:editing": [editing: boolean]
	"update:creating": [creating: boolean]
	"addRegion": []
	"deleteRegion": [id: string]
	"clearRegions": []
}>()

const I18N = computed(() => useLanguages().views.main.model.interactions)
const MODEL_I18N = computed(() => useLanguages().views.main.model)
const AI_CONFIGURED = computed(() => Boolean(RUNTIME.snapshot.value?.ai.configured))

// 当前选中的区域对象
const selectedRegion = computed(() =>
	props.regions.find(r => r.id === props.selectedId) ?? null
)

// 当前选中区域在列表中的序号 (1-indexed)
const selectedIndex = computed(() => {
	const idx = props.regions.findIndex(r => r.id === props.selectedId)
	return idx >= 0 ? idx + 1 : 0
})

// 表情展示名
const expressionLabel = (name: string): string =>
	MODEL_I18N.value.expressionNames[name as keyof typeof MODEL_I18N.value.expressionNames] ?? name

// 动作分组下拉选项
const motionGroupOptions = computed(() =>
	props.availableMotions.map(m => ({
		label: m.group,
		value: m.group,
	}))
)

// 动作文件下拉选项
const motionNameOptions = computed(() => {
	const currentGroup = selectedRegion.value?.motion.group
	if (!currentGroup) return []
	const match = props.availableMotions.find(m => m.group === currentGroup)
	return (match?.names ?? []).map(n => ({
		label: n,
		value: n,
	}))
})

// 表情文件下拉选项
const expressionOptions = computed(() =>
	props.availableExpressions.map(name => ({
		label: expressionLabel(name),
		value: name,
	}))
)

// 绑定有效性校验
const bindingStatus = computed(() => {
	if (!selectedRegion.value) {
		return {motionGroupValid: true, motionNameValid: true, expressionValid: true, isValid: true}
	}
	return validateRegionBindings(
		selectedRegion.value,
		props.availableMotions,
		props.availableExpressions,
	)
})

// 更新当前选中区域的字段
const updateSelectedField = <K extends keyof InteractionRegion>(key: K, value: InteractionRegion[K]) => {
	if (!selectedRegion.value) return
	const targetId = selectedRegion.value.id
	const updated = props.regions.map(r => (r.id === targetId ? {...r, [key]: value} : r))
	emit("update:regions", updated)
}

// 修改反应模式 (local / ai)
const onReactionModeChange = (mode: InteractionReactionMode) => {
	if (mode === "ai" && !AI_CONFIGURED.value) return
	updateSelectedField("reactionMode", mode)
}

// 修改动作模式 (none / random / selected)
const onMotionModeChange = (mode: InteractionActionMode) => {
	if (!selectedRegion.value) return
	const nextMotion: InteractionAction = mode === "selected"
		? {mode}
		: {mode}
	// 切换到 selected 时若未设置 group 且有可用动作组，默认选中第一个
	if (mode === "selected" && !nextMotion.group && props.availableMotions.length > 0) {
		nextMotion.group = props.availableMotions[0].group
		if (props.availableMotions[0].names.length > 0) {
			nextMotion.name = props.availableMotions[0].names[0]
		}
	}
	updateSelectedField("motion", nextMotion)
}

// 修改动作分组
const onMotionGroupChange = (group: string) => {
	if (!selectedRegion.value) return
	const groupMatch = props.availableMotions.find(m => m.group === group)
	const nextName = groupMatch && groupMatch.names.length > 0 ? groupMatch.names[0] : undefined
	const nextMotion: InteractionAction = {
		...selectedRegion.value.motion,
		group,
		name: nextName,
	}
	updateSelectedField("motion", nextMotion)
}

// 修改动作名称
const onMotionNameChange = (name: string) => {
	if (!selectedRegion.value) return
	const nextMotion: InteractionAction = {
		...selectedRegion.value.motion,
		name,
	}
	updateSelectedField("motion", nextMotion)
}

// 修改表情模式 (none / random / selected)
const onExpressionModeChange = (mode: InteractionActionMode) => {
	if (!selectedRegion.value) return
	const nextExpression: InteractionAction = mode === "selected"
		? {mode}
		: {mode}
	// 切换到 selected 时若未设置 name 且有可用表情，默认选中第一个
	if (mode === "selected" && !nextExpression.name && props.availableExpressions.length > 0) {
		nextExpression.name = props.availableExpressions[0]
	}
	updateSelectedField("expression", nextExpression)
}

// 修改表情名称
const onExpressionNameChange = (name: string) => {
	if (!selectedRegion.value) return
	const nextExpression: InteractionAction = {
		...selectedRegion.value.expression,
		name,
	}
	updateSelectedField("expression", nextExpression)
}
</script>

<template>
	<div class="w-full flex flex-col gap-4">
		<!-- 头部控制栏：标题、模式切换、新建与清空 -->
		<div class="w-full flex flex-wrap items-center justify-between gap-3">
			<div class="flex items-center gap-2">
				<h3 class="m-0 text-lg font-700 text-text-primary text-left">
					{{ I18N.title }}
				</h3>
				<AppChip v-if="regions.length > 0" tone="teal" dot>
					<span>{{ regions.length }}</span>
					<span class="ml-0.5">{{ I18N.countSuffix }}</span>
				</AppChip>
			</div>

			<div class="flex items-center gap-2">
				<!-- 编辑模式 / 预览测试模式 切换 -->
				<div class="flex items-center p-0.5 rounded-sm bg-white/5 border border-line-subtle">
					<button
						type="button"
						class="px-2.5 py-1 rounded-xs text-xs font-600 transition-all duration-150 focus-ring flex items-center gap-1.5"
						:class="editing ? 'bg-nori-teal-bright text-on-teal shadow-[0_0_0.8rem_var(--glow-teal)]' : 'text-text-muted hover:text-text-body'"
						@click="emit('update:editing', true)"
					>
						<Icon name="edit" :size="13"/>
						<span>{{ I18N.editMode }}</span>
					</button>
					<button
						type="button"
						class="px-2.5 py-1 rounded-xs text-xs font-600 transition-all duration-150 focus-ring flex items-center gap-1.5"
						:class="!editing ? 'bg-nori-teal-bright text-on-teal shadow-[0_0_0.8rem_var(--glow-teal)]' : 'text-text-muted hover:text-text-body'"
						@click="emit('update:editing', false)"
					>
						<Icon name="play" :size="13"/>
						<span>{{ I18N.previewMode }}</span>
					</button>
				</div>

				<!-- 新建区域按钮 -->
				<AppButton
					icon="plus"
					class="btn-primary py-1 text-xs font-600"
					@click="emit('addRegion')"
				>
					{{ I18N.add }}
				</AppButton>

				<!-- 清空全部按钮 (二次确认) -->
				<n-popconfirm
					v-if="regions.length > 0"
					@positive-click="emit('clearRegions')"
				>
					<template #trigger>
						<AppButton
							icon="trash"
							class="btn-danger py-1 text-xs"
							:aria-label="I18N.clearAll"
						>
							{{ I18N.clearAll }}
						</AppButton>
					</template>
					<span>{{ I18N.clearConfirm }}</span>
				</n-popconfirm>
			</div>
		</div>

		<!-- 模式说明提示 -->
		<p class="m-0 text-hint text-left">
			{{ editing ? I18N.editModeDesc : I18N.previewModeDesc }}
		</p>

		<!-- 区域选择条 (Chips 滚动栏) -->
		<div v-if="regions.length > 0" class="w-full flex items-center gap-2 overflow-x-auto py-1 scroll-area">
			<button
				v-for="(r, idx) in regions"
				:key="r.id"
				type="button"
				class="shrink-0 px-3 py-1.5 rounded-sm border text-xs font-600 transition-all duration-200 focus-ring flex items-center gap-1.5 cursor-pointer"
				:class="r.id === selectedId
					? 'border-nori-teal-bright bg-nori-teal-bright/20 text-text-primary shadow-[0_0_1rem_var(--glow-teal-soft)]'
					: 'border-line-subtle bg-white/4 text-text-muted hover:(border-nori-teal-soft bg-nori-teal-bright/10 text-text-body)'"
				@click="emit('update:selectedId', r.id)"
			>
				<span class="w-4 text-center text-xs opacity-75 font-600">{{ idx + 1 }}</span>
				<span>{{ r.name || I18N.defaultRegionName }}</span>
				<span
					class="px-1 py-0.2 rounded-pill text-xs font-500"
					:class="r.reactionMode === 'ai' ? 'bg-warning/12 text-warning' : 'bg-nori-teal-soft/20 text-nori-teal-bright'"
				>
					{{ r.reactionMode === 'ai' ? I18N.modeAi : I18N.modeLocal }}
				</span>
			</button>
		</div>

		<!-- 空区域状态 -->
		<AppEmpty
			v-if="regions.length === 0"
			class="my-4 py-8 rounded-md border border-line-subtle bg-white/2"
			:title="I18N.empty"
		/>

		<!-- 未选中区域时的提示 -->
		<div
			v-else-if="!selectedRegion"
			class="w-full p-4 rounded-md border border-line-subtle bg-white/2 text-center text-text-muted text-sm"
		>
			{{ I18N.selectHint }}
		</div>

		<!-- 选中区域属性编辑表单 -->
		<div
			v-else
			class="w-full flex flex-col gap-4 p-4.5 rounded-md border border-line-strong bg-bg-card/75 backdrop-blur-[1rem] shadow-[0_0.6rem_2.4rem_rgba(0,0,0,0.3)]"
		>
			<!-- 区域名称与序号 -->
			<div class="w-full flex items-center justify-between gap-3">
				<div class="flex items-center gap-2">
					<span class="w-5 h-5 flex items-center justify-center rounded-xs bg-nori-teal-bright/20 border border-nori-teal-soft text-nori-teal-bright text-xs font-700">
						{{ selectedIndex }}
					</span>
					<span class="text-sm font-600 text-text-primary">{{ I18N.selectedLabel }}</span>
				</div>

				<AppButton
					icon="trash"
					class="btn-danger py-0.8 text-xs font-500"
					@click="emit('deleteRegion', selectedRegion.id)"
				>
					{{ I18N.deleteRegion }}
				</AppButton>
			</div>

			<!-- 区域名称输入 -->
			<AppField :label="I18N.regionName">
				<n-input
					:value="selectedRegion.name"
					:placeholder="I18N.regionNamePlaceholder"
					maxlength="24"
					clearable
					@update:value="updateSelectedField('name', $event)"
				/>
			</AppField>

			<!-- 反应模式选择 (本地 vs AI) -->
			<div class="flex flex-col items-start gap-1.5">
				<span class="text-sm font-500 text-text-body">{{ I18N.reactionMode }}</span>
				<div class="w-full grid grid-cols-2 gap-2.5">
					<button
						type="button"
						class="p-3 rounded-sm border text-left transition-all duration-200 focus-ring cursor-pointer flex flex-col gap-1"
						:class="selectedRegion.reactionMode === 'local'
							? 'border-nori-teal bg-nori-teal-bright/14 shadow-[0_0_1.2rem_var(--glow-teal-soft)]'
							: 'border-line-subtle bg-white/3 hover:bg-white/6'"
						@click="onReactionModeChange('local')"
					>
						<div class="flex items-center justify-between">
							<span class="text-sm font-600 text-text-primary">{{ I18N.modeLocal }}</span>
							<Icon v-if="selectedRegion.reactionMode === 'local'" name="check" class="text-nori-teal-bright" :size="14"/>
						</div>
						<span class="text-xs text-text-muted">{{ I18N.modeLocalDesc }}</span>
					</button>

					<button
						type="button"
						class="p-3 rounded-sm border text-left transition-all duration-200 focus-ring cursor-pointer flex flex-col gap-1"
						:class="!AI_CONFIGURED
							? 'border-line-subtle bg-white/2 opacity-55 cursor-not-allowed'
							: selectedRegion.reactionMode === 'ai'
								? 'border-warning bg-warning/12 shadow-[0_0_1.2rem_var(--glow-teal-soft)]'
								: 'border-line-subtle bg-white/3 hover:bg-white/6'"
						:disabled="!AI_CONFIGURED"
						@click="onReactionModeChange('ai')"
					>
						<div class="flex items-center justify-between">
							<span class="text-sm font-600 text-text-primary">{{ I18N.modeAi }}</span>
							<Icon v-if="selectedRegion.reactionMode === 'ai'" name="sparkles" class="text-warning" :size="14"/>
						</div>
						<span class="text-xs text-text-muted">{{ I18N.modeAiDesc }}</span>
					</button>
				</div>
			</div>

			<!-- AI 兜底说明 (仅在 AI 模式下展示) -->
			<div
				v-if="selectedRegion.reactionMode === 'ai'"
				class="p-3 rounded-sm border border-warning/35 bg-warning/10 flex flex-col gap-1 text-left"
			>
				<span class="text-xs font-600 text-warning">{{ I18N.aiFallbackTitle }}</span>
				<span class="text-xs text-text-muted">{{ I18N.aiFallbackDesc }}</span>
			</div>

			<!-- 动作配置 -->
			<div class="flex flex-col items-start gap-2 pt-1 border-t border-line-subtle">
				<div class="w-full flex items-center justify-between">
					<span class="text-sm font-600 text-text-body">{{ I18N.motion }}</span>
					<!-- 失效绑定警告 -->
					<AppChip v-if="selectedRegion.motion.mode === 'selected' && (!bindingStatus.motionGroupValid || !bindingStatus.motionNameValid)" tone="danger" dot>
						{{ !bindingStatus.motionGroupValid ? I18N.invalidMotionGroup : I18N.invalidMotionName }}
					</AppChip>
				</div>

				<!-- 动作模式三选一 -->
				<div class="w-full grid grid-cols-3 gap-2">
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.motion.mode === 'none'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onMotionModeChange('none')"
					>
						{{ I18N.motionModeNone }}
					</button>
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.motion.mode === 'random'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onMotionModeChange('random')"
					>
						{{ I18N.motionModeRandom }}
					</button>
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.motion.mode === 'selected'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onMotionModeChange('selected')"
					>
						{{ I18N.motionModeSelected }}
					</button>
				</div>

				<!-- 指定动作时的分组与名称选择 -->
				<div v-if="selectedRegion.motion.mode === 'selected'" class="w-full grid grid-cols-2 gap-2 mt-1">
					<AppField :label="I18N.motionGroup" class="text-left">
						<n-select
							:value="selectedRegion.motion.group"
							:options="motionGroupOptions"
							:placeholder="I18N.motionGroupPlaceholder"
							@update:value="onMotionGroupChange"
						/>
					</AppField>

					<AppField :label="I18N.motionName" class="text-left">
						<n-select
							:value="selectedRegion.motion.name"
							:options="motionNameOptions"
							:placeholder="I18N.motionNamePlaceholder"
							@update:value="onMotionNameChange"
						/>
					</AppField>
				</div>
			</div>

			<!-- 表情配置 -->
			<div class="flex flex-col items-start gap-2 pt-2 border-t border-line-subtle">
				<div class="w-full flex items-center justify-between">
					<span class="text-sm font-600 text-text-body">{{ I18N.expression }}</span>
					<!-- 失效表情警告 -->
					<AppChip v-if="selectedRegion.expression.mode === 'selected' && !bindingStatus.expressionValid" tone="danger" dot>
						{{ I18N.invalidExpression }}
					</AppChip>
				</div>

				<!-- 表情模式三选一 -->
				<div class="w-full grid grid-cols-3 gap-2">
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.expression.mode === 'none'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onExpressionModeChange('none')"
					>
						{{ I18N.expressionModeNone }}
					</button>
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.expression.mode === 'random'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onExpressionModeChange('random')"
					>
						{{ I18N.expressionModeRandom }}
					</button>
					<button
						type="button"
						class="py-1.5 px-2 rounded-xs border text-xs font-600 transition-all focus-ring text-center"
						:class="selectedRegion.expression.mode === 'selected'
							? 'border-nori-teal-bright bg-nori-teal-bright/18 text-text-primary'
							: 'border-line-subtle bg-white/3 text-text-muted hover:text-text-body'"
						@click="onExpressionModeChange('selected')"
					>
						{{ I18N.expressionModeSelected }}
					</button>
				</div>

				<!-- 指定表情时的下拉选择 -->
				<div v-if="selectedRegion.expression.mode === 'selected'" class="w-full mt-1">
					<AppField :label="I18N.expressionName" class="text-left">
						<n-select
							:value="selectedRegion.expression.name"
							:options="expressionOptions"
							:placeholder="I18N.expressionNamePlaceholder"
							@update:value="onExpressionNameChange"
						/>
					</AppField>
				</div>
			</div>

			<!-- 快捷键与操作提示 -->
			<p class="m-0 text-hint text-xs text-left opacity-75">
				{{ I18N.keyboardHint }}
			</p>
		</div>
	</div>
</template>
