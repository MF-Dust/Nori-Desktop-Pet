/** 前端构建版本；运行时快照提供更权威的宿主版本。默认回退为 Dev。 */
const BUILT_VERSION = import.meta.env.VITE_APP_VERSION?.trim()
export const APP_VERSION = BUILT_VERSION || "Dev"
