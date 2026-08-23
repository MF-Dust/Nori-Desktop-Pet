import type {TelemetryState} from "../runtime"

/** Web 采样策略集中定义, 防止构建配置和运行时初始化漂移。 */
export const TELEMETRY_SAMPLING = {
	traces: 0.25,
	replaysSession: 0.05,
	replaysOnError: 1.0,
} as const

/** Replay 只保留布局与交互轨迹, 不读取可读文字、输入和媒体。 */
export const REPLAY_OPTIONS = {
	maskAllText: true,
	maskAllInputs: true,
	blockAllMedia: true,
} as const

/** 把路由/操作名限制为不含用户数据的稳定 ASCII 标识。 */
export const NormalizeOperation = (value: string): string => {
	const normalized = value.replace(/[^A-Za-z0-9_.-]+/g, "_").replace(/^_+|_+$/g, "")
	return (normalized || "operation").slice(0, 80)
}

/** 无快照、无 DSN 或 consent 未明确 granted 时不创建 Web transport。 */
export const ShouldEnableTelemetry = (dsn: string, state: TelemetryState | undefined): boolean =>
	Boolean(dsn && state?.available && state.consent === "granted" && state.enabled)
