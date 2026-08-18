<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "@tauri-apps/api/core"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {createResourceDownload} from "../../services/resourceDownload"
import {MODEL_LIST, type ModelInfo} from "../../services/live2d/models"
import {createLive2D} from "../../services/live2d"
import {
	l2dModelKey,
	parseExpressionList,
	parseNumber,
	readModelConfig,
	resolveModelFileBase,
} from "../../services/live2d/config"
import {applyCanvasLayout} from "../../services/live2d/stage"
import {readMotionGroups} from "../../services/live2d/motions"
import Icon from "../Icon.vue"
import AdjustControls from "./AdjustControls.vue"

const I18N = computed(() => useLanguages().views.main.model)

// 资源类型
const RESOURCE_TYPE = "live2d"

// 配置键名
const CONFIG_KEY = "selected_model"

// 下载控制器 (同一时刻只下载一个模型)
const DOWNLOAD = createResourceDownload()

// 各模型安装状态: id -> 是否已安装
const installedMap = ref<Record<string, boolean>>({})

// 当前使用 (selected_model 配置)
const selectedModel = ref("")

// 当前正在下载的模型 id
const activeModelId = ref<string | null>(null)

// 是否有下载进行中
const downloading = computed(() => activeModelId.value !== null)

// 展开蒙版菜单的模型 id
const cardMenuFor = ref<string | null>(null)

// 打开整页调整面板的模型 id
const adjustFor = ref<string | null>(null)

// 模型 id → 展示名
const modelNameOf = (id: string): string => MODEL_LIST.find((model) => model.id === id)?.name ?? id

// 动态检测各模型安装状态 (挂载 / 每次下载完成后调用)
const refreshStatus = async () => {
	try {
		const results = await Promise.all(
			MODEL_LIST.map(async (model) => {
				const installed = await invoke<boolean>("check_resource", {
					resourceType: RESOURCE_TYPE,
					name: model.id,
				})
				return [model.id, installed] as const
			})
		)
		installedMap.value = Object.fromEntries(results)
	} catch (error) {
		console.error("检测模型状态失败:", error)
	}
	await publishMotions()
}

// 各已安装模型: 读取动作组并写入配置 (聊天时 Rust 注入系统提示词, 供 AI 调用)
const publishMotions = async () => {
	for (const model of MODEL_LIST) {
		if (!installedMap.value[model.id]) continue
		const GROUPS = await readMotionGroups(model.id)
		if (!GROUPS || GROUPS.length === 0) continue
		invoke("set_config", {
			key: `l2d_motions_${model.id}`,
			value: JSON.stringify(GROUPS),
		}).catch(() => {})
	}
}

onMounted(async () => {
	try {
		const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
		if (SAVED) selectedModel.value = SAVED
	} catch (error) {
		console.error("读取模型配置失败:", error)
	}
	await refreshStatus()
	window.addEventListener("resize", onWindowResize)
})

onBeforeUnmount(() => {
	DOWNLOAD.stop()
	window.removeEventListener("resize", onWindowResize)
	void PREVIEW.destroy()
})

// 下载模型 (引导页只下载所选模型, 这里可下载其他模型)
const downloadModel = async (model: ModelInfo) => {
	if (downloading.value) return
	activeModelId.value = model.id
	await DOWNLOAD.ensure(RESOURCE_TYPE, model.id)
	activeModelId.value = null
	await refreshStatus()
}

// 点击模型卡: 展开/收起蒙版菜单 (未下载模型同样可打开, 蒙版仅显示下载按钮)
const toggleCardMenu = (model: ModelInfo) => {
	if (adjustFor.value) return
	cardMenuFor.value = cardMenuFor.value === model.id ? null : model.id
}

// 启用模型: 写入 selected_model (需已安装)
const enableModel = async (model: ModelInfo) => {
	if (selectedModel.value === model.id) return
	if (!installedMap.value[model.id]) return
	try {
		await invoke("set_config", {key: CONFIG_KEY, value: model.id})
		await invoke("write_log", {level: "info", message: `启用模型: ${model.id}`})
		selectedModel.value = model.id
	} catch (error) {
		console.error("启用模型失败:", error)
	}
}

// ---- 整页调整: 实时预览 ----
const PREVIEW = createLive2D()
const showcaseRef = ref<HTMLElement>()
const previewReady = ref(false)

// 预览模型显示参数 (调整的模型, 非桌宠当前模型)
const pvScale = ref(1)
const previewExpressionList = ref<string[]>([])

// 预览画布布局到预览区域
const applyPreviewLayout = () => {
	applyCanvasLayout(PREVIEW.canvas(), showcaseRef.value, {
		zIndex: "200",
		scale: pvScale.value,
		offsetX: 0,
		offsetY: 0,
		animate: false,
	})
}

// 窗口尺寸变化时重新对齐预览画布
const onWindowResize = () => {
	if (adjustFor.value) applyPreviewLayout()
}

