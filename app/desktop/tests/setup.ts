/**
 * 测试环境初始化
 *
 * Live2D 资产适配器在模块加载时读取宿主资产基址,
 * Node 环境没有 window, 这里提供一个空对象让纯函数可导入。
 *
 * 注意: 只在确实缺少 window 时才补 —— jsdom 环境下的真实 window 不能被覆盖,
 * 否则 DOMPurify 之类依赖真实 DOM 的库会拿不到 document。
 */
if (typeof (globalThis as {window?: unknown}).window === "undefined") {
	Object.defineProperty(globalThis, "window", {
		value: {},
		writable: true,
		configurable: true,
	})
}

if (typeof (window as any).Live2DCubismCore === "undefined") {
	(window as any).Live2DCubismCore = {}
}
