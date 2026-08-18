/**
 * live2d-easy-control 补丁脚本 (幂等, 可在 pnpm install 后重复执行)
 *
 * 问题: live2d-easy-control 底层 ExpressionManager 支持多个表情同时叠加,
 * 但库里有一段清理逻辑会在新表情淡入完成后移除队列中的旧表情,
 * 导致同时只能显示一个表情。
 *
 * 补丁内容:
 * 1. 移除 updateMotion 中的旧表情清理逻辑 → 允许多个表情同时叠加显示
 * 2. 强化 stopAllExpressions → 停止前把已应用的表情参数还原为基础值,
 *    避免取消表情后参数残留 (嘴形/眼睛等被表情覆盖的参数冻结)
 * 3. WebGL 上下文开启 preserveDrawingBuffer → 支持读取画布像素
 *    测量模型实际可视范围 (桌宠窗口智能适配模型大小)
 */
import {readFileSync, writeFileSync, existsSync} from "node:fs"
import {dirname, resolve} from "node:path"
import {fileURLToPath} from "node:url"

const TARGET = resolve(dirname(fileURLToPath(import.meta.url)), "../node_modules/live2d-easy-control/live2dEasyControl.js")

if (!existsSync(TARGET)) {
	console.error(`[patch-live2d] 找不到库文件: ${TARGET}`)
	process.exit(1)
}

let source = readFileSync(TARGET, "utf8")

// ---- 补丁 1: 移除旧表情清理逻辑 ----
const CLEANUP_OLD = `    if (s.getSize() > 1 && this.getFadeWeight(
      this._fadeWeights.getSize() - 1
    ) >= 1)
      for (let o = s.getSize() - 2; o >= 0; --o) {
        const u = s.at(o);
        xt(u), s.remove(o), this._fadeWeights.remove(o);
      }`

const CLEANUP_NEW = `    // [patch-live2d] 保留所有已选表情, 允许多个表情同时叠加显示`

// ---- 补丁 2: 停止表情时还原参数基础值 ----
const STOP_OLD = `  stopAllExpressions() {
    this._expressionManager != null && this._expressionManager.stopAllMotions();
  }`

const STOP_NEW = `  stopAllExpressions() {
    if (this._expressionManager == null) return;
    // [patch-live2d] 停止前把已应用的表情参数还原为基础值, 避免表情残留
    const values = this._expressionManager._expressionParameterValues;
    if (values) {
      for (let i = 0; i < values.getSize(); i++) {
        const p = values.at(i);
        if (p != null && p.parameterId != null) this._model.setParameterValueById(p.parameterId, p.overwriteValue, 1);
      }
    }
    this._expressionManager.stopAllMotions();
  }`

// ---- 补丁 3: WebGL 上下文开启 preserveDrawingBuffer ----
// 库创建上下文时不传任何属性, preserveDrawingBuffer 默认为 false,
// 合成后无法通过 readPixels 读取画布像素
const GL_OLD = `getContext("webgl2")`
const GL_NEW = `getContext("webgl2", { preserveDrawingBuffer: true })`

let changed = false

if (source.includes(CLEANUP_OLD)) {
	source = source.replace(CLEANUP_OLD, CLEANUP_NEW)
	changed = true
	console.log("[patch-live2d] 补丁 1 (多表情叠加) 已应用")
} else {
	console.log("[patch-live2d] 补丁 1 已存在或已失效, 跳过")
}

if (source.includes(STOP_OLD)) {
	source = source.replace(STOP_OLD, STOP_NEW)
	changed = true
	console.log("[patch-live2d] 补丁 2 (参数还原) 已应用")
} else {
	console.log("[patch-live2d] 补丁 2 已存在或已失效, 跳过")
}

// 补丁 3 需对所有出现处应用
const GL_COUNT = source.split(GL_OLD).length - 1
if (GL_COUNT > 0) {
	source = source.split(GL_OLD).join(GL_NEW)
	changed = true
	console.log(`[patch-live2d] 补丁 3 (preserveDrawingBuffer) 已应用 (${GL_COUNT} 处)`)
} else {
	console.log("[patch-live2d] 补丁 3 已存在或已失效, 跳过")
}

if (changed) writeFileSync(TARGET, source)
