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
 * 4. loadAssets 加载失败时标记 _loadFailed, 且 waiting() 轮询在失败时
 *    reject → 模型资源加载失败时 load() 会真正抛错, 而不是永久挂起
 *    (库原实现: fetch 失败被 catch 吞掉, waiting() 无限轮询 getLoadState,
 *     load() 的 Promise 永不 settle, 桌宠窗口会永远空白且无法重试)
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

// ---- 补丁 4: 模型资源加载失败时让 load() reject, 避免永久挂起 ----
// 库的 loadAssets() 把 fetch 失败吞进 catch, 仅打印日志;
// 而 waiting() 会 10ms 轮询 getLoadState() 直到状态为 22, 永不失败。
// 结果: model3.json 加载失败时 load() 的 Promise 永远不 settle,
// 上层 await load() 永久卡住, 桌宠窗口空白且无法重试。
const LOAD_ASSETS_OLD = `}).catch((e) => {
      F(` + "`Failed to load file ${this._modelHomeDir}.model3.json`" + `);
    });`

const LOAD_ASSETS_NEW = `}).catch((e) => {
      F(` + "`Failed to load file ${this._modelHomeDir}.model3.json`" + `);
      this._loadFailed = !0;
    });`

const WAITING_OLD = `  waiting() {
    return new Promise((t) => {
      const e = () => {
        this._model.getLoadState() ? t() : setTimeout(e, 10);
      };
      e();
    });
  }`

const WAITING_NEW = `  waiting() {
    return new Promise((t, r) => {
      let n = 0;
      const e = () => {
        if (this._model.getLoadState()) t();
        else if (this._model._loadFailed) r(new Error("模型资源加载失败"));
        else if (++n > 6000) r(new Error("模型加载超时 (60s)"));
        else setTimeout(e, 10);
      };
      e();
    });
  }`

if (source.includes(LOAD_ASSETS_OLD)) {
	source = source.replace(LOAD_ASSETS_OLD, LOAD_ASSETS_NEW)
	changed = true
	console.log("[patch-live2d] 补丁 4a (loadAssets 失败标记) 已应用")
} else {
	console.log("[patch-live2d] 补丁 4a 已存在或已失效, 跳过")
}

if (source.includes(WAITING_OLD)) {
	source = source.replace(WAITING_OLD, WAITING_NEW)
	changed = true
	console.log("[patch-live2d] 补丁 4b (waiting 失败即 reject) 已应用")
} else {
	console.log("[patch-live2d] 补丁 4b 已存在或已失效, 跳过")
}

// ---- 补丁 5: 优先使用本地 live2dcubismcore.min.js, 避免外网 403 / 断网 / 卡死 ----
const SCRIPT_LOADER_OLD = `const _a = () => new Promise((r) => {
  const t = document.createElement("script");
  t.src = "https://cubism.live2d.com/sdk-web/cubismcore/live2dcubismcore.min.js", t.async = !0, t.onload = () => r(), document.head.appendChild(t);
})`

const SCRIPT_LOADER_NEW = `const _a = () => new Promise((r) => {
  if (typeof window !== "undefined" && window.Live2DCubismCore) {
    r();
    return;
  }
  const t = document.createElement("script");
  t.src = "/live2dcubismcore.min.js", t.async = !0, t.onload = () => r(), t.onerror = () => r(), document.head.appendChild(t);
})`

if (source.includes(SCRIPT_LOADER_OLD)) {
	source = source.replace(SCRIPT_LOADER_OLD, SCRIPT_LOADER_NEW)
	changed = true
	console.log("[patch-live2d] 补丁 5 (本地 Live2DCubismCore 加载) 已应用")
} else {
	console.log("[patch-live2d] 补丁 5 已存在或已失效, 跳过")
}

if (changed) writeFileSync(TARGET, source)
