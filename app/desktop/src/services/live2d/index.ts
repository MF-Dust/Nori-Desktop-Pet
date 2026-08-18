/**
 * Live2D 控制 Service: 对 `live2d-easy-control` 的封装, 统一管理加载、渲染、
 * 交互与消息气泡, 并把"加载模型 / 操作模型"的细节从视图解耦.
 *
 * 模型文件由 Tauri 资源管理器下载到 `data/live2d/<name>/`, 通过 `asset` 协议
 * 被 `live2d-easy-control` 以 fetch 方式读取. 详细路径约定见 `./config.ts`.
 */

import {
	load,
	setPointMovedEvent,
	removePointMovedEvent,
	setPointClickEvent,
	removePointClickEvent,
	getAllMotionsInfo,
	getAllExpressionsInfo,
	playMotion,
	playExpression,
	stopExpression,
	setAngle,
	setAngleXY,
	reSetAngle,
	setMessage,
	hideMessageBox,
	setLipSync,
	stop,
} from "live2d-easy-control"
import {ASSET_ORIGIN, RESOURCE_SDK, SDK_NAME} from "./config"
import {createResourceDownload} from "../resourceDownload"

/**
 * 模型描述: 目录名 + 模型清单基础名.
 * live2d-easy-control 约定模型存储结构为 `<同名目录>/<同名>.<extends>`, 而我们的
 * 运行时目录 `data/live2d/<name>/` 是扁平存放的, 由 `asset` 协议对 ID 目录层做折叠适配.
 */
export interface Live2DModelSpec {
	/** 模型在 `live2d` 下的运行时目录名, 如 `arg-nori` */
	directory: string
	/** 模型清单基础名 (不含扩展名), 如 `ARGNori`, 对应磁盘 `ARGNori.model3.json` */
	fileBase: string
}

/** 生命周期 / 交互选项. */
export interface Live2DLoadOptions {
	/** 是否开启模型随鼠标的眼神追踪 (仅画布内) */
	enableIdleTracking?: boolean
	/** 是否开启"点击模型触发随机表情/动作" */
	enableClickTap?: boolean
}

/**
 * 组装给 `live2d-easy-control` 的加载配置.
 * 库内部用 `resourcesPath + modelDir + "/"` 拼接模型目录, 再 fetch `modelDir + ".model3.json"`.
 */
const buildLoadConfig = (model: Live2DModelSpec): Record<string, unknown> => ({
	modelDir: model.fileBase,
	// 指向模型所在运行时目录的首段 (经 asset 协议访问).
	// 例: http://asset.localhost/live2d/arg-nori/
	resourcesPath: `${ASSET_ORIGIN}/live2d/${model.directory}/`,
	canvasSize: "auto",
	canvasWidth: "100%",
	canvasHeight: "100%",
	// SDK 本身已内置在 live2d-easy-control 中; 若未来改用外部 SDK 可在此指向 data/live2d-sdk
})

/** 创建一个 Live2D 控制器实例. */
export const createLive2D = () => {
	const sdkDownload = createResourceDownload()

	/** 加载并渲染一个 Live2D 模型, 完成后默认开启眼神追踪与点击交互. */
	const mount = async (
		model: Live2DModelSpec,
		options: Live2DLoadOptions = {},
	): Promise<void> => {
		// 渲染前确保 Cubism SDK 已就位. live2d-easy-control 已内置 SDK, 因此这里仅
		// 探查资源是否存在 (不发起下载, 避免初始化时 API 404 阻塞渲染);
		// 真正的 SDK 下载由软件初始化流程调用 ensureSdk() 负责.
		await sdkDownload.check(RESOURCE_SDK, SDK_NAME)
		await load(buildLoadConfig(model))

		if (options.enableIdleTracking !== false) await setPointMovedEvent()
		if (options.enableClickTap !== false) await setPointClickEvent()
	}

	/** 释放渲染与所有事件监听. */
	const destroy = async (): Promise<void> => {
		try {
			await stop()
		} catch {
			/* 未加载时忽略 */
		}
	}

	return {
		/** 加载并渲染模型 (含 SDK 就位检查与默认交互). */
		mount,
		/** 仅确保 Cubism SDK 资源就位. */
		ensureSdk: () => sdkDownload.ensure(RESOURCE_SDK, SDK_NAME),
		/** SDK 下载状态 (供进度展示). */
		state: sdkDownload.state,
		/** 释放渲染与事件监听. */
		destroy,
		// ---- 鼠标交互事件 ----
		/** 开启模型随鼠标眼神 (画布内有效). */
		enableIdleTracking: () => setPointMovedEvent(),
		/** 关闭随鼠标眼神. */
		disableIdleTracking: () => removePointMovedEvent(),
		/** 开启点击随机切换表情/动作. */
		enableClickTap: () => setPointClickEvent(),
		/** 关闭点击事件. */
		disableClickTap: () => removePointClickEvent(),
		/** 按鼠标事件绘制视线方向 (接给 mousemove). */
		setAngle: (e: MouseEvent, duration?: number) => setAngle(e, duration),
		/** 按相对画布的 XY 坐标绘制视线方向. */
		setAngleXY: (x: number, y: number, duration?: number) => setAngleXY(x, y, duration),
		/** 恢复默认朝向. */
		resetAngle: () => reSetAngle(),
		// ---- 动作 / 表情 ----
		/** 播放动作: group 如 "Reactions", no 组内序号, priority 0-3. */
		playMotion: (group: string, no: number, priority = 2) => playMotion(group, no, priority),
		/** 播放表情 (按模型 expressions 名称). */
		playExpression: (name: string) => playExpression(name),
		/** 停止表情. */
		stopExpression: () => stopExpression(),
		/** 列出可用动作组. */
		listMotions: () => getAllMotionsInfo(),
		/** 列出可用表情. */
		listExpressions: () => getAllExpressionsInfo(),
		// ---- 口型 / 对话 ----
		/** 设置嘴型大小 (0-1, 常用于音乐/语音联动). */
		setLipSync: (value: number) => setLipSync(value),
		/** 显示消息气泡. */
		say: (message: string, duration?: number) => setMessage(message, duration),
		/** 隐藏消息气泡. */
		hideMessage: () => hideMessageBox(),
	}
}

export type Live2DController = ReturnType<typeof createLive2D>
