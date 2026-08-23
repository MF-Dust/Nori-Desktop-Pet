import * as Sentry from "@sentry/vue"
import type {ErrorEvent, SpanJSON, TransactionEvent} from "@sentry/core"
import type {App} from "vue"
import type {Router} from "vue-router"
import {host} from "../host"
import type {TelemetryState, UiSnapshot} from "../runtime"
import {NormalizeOperation, REPLAY_OPTIONS, ShouldEnableTelemetry, TELEMETRY_SAMPLING} from "./policy"

const WEB_DSN = import.meta.env.VITE_SENTRY_DSN_WEB ?? ""
const RELEASE = import.meta.env.VITE_SENTRY_RELEASE ?? ""
const ENVIRONMENT = import.meta.env.VITE_SENTRY_ENVIRONMENT || "production"
const REPLAY_INTEGRATIONS = new Set(["GlobalHandlers", "BrowserApiErrors", "Breadcrumbs", "HttpContext", "BrowserTracing"])
const RECENT_ERRORS = new Map<string, number>()
const SEEN_ERROR_OBJECTS = new WeakSet<object>()

let active = false
let generation = 0
let activeKey = ""
let currentWindow = "unknown"
let currentPlatform = "unknown"

/**
 * 当前 WebView 的安全标签。
 *
 * label 只来自宿主注入的固定窗口名, 不把 URL、查询参数或业务数据带进标签。
 */
const safeTags = () => ({
	runtime: "webview",
	window: currentWindow,
	platform: currentPlatform,
})

/** 把错误压缩成稳定 key, 只用于本地去重, 不会发送给 Sentry。 */
const errorKey = (error: unknown): string => {
	if (error instanceof Error) return `${error.name}:${error.message}`
	return `${typeof error}:${String(error)}`
}

/** 前端事件最后一道脱敏边界。 */
const scrubEvent = (event: ErrorEvent): ErrorEvent => {
	event.user = undefined
	event.request = undefined
	event.extra = undefined
	event.logentry = undefined
	event.breadcrumbs = undefined
	event.contexts = undefined
	event.tags = safeTags()
	if (event.exception?.values) {
		for (const exception of event.exception.values) {
			exception.value = exception.type || "Error"
		}
	}
	if (event.message) event.message = event.exception ? undefined : "web_error"
	if (event.transaction) event.transaction = NormalizeOperation(event.transaction)
	if (event.spans) {
		event.spans = event.spans.map((span: SpanJSON) => ({
			...span,
			description: "web.span",
			data: {},
		}))
	}
	return event
}

/** 事务只允许携带路由/固定操作名, 不记录请求 URL 或参数。 */
const scrubTransaction = (event: TransactionEvent): TransactionEvent => {
	event.user = undefined
	event.request = undefined
	event.extra = undefined
	event.logentry = undefined
	event.breadcrumbs = undefined
	event.contexts = undefined
	event.tags = safeTags()
	event.transaction = NormalizeOperation(event.transaction ?? "web_transaction")
	if (event.spans) {
		event.spans = event.spans.map((span: SpanJSON) => ({
			...span,
			description: "web.span",
			data: {},
		}))
	}
	return event
}

/** 初始化 Web SDK; Replay 作为可选集成单独控制。 */
const InitializeWebSentry = (app: App, router: Router, includeReplay: boolean): void => {
	Sentry.init({
		app,
		attachErrorHandler: false,
		attachProps: false,
		dsn: WEB_DSN,
		release: RELEASE || undefined,
		environment: ENVIRONMENT,
		integrations: integrations => {
			const RESULT = integrations
				.filter(integration => !REPLAY_INTEGRATIONS.has(integration.name))
				.concat(Sentry.browserTracingIntegration({
					router,
					traceFetch: false,
					traceXHR: false,
					instrumentPageLoad: true,
					instrumentNavigation: true,
				}))
			if (includeReplay) RESULT.push(Sentry.replayIntegration(REPLAY_OPTIONS))
			return RESULT
		},
		tracesSampleRate: TELEMETRY_SAMPLING.traces,
		replaysSessionSampleRate: TELEMETRY_SAMPLING.replaysSession,
		replaysOnErrorSampleRate: TELEMETRY_SAMPLING.replaysOnError,
		tracePropagationTargets: [],
		beforeSend: scrubEvent,
		beforeSendTransaction: scrubTransaction,
		beforeBreadcrumb: () => null,
	})
}

/**
 * 根据宿主快照启停 Web SDK。
 *
 * 没有快照、没有 Web DSN 或用户关闭开关时都不初始化 transport。
 */
export const SyncWebTelemetry = async (app: App, router: Router, snapshot: UiSnapshot | null): Promise<void> => {
	const CURRENT_GENERATION = ++generation
	const telemetry: TelemetryState | undefined = snapshot?.telemetry
	const shouldEnable = ShouldEnableTelemetry(WEB_DSN, telemetry)

	const NEXT_WINDOW = host()?.label || "unknown"
	const NEXT_PLATFORM = snapshot?.app.platform || "unknown"
	const NEXT_KEY = `${NEXT_WINDOW}:${NEXT_PLATFORM}:${RELEASE}:${ENVIRONMENT}`
	if (active && shouldEnable && activeKey === NEXT_KEY) return
	if (active) {
		await Sentry.close(1000)
		active = false
		activeKey = ""
	}
	if (!shouldEnable || CURRENT_GENERATION !== generation) return

	currentWindow = NEXT_WINDOW
	currentPlatform = NEXT_PLATFORM
	try {
		InitializeWebSentry(app, router, true)
		Sentry.setTags(safeTags())
		active = true
		activeKey = NEXT_KEY
	} catch {
		// 某些 WebView 不支持 Replay 时, 关闭 Replay 后保留错误与性能上报。
		try {
			await Sentry.close(1000)
			InitializeWebSentry(app, router, false)
			Sentry.setTags(safeTags())
			active = true
			activeKey = NEXT_KEY
		} catch {
			// Sentry 初始化失败只降级为本地日志, 不影响页面挂载。
			active = false
			activeKey = ""
		}
	}
}

/** 手动捕获前端异常; 全局错误处理器和 Vue handler 共用此入口。 */
export const CaptureError = (error: unknown, operation: string): void => {
	if (!active) return
	if (typeof error === "object" && error !== null) {
		if (SEEN_ERROR_OBJECTS.has(error)) return
		SEEN_ERROR_OBJECTS.add(error)
	}
	const KEY = errorKey(error)
	const NOW = Date.now()
	const LAST = RECENT_ERRORS.get(KEY) ?? 0
	if (NOW - LAST < 1000) return
	RECENT_ERRORS.set(KEY, NOW)
	if (RECENT_ERRORS.size > 100) {
		for (const [key, timestamp] of RECENT_ERRORS) {
			if (NOW - timestamp >= 1000) RECENT_ERRORS.delete(key)
		}
	}
	try {
		Sentry.withScope(scope => {
			scope.setTags(safeTags())
			scope.setTag("operation", NormalizeOperation(operation))
			Sentry.captureException(error)
		})
	} catch {
		// 错误上报自身失败不能触发第二条错误链路。
	}
}

/** 供测试和调试页确认当前 Web SDK 是否已启用。 */
export const IsWebTelemetryEnabled = (): boolean => active
