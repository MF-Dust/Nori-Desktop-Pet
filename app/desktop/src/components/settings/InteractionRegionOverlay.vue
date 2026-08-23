<script setup lang="ts">
import {computed, onBeforeUnmount, ref} from "vue"
import useLanguages from "../../services/i18n/useLanguages"
import {
	clampNormalizedRect,
	containerToModelNormalizedPoint,
	modelNormalizedToContainerRect,
	normalizeRectFromPoints,
	type Point2D,
	type ViewportPixelRect,
} from "../../services/live2d/interactions"
import type {
	InteractionRect,
	InteractionRegion,
} from "../../services/runtime/types"

const props = withDefaults(defineProps<{
	regions: InteractionRegion[]
	selectedId?: string | null
	modelViewport?: ViewportPixelRect | null
	editing?: boolean
	creating?: boolean
}>(), {
	selectedId: null,
	modelViewport: null,
	editing: false,
	creating: false,
})

const emit = defineEmits<{
	"update:selectedId": [id: string | null]
	"update:regions": [regions: InteractionRegion[]]
	"update:creating": [creating: boolean]
	"createRegion": [rect: InteractionRect]
	"deleteRegion": [id: string]
	"regionClick": [region: InteractionRegion]
}>()

const I18N = computed(() => useLanguages().views.main.model.interactions)

const overlayRef = ref<HTMLElement>()

// ---- 交互操作状态机 ----
type DragMode = "none" | "create" | "move" | "resize"
const dragMode = ref<DragMode>("none")
const resizeHandle = ref<"nw" | "ne" | "se" | "sw" | "n" | "s" | "e" | "w" | null>(null)

// 拖拽起始与中间数据 (归一化模型坐标)
const dragStartNorm = ref<Point2D>({x: 0, y: 0})
const dragCurrentNorm = ref<Point2D>({x: 0, y: 0})
const moveOffsetNorm = ref<Point2D>({x: 0, y: 0})
const initialRegionRect = ref<InteractionRect | null>(null)

// 新建矩形临时草稿
const draftNormRect = ref<InteractionRect | null>(null)

// 像素转 rem 工具 (1 DIP px = 0.1 rem)
const pxToRem = (px: number): string => `${(px * 0.1).toFixed(3)}rem`

// 模型视口像素矩形在容器内的 rem 样式
const viewportStyle = computed(() => {
	const vp = props.modelViewport
	if (!vp) return {display: "none"}
	return {
		left: pxToRem(vp.left),
		top: pxToRem(vp.top),
		width: pxToRem(vp.width),
		height: pxToRem(vp.height),
	}
})

// 计算各区域在容器内的像素矩形映射
const renderedRegions = computed(() => {
	const vp = props.modelViewport
	if (!vp || vp.width <= 0 || vp.height <= 0) return []

	return props.regions.map(region => {
		const pixelRect = modelNormalizedToContainerRect(region.rect, vp)
		const isSelected = region.id === props.selectedId
		return {
			region,
			pixelRect,
			isSelected,
			style: {
				left: pxToRem(pixelRect.left),
				top: pxToRem(pixelRect.top),
				width: pxToRem(pixelRect.width),
				height: pxToRem(pixelRect.height),
			},
		}
	})
})

// 草稿矩形在容器内的 rem 样式
const draftBoxStyle = computed(() => {
	const vp = props.modelViewport
	const draft = draftNormRect.value
	if (!vp || !draft) return {display: "none"}
	const pixelRect = modelNormalizedToContainerRect(draft, vp)
	return {
		left: pxToRem(pixelRect.left),
		top: pxToRem(pixelRect.top),
		width: pxToRem(pixelRect.width),
		height: pxToRem(pixelRect.height),
	}
})

// 鼠标位置转换为模型归一化坐标
const pointerToNorm = (event: MouseEvent | PointerEvent): Point2D | null => {
	const vp = props.modelViewport
	const overlayEl = overlayRef.value
	if (!vp || !overlayEl) return null
	const rect = overlayEl.getBoundingClientRect()
	const containerX = event.clientX - rect.left
	const containerY = event.clientY - rect.top
	return containerToModelNormalizedPoint(containerX, containerY, vp)
}

