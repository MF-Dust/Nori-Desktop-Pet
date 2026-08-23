<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {MODEL_LIST, type ModelInfo} from "../../services/live2d/models"
import {createLive2D} from "../../services/live2d"
import {resolveModelFileBase} from "../../services/live2d/config"
import {RUNTIME} from "../../services/runtime"
import Icon from "../Icon.vue"
import AppChip from "../ui/AppChip.vue"
import AppButton from "../ui/AppButton.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import {feedback} from "../../services/feedback"
import AdjustControls from "./AdjustControls.vue"
import Live2dBehaviorControls from "./Live2dBehaviorControls.vue"

const I18N = computed(() => useLanguages().views.main.model)

// 各模型安装状态与当前选择由后端快照提供
const installedMap = ref<Record<string, boolean>>({})
const selectedModel = ref("")

// 本地导入状态
const importing = ref(false)
const importStatusText = ref("")

// 本地导入 Live2D 模型 (文件选择与导入均由宿主完成)
const importLocalModel = async () => {
	if (importing.value) return
	importing.value = true
	importStatusText.value = I18N.value.import.picking
	try {
		const imported = await RUNTIME.importLocalModel()
		if (imported?.length) {
			importStatusText.value = `${I18N.value.import.success}: ${imported.join(", ")}`
			await refreshStatus()
			setTimeout(() => { importStatusText.value = "" }, 3000)
		} else importStatusText.value = ""
	} catch (error) {
		feedback.error(I18N.value.import.failed, error)
		importStatusText.value = ""
	} finally {
		importing.value = false
	}
}

// 展开蒙版菜单的模型 id
const cardMenuFor = ref<string | null>(null)

// 打开整页调整面板的模型 id
const adjustFor = ref<string | null>(null)

// 模型 id → 展示名
const modelNameOf = (id: string): string => MODEL_LIST.find((model) => model.id === id)?.name ?? id

// 快照刷新后同步模型目录
const refreshStatus = async () => {
	await RUNTIME.refresh()
	const ITEMS = RUNTIME.snapshot.value?.models.items ?? []
	installedMap.value = Object.fromEntries(ITEMS.map(item => [item.id, item.installed]))
	selectedModel.value = RUNTIME.snapshot.value?.models.selected ?? selectedModel.value
}

onMounted(async () => {
	await RUNTIME.init()
	await refreshStatus()
	window.addEventListener("resize", onWindowResize)
})

onBeforeUnmount(() => {
	window.removeEventListener("resize", onWindowResize)
	unbindPreviewClick()
	void PREVIEW.destroy()
})

// 点击模型卡: 展开/收起蒙版菜单 (仅已安装模型可操作, 未安装请通过导入添加)
const toggleCardMenu = (model: ModelInfo) => {
	if (adjustFor.value || !installedMap.value[model.id]) return
	cardMenuFor.value = cardMenuFor.value === model.id ? null : model.id
}

// 启用模型: 写入后端 selected_model
const enableModel = async (model: ModelInfo) => {
	if (selectedModel.value === model.id || !installedMap.value[model.id]) return
	try {
		await RUNTIME.selectModel(model.id)
		selectedModel.value = model.id
		await refreshStatus()
	} catch (error) {
		feedback.error(I18N.value.enableFailed, error)
	}
}

// ---- 整页调整: 实时预览 ----
const PREVIEW = createLive2D()
const showcaseRef = ref<HTMLElement>()
const previewReady = ref(false)
const previewClickInteraction = ref(true)

const onPreviewClick = (event: MouseEvent) => {
	if (!previewReady.value || !previewClickInteraction.value) return
	PREVIEW.tapAt(event.clientX, event.clientY)
}

const bindPreviewClick = () => {
	const CANVAS = PREVIEW.canvas()
	CANVAS?.addEventListener("click", onPreviewClick)
}

const unbindPreviewClick = () => {
	const CANVAS = PREVIEW.canvas()
	CANVAS?.removeEventListener("click", onPreviewClick)
}

const syncPreviewClickInteraction = async () => {
	await RUNTIME.refresh()
	previewClickInteraction.value = RUNTIME.snapshot.value?.behaviors.clickInteraction ?? true
	PREVIEW.setClickInteraction(previewClickInteraction.value)
}

// 预览模型显示参数 (调整的模型, 非桌宠当前模型)
const pvScale = ref(1)
const previewExpressionList = ref<string[]>([])

// 预览画布布局到预览区域 (由控制器容器自动定位)
const refreshPreviewLayout = () => {
	if (!previewReady.value) return
	PREVIEW.resize()
	PREVIEW.setUserScale(pvScale.value)
}

// 窗口尺寸变化时重新对齐预览画布
const onWindowResize = () => {
	if (adjustFor.value) refreshPreviewLayout()
}

// ---- 预览配置保存 (按模型存储, 桌宠窗口会热更新) ----
const savePreviewScale = (): void => {
	const MODEL = adjustFor.value
	if (!MODEL) return
	void RUNTIME.setModelDisplay(MODEL, {scale: pvScale.value}).catch(error => console.error("保存预览缩放失败:", error))
}

