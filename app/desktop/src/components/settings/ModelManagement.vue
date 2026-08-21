<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref} from "vue"
import {listen, type UnlistenFn} from "../../services/host/event"
import {invoke} from "../../services/host/invoke"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {MODEL_LIST, type ModelInfo} from "../../services/live2d/models"
import {createLive2D} from "../../services/live2d"
import {
	l2dModelKey,
	parseExpressionList,
	parseNumber,
	readBehaviorConfig,
	readModelConfig,
	resolveModelFileBase,
} from "../../services/live2d/config"
import {readMotionGroups} from "../../services/live2d/motions"
import Icon from "../Icon.vue"
import AdjustControls from "./AdjustControls.vue"
import Live2dBehaviorControls from "./Live2dBehaviorControls.vue"

const I18N = computed(() => useLanguages().views.main.model)

// 资源类型
const RESOURCE_TYPE = "live2d"

// 配置键名
const CONFIG_KEY = "selected_model"

// 各模型安装状态: id -> 是否已安装
const installedMap = ref<Record<string, boolean>>({})

// 当前使用 (selected_model 配置)
const selectedModel = ref("")

// 本地导入状态
const importing = ref(false)
const importStatusText = ref("")

// 本地导入 Live2D 模型
const importLocalModel = async () => {
	if (importing.value) return
	importing.value = true
	importStatusText.value = "正在选择并导入 Live2D 文件..."
	try {
		const imported = await invoke<string[] | null>("import_local_resource", {resourceType: "live2d"})
		if (imported && imported.length > 0) {
			importStatusText.value = `导入成功: ${imported.join(", ")}`
			await refreshStatus()
			setTimeout(() => {
				importStatusText.value = ""
			}, 3000)
		} else {
			importStatusText.value = ""
		}
	} catch (error) {
		console.error("导入模型失败:", error)
		importStatusText.value = `导入失败: ${String(error)}`
		setTimeout(() => {
			importStatusText.value = ""
		}, 4000)
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

// 动态检测各模型安装状态 (挂载 / 每次状态刷新后调用)
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
	const UNLISTEN = await listen<{key?: string}>("nori:config-changed", ({payload}) => {
		if (payload?.key === "l2d_click_interaction") void syncPreviewClickInteraction()
	})
	if (disposed) UNLISTEN()
	else unlistenConfigChanged = UNLISTEN
})

onBeforeUnmount(() => {
	disposed = true
	window.removeEventListener("resize", onWindowResize)
	unbindPreviewClick()
	unlistenConfigChanged?.()
	unlistenConfigChanged = null
	void PREVIEW.destroy()
})