// 选中区域
const selectRegion = (id: string | null) => {
	emit("update:selectedId", id)
}

// ---- 指针事件分发 ----

// 背景点击 / 准备创建
const onBackgroundPointerDown = (event: PointerEvent) => {
	if (!props.editing) return
	if (event.button !== 0) return

	const normPoint = pointerToNorm(event)
	if (!normPoint) return

	// 若处于创建模式或在空白处拖拽
	selectRegion(null)
	dragMode.value = "create"
	dragStartNorm.value = normPoint
	dragCurrentNorm.value = normPoint
	draftNormRect.value = normalizeRectFromPoints(normPoint, normPoint)

	window.addEventListener("pointermove", onGlobalPointerMove)
	window.addEventListener("pointerup", onGlobalPointerUp)
}

// 区域本体指针按下 (移动模式或非编辑模式点击)
const onRegionPointerDown = (region: InteractionRegion, event: PointerEvent) => {
	if (!props.editing) {
		emit("regionClick", region)
		return
	}
	if (event.button !== 0) return
	event.stopPropagation()

	selectRegion(region.id)
	const normPoint = pointerToNorm(event)
	if (!normPoint) return

	dragMode.value = "move"
	dragStartNorm.value = normPoint
	initialRegionRect.value = {...region.rect}
	moveOffsetNorm.value = {
		x: normPoint.x - region.rect.x,
		y: normPoint.y - region.rect.y,
	}

	window.addEventListener("pointermove", onGlobalPointerMove)
	window.addEventListener("pointerup", onGlobalPointerUp)
}

// 控制手柄指针按下 (缩放模式)
const onHandlePointerDown = (
	handle: "nw" | "ne" | "se" | "sw" | "n" | "s" | "e" | "w",
	region: InteractionRegion,
	event: PointerEvent,
) => {
	if (!props.editing || event.button !== 0) return
	event.stopPropagation()

	const normPoint = pointerToNorm(event)
	if (!normPoint) return

	dragMode.value = "resize"
	resizeHandle.value = handle
	dragStartNorm.value = normPoint
	initialRegionRect.value = {...region.rect}

	window.addEventListener("pointermove", onGlobalPointerMove)
	window.addEventListener("pointerup", onGlobalPointerUp)
}

// 全局指针移动
const onGlobalPointerMove = (event: PointerEvent) => {
	if (dragMode.value === "none") return
	const normPoint = pointerToNorm(event)
	if (!normPoint) return

	if (dragMode.value === "create") {
		dragCurrentNorm.value = normPoint
		draftNormRect.value = normalizeRectFromPoints(dragStartNorm.value, normPoint)
		return
	}

	const selectedRegion = props.regions.find(r => r.id === props.selectedId)
	const initRect = initialRegionRect.value
	if (!selectedRegion || !initRect) return

	if (dragMode.value === "move") {
		const targetX = normPoint.x - moveOffsetNorm.value.x
		const targetY = normPoint.y - moveOffsetNorm.value.y
		const clampedX = Math.max(0, Math.min(1 - initRect.width, targetX))
		const clampedY = Math.max(0, Math.min(1 - initRect.height, targetY))

		const updated = props.regions.map(r =>
			r.id === selectedRegion.id
				? {...r, rect: clampNormalizedRect({...r.rect, x: clampedX, y: clampedY})}
				: r
		)
		emit("update:regions", updated)
		return
	}

	if (dragMode.value === "resize" && resizeHandle.value) {
		const handle = resizeHandle.value
		let x1 = initRect.x
		let y1 = initRect.y
		let x2 = initRect.x + initRect.width
		let y2 = initRect.y + initRect.height

		if (handle.includes("w")) x1 = Math.min(x2 - 0.02, normPoint.x)
		if (handle.includes("e")) x2 = Math.max(x1 + 0.02, normPoint.x)
		if (handle.includes("n")) y1 = Math.min(y2 - 0.02, normPoint.y)
		if (handle.includes("s")) y2 = Math.max(y1 + 0.02, normPoint.y)

		const newRect = clampNormalizedRect({
			x: x1,
			y: y1,
			width: x2 - x1,
			height: y2 - y1,
		})

		const updated = props.regions.map(r =>
			r.id === selectedRegion.id
				? {...r, rect: newRect}
				: r
		)
		emit("update:regions", updated)
	}
}

