/**
 * Live2D 资源路径与命名常量.
 *
 * 模型 / SDK 都由 Tauri 资源管理器下载到运行期 `data` 目录, 再由自定义 `asset`
 * 协议 serve 给 frontend (Rust 侧 `src/asset.rs` 注册). 这里集中维护路径约定,
 * 避免在调用处散落魔法字符串, 也便于未来替换协议或模型源.
 */

/**
 * 资源协议名 (与 Rust `asset::SCHEME` 保持一致).
 */
export const ASSET_SCHEME = "asset"

/**
 * 资源协议基础 URL, 按平台适配:
 * - Windows / Android:  `http://asset.localhost/<相对路径>`
 * - macOS / iOS / Linux: `asset://localhost/<相对路径>`
 */
export const ASSET_ORIGIN = /Windows|Win/i.test(navigator.userAgent)
	? `http://${ASSET_SCHEME}.localhost`
	: `${ASSET_SCHEME}://localhost`

/**
 * 由相对 data 目录的路径构造可 fetch 的 `asset` URL.
 * @example assetUrl("live2d/arg-nori/ARGNori.model3.json")
 */
export const assetUrl = (relativePath: string): string =>
	`${ASSET_ORIGIN}/${relativePath.replace(/^\/+/, "")}`

/**
 * Live2D 模型资源类型 (与 Rust `ResourceType::Live2D.dir_name()` 对应目录名).
 */
export const RESOURCE_LIVE2D = "live2d"

/**
 * Cubism SDK 资源类型 (与 Rust `ResourceType::Live2DSdk` 对应, 目录 `live2d-sdk`).
 */
export const RESOURCE_SDK = "live2dsdk"

/**
 * SDK 在 `data/live2d-sdk` 下的子目录名, 作为 ensure/check_resource 的 `name`.
 */
export const SDK_NAME = "sdk"

/**
 * SDK 在 data 目录下的相对路径 (供后续自行用 Cubism API 渲染时引用).
 */
export const sdkDir = (): string => `live2d-sdk/${SDK_NAME}`

/**
 * 内置 Live2D 模型清单: 目录名 → 模型清单基础名 (`<fileBase>.model3.json`).
 * 主目录名从配置 `selected_model` 读取, 模型文件基础名由 `asset` 协议折叠适配.
 * 后续新增模型时在此登记即可.
 */
export const defaultModels: Record<string, string> = {
	"arg-nori": "ARGNori",
}

/** 由模型目录名解析其模型清单基础名; 未知模型回退为目录名本身. */
export const resolveModelFileBase = (directory: string): string =>
	defaultModels[directory] ?? directory