// 点击模型卡: 展开/收起蒙版菜单 (仅已安装模型可操作, 未安装请通过导入添加)
const toggleCardMenu = (model: ModelInfo) => {
	if (adjustFor.value || !installedMap.value[model.id]) return
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
const previewClickInteraction = ref(true)
let unlistenConfigChanged: UnlistenFn | null = null
let disposed = false

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
	previewClickInteraction.value = (await readBehaviorConfig("l2d_click_interaction")) !== false
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
		unbindPreviewClick()
		await PREVIEW.destroy()
		await PREVIEW.mount(
			{directory: model.id, fileBase: resolveModelFileBase(model.id)},
			{container: showcaseRef.value, canvasWidth: Math.max(0, Math.round(RECT.width)), canvasHeight: Math.max(0, Math.round(RECT.height))}
		)
		PREVIEW.setUserScale(pvScale.value)
	} catch (error) {
		console.error("加载预览模型失败:", error)
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
	<section class="model-management">
		<div class="mm-head">
			<div class="mm-head-info">
				<h2 class="mm-title glow-teal">{{ I18N.title }}</h2>
				<p class="mm-sub">{{ I18N.sub }}</p>
			</div>
			<div class="mm-head-actions">
				<button class="btn-import" :disabled="importing" @click="importLocalModel">
					<Icon name="package" :size="16"/>
					<span>{{ importing ? "正在导入..." : "导入本地 Live2D" }}</span>
				</button>
			</div>
		</div>

		<div v-if="importStatusText" class="mm-import-status">{{ importStatusText }}</div>

		<!-- 下载进度条面板 -->

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
					<div class="thumb-glow-overlay"></div>
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
								class="mm-menu-btn btn-primary"
								:class="{enabled: selectedModel === model.id}"
								:disabled="!installedMap[model.id] || selectedModel === model.id"
								@click="enableModel(model)"
							>
								<Icon name="check" :size="13"/>
								<span>{{ selectedModel === model.id ? I18N.enabled : I18N.enable }}</span>
							</button>
							<button
								v-if="installedMap[model.id]"
								class="mm-menu-btn btn-ghost"
								:disabled="!installedMap[model.id]"
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
		<div class="mm-behavior-section glass-panel">
			<Live2dBehaviorControls :model-id="selectedModel"/>
		</div>

		<!-- 整页调整面板: 铺满整个页面, 左侧橱窗实时预览, 右侧控制 -->
		<Transition name="view">
			<div v-if="adjustFor" class="mm-fullpage">
				<button class="mm-back" :title="I18N.back" @click="closeAdjust">
					<Icon name="arrow-left" class="mm-back-icon"/>
				</button>

				<div ref="showcaseRef" class="mm-showcase"></div>

				<div class="mm-adjust-pane glass-panel">
					<AdjustControls
						:model-id="adjustFor"
						:model-name="modelNameOf(adjustFor)"
						:expression-enabled="true"
						:initial-scale="pvScale"
						:initial-expressions="previewExpressionList"
						@scale="onPreviewScale"
						@expressions="onPreviewExpressions"
					/>
					<div class="mm-behavior-divider"/>
					<Live2dBehaviorControls :model-id="adjustFor"/>
				</div>
			</div>
		</Transition>
	</section>
</template>

<style scoped lang="less">
.model-management {
	width: 100%;
	height: 100%;
	padding: 1.6rem 2.4rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 2rem;
	overflow-y: auto;
}

.mm-head {
	width: 100%;
	display: flex;
	align-items: center;
	justify-content: space-between;
	gap: 1.6rem;
}

.mm-head-info {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
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

.btn-import {
	display: inline-flex;
	align-items: center;
	gap: 0.7rem;
	padding: 0.85rem 1.6rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-strong);
	border-radius: var(--radius-sm);
	color: var(--nori-teal-bright);
	font-size: 1.25rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover:not(:disabled) {
		background: rgba(125, 227, 255, 0.18);
		box-shadow: 0 0 1.4rem var(--glow-teal-soft);
		transform: translateY(-0.15rem);
	}

	&:disabled {
		opacity: 0.6;
		cursor: not-allowed;
	}
}

.mm-import-status {
	width: 100%;
	padding: 0.8rem 1.2rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
	text-align: center;
}

.mm-grid {
	display: flex;
	flex-wrap: wrap;
	justify-content: center;
	gap: 2.4rem;
}

.mm-card {
	position: relative;
	padding: 1rem 1rem 1.2rem;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.9rem;
	border: 0.15rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: rgba(255, 255, 255, 0.03);
	cursor: default;
	transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		background: rgba(125, 227, 255, 0.08);
		border-color: var(--nori-teal-soft);
		transform: translateY(-0.25rem);
		box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.35), 0 0 1.2rem var(--glow-teal-soft);
	}

	&.active {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.12);
		box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.4), 0 0 1.8rem var(--glow-teal);
	}
}

.mm-thumb-wrap {
	width: 15.2rem;
	height: 25.2rem;
	overflow: hidden;
	border-radius: var(--radius-sm);
	background: rgba(0, 0, 0, 0.3);
	border: 0.1rem solid var(--line-subtle);
	position: relative;
}