// 全局指针释放
const onGlobalPointerUp = () => {
	if (dragMode.value === "create" && draftNormRect.value) {
		const draft = draftNormRect.value
		// 面积微小过滤 (避免误触创建垃圾区域)
		if (draft.width >= 0.02 && draft.height >= 0.02) {
			emit("createRegion", draft)
			emit("update:creating", false)
		}
	}

	dragMode.value = "none"
	resizeHandle.value = null
	draftNormRect.value = null
	initialRegionRect.value = null

	window.removeEventListener("pointermove", onGlobalPointerMove)
	window.removeEventListener("pointerup", onGlobalPointerUp)
}

// 键盘微调与删除支持
const onKeyDown = (event: KeyboardEvent) => {
	if (!props.editing || !props.selectedId) return

	if (event.key === "Delete" || event.key === "Backspace") {
		event.preventDefault()
		emit("deleteRegion", props.selectedId)
		return
	}

	if (event.key === "Escape") {
		event.preventDefault()
		selectRegion(null)
		return
	}

	const selectedRegion = props.regions.find(r => r.id === props.selectedId)
	if (!selectedRegion) return

	const step = event.shiftKey ? 0.05 : 0.01
	let dx = 0
	let dy = 0

	if (event.key === "ArrowLeft") dx = -step
	else if (event.key === "ArrowRight") dx = step
	else if (event.key === "ArrowUp") dy = -step
	else if (event.key === "ArrowDown") dy = step
	else return

	event.preventDefault()
	const targetX = selectedRegion.rect.x + dx
	const targetY = selectedRegion.rect.y + dy
	const newRect = clampNormalizedRect({
		...selectedRegion.rect,
		x: Math.max(0, Math.min(1 - selectedRegion.rect.width, targetX)),
		y: Math.max(0, Math.min(1 - selectedRegion.rect.height, targetY)),
	})

	const updated = props.regions.map(r =>
		r.id === selectedRegion.id ? {...r, rect: newRect} : r
	)
	emit("update:regions", updated)
}

onBeforeUnmount(() => {
	window.removeEventListener("pointermove", onGlobalPointerMove)
	window.removeEventListener("pointerup", onGlobalPointerUp)
})
</script>