const savePreviewExpressions = (list: string[]): void => {
	const MODEL = adjustFor.value
	if (!MODEL) return
	void RUNTIME.setModelDisplay(MODEL, {expressions: list}).catch(error => console.error("保存预览表情失败:", error))
}

// 预览播放表情
const applyPreviewExpressions = async (list: string[]): Promise<void> => {
	if (!previewReady.value) return
	try {
		await PREVIEW.stopExpression()
		for (const name of list) await PREVIEW.playExpression(name)
	} catch {
		/* 预览未就绪时忽略 */
	}
}

const onPreviewScale = (value: number) => {
	pvScale.value = value
	refreshPreviewLayout()
	savePreviewScale()
}

const onPreviewExpressions = (list: string[]) => {
	previewExpressionList.value = list
	savePreviewExpressions(list)
	void applyPreviewExpressions(list)
}

// 打开整页调整面板: 挂载该模型的实时预览
const openAdjust = async (model: ModelInfo) => {
	cardMenuFor.value = null
	adjustFor.value = model.id
	previewReady.value = false
	await nextTick()

	// 读取该模型的显示配置与后端元数据
	try {
		const META = await RUNTIME.modelMeta(model.id)
		pvScale.value = META.scale
		const SNAPSHOT = RUNTIME.snapshot.value
		previewExpressionList.value = SNAPSHOT?.models.selected === model.id ? [...SNAPSHOT.models.expressions] : []
	} catch (error) {
		console.error("读取模型显示配置失败:", error)
		pvScale.value = 1
		previewExpressionList.value = []
	}

	// 以预览区域尺寸挂载预览, 避免画布变形
	const RECT = showcaseRef.value?.getBoundingClientRect()
	if (!RECT) return
	try {
		unbindPreviewClick()
		await PREVIEW.destroy()
		await PREVIEW.mount(
			{directory: model.id, fileBase: resolveModelFileBase(model.id)},
			{container: showcaseRef.value, canvasWidth: Math.max(0, Math.round(RECT.width)), canvasHeight: Math.max(0, Math.round(RECT.height))}
		)
		PREVIEW.setUserScale(pvScale.value)
	} catch (error) {
		feedback.error(I18N.value.previewFailed, error)
	}
	previewReady.value = true
	await syncPreviewClickInteraction()
	bindPreviewClick()
	refreshPreviewLayout()
	await applyPreviewExpressions(previewExpressionList.value)
}

// 关闭调整面板: 卸载预览
const closeAdjust = () => {
	adjustFor.value = null
	previewReady.value = false
	unbindPreviewClick()
	void PREVIEW.destroy()
}
</script>