// ---- 预览配置保存 (按模型存储, 桌宠窗口会热更新) ----
const savePreviewScale = (): void => {
	const MODEL = adjustFor.value
	if (!MODEL) return
	invoke("set_config", {key: l2dModelKey("l2d_scale", MODEL), value: String(pvScale.value)}).catch(() => {})
}

const savePreviewExpressions = (list: string[]): void => {
	const MODEL = adjustFor.value
	if (!MODEL) return
	invoke("set_config", {key: l2dModelKey("l2d_expression", MODEL), value: list}).catch(() => {})
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
	applyPreviewLayout()
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

	// 读取该模型的显示配置
	pvScale.value = await readModelConfig(model.id, "l2d_scale", parseNumber, 1)
	previewExpressionList.value = await readModelConfig(model.id, "l2d_expression", (value) => {
		const LIST = parseExpressionList(value)
		return LIST.length > 0 ? LIST : null
	}, [])

	// 以预览区域尺寸挂载预览, 避免画布变形
	const RECT = showcaseRef.value?.getBoundingClientRect()
	if (!RECT) return
	try {
		await PREVIEW.destroy()
		await PREVIEW.mount(
			{directory: model.id, fileBase: resolveModelFileBase(model.id)},
			{canvasWidth: `${Math.max(0, Math.round(RECT.width))}px`, canvasHeight: `${Math.max(0, Math.round(RECT.height))}px`}
		)
	} catch (error) {
		console.error("加载预览模型失败:", error)
	}
	previewReady.value = true
	applyPreviewLayout()
	await applyPreviewExpressions(previewExpressionList.value)
}

// 关闭调整面板: 卸载预览
const closeAdjust = () => {
	adjustFor.value = null
	previewReady.value = false
	void PREVIEW.destroy()
}
</script>

<template>
	<section class="model-management">
		<div class="mm-head">
			<h2 class="mm-title glow-teal">{{ I18N.title }}</h2>
			<p class="mm-sub">{{ I18N.sub }}</p>
		</div>

		<div class="mm-grid">
			<div
				v-for="model in MODEL_LIST"
				:key="model.id"
				class="mm-card"
				:class="{active: selectedModel === model.id}"
				@click="toggleCardMenu(model)"
			>
				<div class="mm-thumb-wrap">
					<img class="mm-thumb" :src="model.thumb" :alt="model.name"/>
				</div>

				<span class="mm-name">{{ model.name }}</span>

				<div class="mm-tags">
					<span class="mm-tag" :class="installedMap[model.id] ? 'ok' : ''">
						{{ installedMap[model.id] ? I18N.installed : I18N.notInstalled }}
					</span>
					<span v-if="selectedModel === model.id" class="mm-tag current">{{ I18N.current }}</span>
				</div>

				<!-- 点击卡片: 蒙版菜单 (再点一次卡片收起) -->
				<Transition name="mask">
					<div v-if="cardMenuFor === model.id" class="mm-card-mask" @click.stop="cardMenuFor = null">
						<div class="mm-card-menu" @click.stop>
							<button
								v-if="installedMap[model.id]"
								class="mm-menu-btn"
								:class="{enabled: selectedModel === model.id}"
								:disabled="!installedMap[model.id] || selectedModel === model.id"
								@click="enableModel(model)"
							>
								{{ selectedModel === model.id ? I18N.enabled : I18N.enable }}
							</button>
							<button
								v-if="installedMap[model.id]"
								class="mm-menu-btn"
								:disabled="!installedMap[model.id]"
								@click="openAdjust(model)"
							>
								{{ I18N.adjust }}
							</button>
							<button
								v-if="!installedMap[model.id]"
								class="mm-menu-btn"
								:disabled="downloading"
								@click="downloadModel(model)"
							>
								<Icon name="arrow-down" class="mm-menu-btn-icon"/>
								{{ activeModelId === model.id ? I18N.downloading : I18N.download }}
							</button>
						</div>
					</div>
				</Transition>
			</div>
		</div>

		<!-- 整页调整面板: 铺满整个页面, 左侧橱窗实时预览, 右侧控制 -->
		<Transition name="view">
			<div v-if="adjustFor" class="mm-fullpage">
				<button class="mm-back" :title="I18N.back" @click="closeAdjust">
					<Icon name="arrow-left" class="mm-back-icon"/>
				</button>

				<div ref="showcaseRef" class="mm-showcase"></div>

				<div class="mm-adjust-pane">
					<AdjustControls
						:model-id="adjustFor"
						:model-name="modelNameOf(adjustFor)"
						:expression-enabled="true"
						:initial-scale="pvScale"
						:initial-expressions="previewExpressionList"
						@scale="onPreviewScale"
						@expressions="onPreviewExpressions"
					/>
				</div>
			</div>
		</Transition>
	</section>
</template>