<template>
	<div
		ref="overlayRef"
		class="absolute inset-0 select-none overflow-hidden"
		:class="editing ? 'pointer-events-auto cursor-crosshair' : 'pointer-events-none'"
		tabindex="0"
		:aria-label="I18N.title"
		@pointerdown="onBackgroundPointerDown"
		@keydown="onKeyDown"
	>
		<!-- 模型物理画布边界参考框 (仅编辑模式显示) -->
		<div
			v-if="editing && modelViewport"
			class="absolute border border-dashed border-nori-teal-bright/35 rounded-sm pointer-events-none shadow-[0_0_1rem_rgba(125,227,255,0.06)]"
			:style="viewportStyle"
		>
			<span class="absolute -top-[1.8rem] left-1 px-1.5 py-0.5 rounded-xs text-xs font-500 text-nori-teal-bright/80 bg-bg-abyss/80 border border-nori-teal-soft/40 backdrop-blur-[0.4rem]">
				{{ I18N.title }}
			</span>
		</div>

		<!-- 各交互区域矩形 -->
		<div
			v-for="item in renderedRegions"
			:key="item.region.id"
			class="absolute group transition-shadow duration-150"
			:class="[
				editing ? 'pointer-events-auto cursor-move' : 'pointer-events-auto cursor-pointer',
				item.isSelected
					? 'z-20 border-2 border-nori-teal-bright bg-nori-teal-bright/22 shadow-[0_0_1.6rem_var(--glow-teal)]'
					: 'z-10 border border-nori-teal-soft/75 bg-nori-teal-bright/10 hover:(bg-nori-teal-bright/16 border-nori-teal-bright shadow-[0_0_1rem_var(--glow-teal-soft)])'
			]"
			:style="item.style"
			@pointerdown="onRegionPointerDown(item.region, $event)"
		>
			<!-- 区域标签 (名称与模式) -->
			<div class="absolute -top-[2.2rem] left-0 flex items-center gap-1 pointer-events-none whitespace-nowrap">
				<span
					class="px-2 py-0.5 rounded-xs text-xs font-600 border backdrop-blur-[0.6rem] shadow-sm flex items-center gap-1"
					:class="item.isSelected
						? 'bg-nori-teal-bright text-on-teal border-nori-teal-bright shadow-[0_0_0.8rem_var(--glow-teal)]'
						: 'bg-bg-abyss/90 text-text-primary border-line-subtle group-hover:border-nori-teal-soft'"
				>
					<span>{{ item.region.name || I18N.defaultRegionName }}</span>
					<span
						class="text-xs px-1 py-0.2 rounded-pill font-600"
						:class="item.region.reactionMode === 'ai'
							? 'bg-purple-500/25 text-purple-200 border border-purple-400/40'
							: 'bg-nori-teal-soft/30 text-nori-teal-bright border border-nori-teal-soft/40'"
					>
						{{ item.region.reactionMode === 'ai' ? I18N.modeAi : I18N.modeLocal }}
					</span>
				</span>
			</div>

			<!-- 选中的缩放控制手柄 (8个方向) -->
			<template v-if="editing && item.isSelected">
				<!-- 四角手柄 -->
				<button
					type="button"
					class="absolute -top-[0.5rem] -left-[0.5rem] w-[1rem] h-[1rem] rounded-xs bg-white border-2 border-nori-teal-bright cursor-nwse-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('nw', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute -top-[0.5rem] -right-[0.5rem] w-[1rem] h-[1rem] rounded-xs bg-white border-2 border-nori-teal-bright cursor-nesw-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('ne', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute -bottom-[0.5rem] -right-[0.5rem] w-[1rem] h-[1rem] rounded-xs bg-white border-2 border-nori-teal-bright cursor-nwse-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('se', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute -bottom-[0.5rem] -left-[0.5rem] w-[1rem] h-[1rem] rounded-xs bg-white border-2 border-nori-teal-bright cursor-nesw-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('sw', item.region, $event)"
				/>

				<!-- 四边中点手柄 -->
				<button
					type="button"
					class="absolute -top-[0.4rem] left-1/2 -translate-x-1/2 w-[1.2rem] h-[0.8rem] rounded-xs bg-white border border-nori-teal-bright cursor-ns-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('n', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute -bottom-[0.4rem] left-1/2 -translate-x-1/2 w-[1.2rem] h-[0.8rem] rounded-xs bg-white border border-nori-teal-bright cursor-ns-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('s', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute top-1/2 -translate-y-1/2 -left-[0.4rem] w-[0.8rem] h-[1.2rem] rounded-xs bg-white border border-nori-teal-bright cursor-ew-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('w', item.region, $event)"
				/>
				<button
					type="button"
					class="absolute top-1/2 -translate-y-1/2 -right-[0.4rem] w-[0.8rem] h-[1.2rem] rounded-xs bg-white border border-nori-teal-bright cursor-ew-resize shadow-md focus-ring"
					:aria-label="I18N.editMode"
					@pointerdown="onHandlePointerDown('e', item.region, $event)"
				/>
			</template>
		</div>

		<!-- 拖拽创建过程中的草稿虚线框 -->
		<div
			v-if="dragMode === 'create' && draftNormRect"
			class="absolute border-2 border-dashed border-nori-teal-bright bg-nori-teal-bright/25 rounded-xs pointer-events-none shadow-[0_0_1.4rem_var(--glow-teal)]"
			:style="draftBoxStyle"
		/>
	</div>
</template>
