/**
 * 测试环境初始化
 *
 * config.ts 在模块加载时读取 window.__nori 计算资产基址,
 * Node 环境没有 window, 这里提供一个空对象让纯函数可导入。
 */
Object.defineProperty(globalThis, "window", {
	value: {},
	writable: true,
	configurable: true,
})
