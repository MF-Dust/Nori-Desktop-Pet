/**
 * 画布布局参数
 */
export interface CanvasLayoutOptions {
	/**
	 * 画布所在层级 (调整模式需高于遮罩层)
	 */
	zIndex: string

	/**
	 * 模型缩放
	 */
	scale: number

	/**
	 * 模型水平位移 (px)
	 */
	offsetX: number

	/**
	 * 模型垂直位移 (px)
	 */
	offsetY: number

	/**
	 * 布局属性过渡 (收回/展开动画), transform 始终即时生效
	 */
	animate: boolean

	/**
	 * 画布相对容器内缩距离 (px), 让橱窗边框/标题可见
	 */
	inset?: number
}

/**
 * 将库创建的 body 级画布布局到指定容器内 (无容器时铺满窗口)
 *
 * 布局属性带过渡动画, transform (缩放/位移) 即时生效
 */
export const applyCanvasLayout = (
	canvas: HTMLCanvasElement | null,
	box: HTMLElement | null | undefined,
	options: CanvasLayoutOptions
): void => {
	if (!canvas) return
	canvas.style.position = "fixed"
	canvas.style.pointerEvents = "auto"
	canvas.style.transformOrigin = "bottom center"

	if (box) {
		const RECT = box.getBoundingClientRect()
		const INSET = options.inset ?? 0
		canvas.style.left = `${RECT.left + INSET}px`
		canvas.style.top = `${RECT.top + INSET}px`
		canvas.style.width = `${Math.max(0, RECT.width - INSET * 2)}px`
		canvas.style.height = `${Math.max(0, RECT.height - INSET * 2)}px`
		canvas.style.zIndex = options.zIndex
	} else {
		canvas.style.left = "0"
		canvas.style.top = "0"
		canvas.style.width = "100%"
		canvas.style.height = "100%"
		canvas.style.zIndex = "auto"
	}

	canvas.style.transform = `scale(${options.scale}) translate(${options.offsetX}px, ${options.offsetY}px)`
	canvas.style.opacity = "1"

	if (options.animate) {
		const EASE = "0.42s cubic-bezier(0.4, 0, 0.2, 1)"
		canvas.style.transition = `left ${EASE}, top ${EASE}, width ${EASE}, height ${EASE}, opacity 0.3s ease`
	} else {
		canvas.style.transition = "none"
	}
}