.mm-thumb {
	width: 100%;
	height: 100%;
	object-fit: cover;
	display: block;
	transition: transform 0.3s ease;
}

.thumb-glow-overlay {
	position: absolute;
	inset: 0;
	background: linear-gradient(180deg, transparent 65%, rgba(5, 14, 26, 0.7) 100%);
	pointer-events: none;
}

.mm-name {
	font-size: 1.35rem;
	font-weight: 500;
	color: var(--text-primary);
}

.mm-tags {
	display: flex;
	gap: 0.6rem;
	align-items: center;
}

.mm-tag {
	padding: 0.25rem 0.8rem;
	border-radius: var(--radius-pill);
	font-size: 1.05rem;
	color: var(--text-faint);
	background: rgba(255, 255, 255, 0.06);
	white-space: nowrap;

	&.ok {
		color: var(--nori-teal);
		background: rgba(94, 234, 212, 0.12);
		border: 0.1rem solid rgba(94, 234, 212, 0.25);
	}

	&.current {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.15);
		border: 0.1rem solid var(--nori-teal);
		font-weight: 600;
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
	background: rgba(2, 10, 18, 0.75);
	backdrop-filter: blur(4px);
	border-radius: var(--radius-md);
	cursor: default;
}

.mm-card-menu {
	position: relative;
	width: 84%;
	display: flex;
	flex-direction: column;
	align-items: center;
	gap: 0.8rem;
	padding: 1.6rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	background: linear-gradient(160deg, #0e2a44 0%, var(--bg-abyss) 100%);
	box-shadow: 0 1rem 3rem rgba(0, 0, 0, 0.6), 0 0 1.4rem var(--glow-teal-soft);
}

.mm-menu-btn {
	width: 100%;
	padding: 0.75rem 0;
	font-size: 1.25rem;
	font-family: inherit;
	border-radius: var(--radius-sm);
	cursor: pointer;
	display: inline-flex;
	align-items: center;
	justify-content: center;
	gap: 0.6rem;

	&.enabled,
	&:disabled {
		color: var(--text-muted);
		opacity: 0.55;
		cursor: default;
		background: rgba(255, 255, 255, 0.04);
		border-color: var(--line-subtle);
		filter: none;
	}
}

// ---- 整页调整面板: 铺满页面 ----
.mm-fullpage {
	position: fixed;
	inset: 0;
	z-index: 100;
	display: flex;
	flex-direction: column;
	background: linear-gradient(165deg, rgba(8, 20, 32, 0.97) 0%, rgba(2, 10, 18, 0.98) 100%);
	backdrop-filter: blur(1.2rem);
	cursor: default;
}

.mm-back {
	position: absolute;
	top: 1.6rem;
	left: 2rem;
	z-index: 3;
	width: 3.8rem;
	height: 3.8rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: 50%;
	background: rgba(255, 255, 255, 0.05);
	color: var(--text-body);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.12);
		box-shadow: 0 0 1.4rem var(--glow-teal-soft);
		transform: translateY(-0.1rem);
	}
}

.mm-back-icon {
	width: 1.8rem;
	height: 1.8rem;
}

.mm-showcase {
	position: absolute;
	top: 5.6rem;
	left: 2.4rem;
	width: 34rem;
	height: 50rem;
	z-index: 201;
}

.mm-adjust-pane {
	position: absolute;
	top: 5.6rem;
	left: 40rem;
	right: 2.4rem;
	bottom: 2rem;
	overflow-y: auto;
	padding: 1.6rem 2rem;
}

.mm-behavior-section {
	width: 100%;
	max-width: 60rem;
	margin: 0 auto;
	padding: 1.6rem 2.4rem;
}

.mm-behavior-divider {
	height: 0.1rem;
	background: var(--line-subtle);
	margin: 1.6rem 0;
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
