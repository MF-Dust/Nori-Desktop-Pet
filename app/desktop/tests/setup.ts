/**
 * 测试环境初始化
 *
 * Live2D 资产适配器在模块加载时读取宿主资产基址,
 * Node 环境没有 window, 这里提供一个空对象让纯函数可导入。
 */
Object.defineProperty(globalThis, "window", {
	value: {},
	writable: true,
	configurable: true,
})