<style scoped lang="less">
.model-management {
	width: 100%;
	height: 100%;
	padding: 2.4rem 3.2rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 2rem;
	overflow-y: auto;
}

.mm-head {
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.6rem;
	text-align: center;
}

.mm-title {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--text-primary);
}

.mm-sub {
	font-size: 1.2rem;
	color: var(--text-faint);
}

.mm-grid {
	display: flex;
	flex-wrap: wrap;
	justify-content: center;
	gap: 2.4rem;
}

.mm-card {
	position: relative;
	padding: 0.8rem 0.8rem 1rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	border: 0.2rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.04);
	cursor: default;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.08);
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.1);
		box-shadow: 0 0 1.6rem var(--glow-teal-soft);
	}
}

// 统一展示尺寸: 固定外框 + cover 铺满, 避免不同图片尺寸导致大小不一
.mm-thumb-wrap {
	width: 15.2rem;
	height: 25.2rem;
	overflow: hidden;
	border-radius: var(--radius-sm);
}

.mm-thumb {
	width: 100%;
	height: 100%;
	object-fit: cover;
	display: block;
}

.mm-name {
	font-size: 1.3rem;
	font-weight: 500;
	color: var(--text-primary);
}

.mm-tags {
	display: flex;
	gap: 0.6rem;
	align-items: center;
}

.mm-tag {
	padding: 0.2rem 0.8rem;
	border-radius: 1rem;
	font-size: 1.05rem;
	color: var(--text-faint);
	background: rgba(255, 255, 255, 0.06);
	white-space: nowrap;

	&.ok {
		color: var(--nori-teal);
		background: rgba(94, 234, 212, 0.1);
	}

	&.current {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.12);
	}
}

// ---- 卡片蒙版菜单 ----
.mm-card-mask {
	position: absolute;
	inset: 0;
	z-index: 5;
	display: flex;
	align-items: center;
	justify-content: center;
	background: rgba(2, 10, 18, 0.6);
	backdrop-filter: blur(2px);
	border-radius: var(--radius-md);
	cursor: default;
}

.mm-card-menu {
	position: relative;
	width: 82%;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.7rem;
	padding: 1.4rem 1.2rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: linear-gradient(160deg, var(--bg-panel) 0%, var(--bg-abyss) 100%);
	box-shadow: 0 0 2rem rgba(0, 0, 0, 0.5);
}

.mm-menu-btn {
	width: 100%;
	padding: 0.6rem 0;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-body);
	font-size: 1.25rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;
	display: inline-flex;
	align-items: center;
	justify-content: center;
	gap: 0.6rem;

	&:hover:not(:disabled) {
		color: var(--text-primary);
		border-color: var(--line-strong);
		background: rgba(125, 227, 255, 0.08);
	}

	// 已启用 / 未安装: 灰色禁用
	&.enabled,
	&:disabled {
		color: var(--text-muted);
		opacity: 0.55;
		cursor: default;
		background: rgba(255, 255, 255, 0.04);
		border-color: var(--line-subtle);
	}
}

.mm-menu-btn-icon {
	width: 1.4rem;
	height: 1.4rem;
}

// ---- 整页调整面板: 铺满页面 ----
.mm-fullpage {
	position: fixed;
	inset: 0;
	z-index: 100;
	display: flex;
	flex-direction: column;
	background: linear-gradient(165deg, rgba(8, 18, 28, 0.96) 0%, rgba(2, 10, 18, 0.98) 100%);
	backdrop-filter: blur(5px);
	cursor: default;
}

.mm-back {
	position: absolute;
	top: 1.6rem;
	left: 2rem;
	z-index: 3;
	width: 3.6rem;
	height: 3.6rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: 50%;
	background: rgba(255, 255, 255, 0.05);
	color: var(--text-body);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s ease;

	&:hover {
		color: var(--text-primary);
		border-color: var(--line-strong);
		background: rgba(125, 227, 255, 0.1);
	}
}

.mm-back-icon {
	width: 1.8rem;
	height: 1.8rem;
}

// 预览区域: 无边框锚点, 预览画布定位于此 (预览画布层级 200 高于遮罩 100)
.mm-showcase {
	position: absolute;
	top: 5.6rem;
	left: 2.4rem;
	width: 34rem;
	height: 50rem;
	z-index: 201;
}

// 右侧控制区
.mm-adjust-pane {
	position: absolute;
	top: 5.6rem;
	left: 40rem;
	right: 2.4rem;
	bottom: 2rem;
	overflow-y: auto;
	padding: 0.4rem 0.8rem 1rem 0.2rem;
}

// ---- 动画 ----
.mask-enter-active,
.mask-leave-active {
	transition: opacity 0.18s ease;
}

.mask-enter-from,
.mask-leave-to {
	opacity: 0;
}

.view-enter-active,
.view-leave-active {
	transition: opacity 0.25s ease;
}

.view-enter-from,
.view-leave-to {
	opacity: 0;
}
</style>