<template>
	<section class="w-full h-full flex flex-col items-center gap-6 px-6 py-4 scroll-area">
		<AppSectionHeader class="w-full" :title="I18N.title" :subtitle="I18N.sub">
			<template #actions>
				<AppButton
					icon="package"
					class="text-nori-teal-bright border-line-strong bg-nori-teal-bright/10 shadow-[0_0_1.4rem_rgba(125,227,255,0.12)] hover:bg-nori-teal-bright/15"
					:loading="importing"
					@click="importLocalModel"
				>
					{{ importing ? I18N.import.importing : I18N.import.button }}
				</AppButton>
			</template>
		</AppSectionHeader>

		<p
			v-if="importStatusText"
			class="w-full m-0 px-4 py-2.5 rounded-sm text-center text-sm font-500 text-nori-teal-bright bg-nori-teal-bright/10 border border-nori-teal-soft/80 shadow-[0_0_1.2rem_rgba(125,227,255,0.15)]"
			aria-live="polite"
		>{{ importStatusText }}</p>

		<!-- 模型卡片展示网格 -->
		<div class="flex flex-wrap justify-center gap-7">
			<div
				v-for="model in MODEL_LIST"
				:key="model.id"
				class="relative flex flex-col items-center gap-2.5 p-3 pb-3.5 rounded-md border transition-all duration-250
					hover:(border-line-glow bg-bg-card-hover -translate-y-[0.25rem] shadow-[0_0.8rem_2.8rem_rgba(0,0,0,0.5),0_0_1.6rem_var(--glow-teal-soft)])"
				:class="selectedModel === model.id
					? 'border-nori-teal bg-nori-teal-bright/8 shadow-[0_0.6rem_2.4rem_rgba(0,0,0,0.4),0_0_1.8rem_var(--glow-teal-soft)]'
					: 'border-line-subtle surface-card'"
			>
				<button
					type="button"
					class="group/thumb relative w-[15.6rem] h-[25.6rem] overflow-hidden rounded-sm border border-line-subtle bg-black/40 cursor-pointer focus-ring"
					:aria-label="model.name"
					:aria-expanded="cardMenuFor === model.id"
					@click="toggleCardMenu(model)"
				>
					<img class="w-full h-full object-cover block transition-transform duration-300 group-hover/thumb:scale-106" :src="model.thumb" :alt="model.name"/>
					<span class="absolute inset-0 pointer-events-none bg-gradient-to-b from-transparent via-transparent to-bg-abyss/85"/>
					<span
						v-if="selectedModel === model.id"
						class="absolute top-2 right-2 px-2 py-0.5 rounded-pill text-xs font-600 bg-nori-teal-bright text-on-teal shadow-[0_0_1rem_var(--glow-teal)]"
					>
						{{ I18N.current }}
					</span>
				</button>

				<span class="text-md font-600 text-text-primary tracking-[0.02rem]">{{ model.name }}</span>

				<div class="flex items-center gap-1.5">
					<AppChip :tone="installedMap[model.id] ? 'success' : 'neutral'" dot>
						{{ installedMap[model.id] ? I18N.installed : I18N.notInstalled }}
					</AppChip>
				</div>

				<!-- 点击卡片: 蒙版菜单 (再点一次卡片收起) -->
				<Transition name="fade">
					<div
						v-if="cardMenuFor === model.id"
						class="absolute inset-0 z-5 flex items-center justify-center rounded-md bg-bg-base/85 backdrop-blur-[0.8rem]"
						@click.stop="cardMenuFor = null"
					>
						<div
							class="relative w-[88%] flex flex-col items-center gap-3 px-3.5 py-4.5 rounded-md border border-line-strong
								bg-[linear-gradient(160deg,var(--bg-panel)_0%,var(--bg-abyss)_100%)]
								shadow-[0_1.2rem_3.6rem_rgba(0,0,0,0.7),0_0_2rem_var(--glow-teal-soft)]"
							@click.stop
						>
							<button
								v-if="installedMap[model.id]"
								type="button"
								class="w-full justify-center"
								:class="selectedModel === model.id ? 'btn-ghost' : 'btn-primary'"
								:disabled="selectedModel === model.id"
								@click="enableModel(model)"
							>
								<Icon name="check" :size="13"/>
								<span>{{ selectedModel === model.id ? I18N.enabled : I18N.enable }}</span>
							</button>
							<button
								v-if="installedMap[model.id]"
								type="button"
								class="btn-ghost w-full justify-center"
								@click="openAdjust(model)"
							>
								<Icon name="settings" :size="13"/>
								<span>{{ I18N.adjust }}</span>
							</button>
						</div>
					</div>
				</Transition>
			</div>
		</div>

		<!-- 行为与交互设置 (始终可见) -->
		<div class="w-full max-w-[62rem] mx-auto px-6 py-5 glass-panel rounded-lg shadow-[0_0.6rem_2.4rem_rgba(0,0,0,0.35)]">
			<Live2dBehaviorControls :model-id="selectedModel"/>
		</div>

		<!-- 整页调整面板: 铺满整个页面, 左侧橱窗实时预览, 右侧控制 -->
		<Transition name="fade">
			<div
				v-if="adjustFor"
				class="fixed inset-0 z-100 flex flex-col backdrop-blur-[1.6rem]
					bg-[radial-gradient(ellipse_70rem_50rem_at_20%_20%,rgba(125,227,255,0.1)_0%,transparent_65%),linear-gradient(165deg,rgba(8,20,32,0.98)_0%,rgba(2,10,18,0.99)_100%)]"
			>
				<button
					type="button"
					class="absolute top-4.5 left-5 z-3 w-[4rem] h-[4rem] flex items-center justify-center rounded-full
						border border-line-subtle bg-white/6 text-text-body cursor-pointer transition-all duration-200 focus-ring shadow-soft
						hover:(text-nori-teal-bright border-nori-teal-soft bg-nori-teal-bright/14 -translate-y-[0.1rem] shadow-[0_0_1.6rem_var(--glow-teal-soft)])"
					:title="I18N.back"
					:aria-label="I18N.back"
					@click="closeAdjust"
				>
					<Icon name="arrow-left" :size="18"/>
				</button>

				<!-- 预览画布容器: 尺寸被 getBoundingClientRect 读取用于建画布, 不要改动几何 -->
				<div ref="showcaseRef" class="absolute top-[5.6rem] left-6 w-[34rem] h-[50rem] z-201"/>

				<div class="absolute top-[5.6rem] left-[40rem] right-6 bottom-5 px-6 py-5 scroll-area glass-panel shadow-[0_0.8rem_3.2rem_rgba(0,0,0,0.5)]">
					<AdjustControls
						:model-id="adjustFor"
						:model-name="modelNameOf(adjustFor)"
						:expression-enabled="true"
						:initial-scale="pvScale"
						:initial-expressions="previewExpressionList"
						@scale="onPreviewScale"
						@expressions="onPreviewExpressions"
					/>
					<div class="h-[0.1rem] my-5 bg-line-subtle"/>
					<Live2dBehaviorControls :model-id="adjustFor"/>
				</div>
			</div>
		</Transition>
	</section>
</template>
