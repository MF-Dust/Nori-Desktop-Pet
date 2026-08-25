<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppCard from "../ui/AppCard.vue"
import AppButton from "../ui/AppButton.vue"
import AppActionButton from "../ui/AppActionButton.vue"
import AppDangerZone from "../ui/AppDangerZone.vue"

/**
 * 调试与诊断页 (参考 ClassIsland 的 DebugPage 与 AppLogsWindow)
 *
 * 页面文案统一走 i18n 访问器 (views.main.debug.*); 宿主日志内容与故意抛出的测试异常保持原文, 它们是诊断信息不是界面文案.
 */

const I18N = computed(() => useLanguages().views.main.debug)
const UI_I18N = computed(() => useLanguages().components.ui.state)

/** 日志条目, 与宿主 get_recent_logs 返回结构一致 */
type LogItem = {
	time: string
	level: string
	source: string
	message: string
}

const LEVEL_FILTERS = ["all", "error", "warn", "info"] as const
type LevelFilter = (typeof LEVEL_FILTERS)[number]

const FILTER_LABELS = computed<Record<LevelFilter, string>>(() => ({
	all: I18N.value.logs.filter.all,
	error: I18N.value.logs.filter.error,
	warn: I18N.value.logs.filter.warn,
	info: I18N.value.logs.filter.info,
}))

const logs = ref<LogItem[]>([])
const levelFilter = ref<LevelFilter>("all")
const diagnostic = ref<Record<string, string>>({})
const gcReleased = ref("")
const gcFailed = ref(false)
const busy = ref(false)
const DEBUG_CRASH_TESTS_AVAILABLE = computed(() => RUNTIME.snapshot.value?.app.debugCrashTestsAvailable ?? false)

const FILTERED_LOGS = computed(() => {
	const LIST = logs.value ?? []
	return levelFilter.value === "all" ? LIST : LIST.filter((item) => item.level === levelFilter.value)
})

// 垃圾回收结果提示: 标签与数值分开拼接, 不在 i18n 值里放占位符
const GC_TEXT = computed(() => {
	if (gcFailed.value) return I18N.value.tests.gcFailedText
	return gcReleased.value ? `${I18N.value.tests.gcReleased} ${gcReleased.value}` : ""
})

const refreshLogs = async () => {
	try {
		logs.value = (await RUNTIME.getRecentLogs()) ?? []
	} catch (error) {
		feedback.error(I18N.value.logs.loadFailed, error)
	}
}

const refreshDiagnostic = async () => {
	try {
		diagnostic.value = await RUNTIME.getDiagnosticInfo()
	} catch (error) {
		feedback.error(I18N.value.diagnostic.loadFailed, error)
	}
}

onMounted(() => {
	void refreshLogs()
	void refreshDiagnostic()
})

const clearLogs = async () => {
	try {
		await RUNTIME.clearRecentLogs()
		await refreshLogs()
	} catch (error) {
		feedback.error(I18N.value.logs.clearFailed, error)
	}
}

// 失败一律 rethrow: 详情走 feedback toast, 按钮自身只负责状态回落
const exportDiagnostics = async () => {
	try {
		const RESULT = await RUNTIME.exportDiagnostics()
		if (RESULT) feedback.success(`${I18N.value.diagnostic.exportSuccess}: ${RESULT.fileName}`)
	} catch (error) {
		feedback.error(I18N.value.diagnostic.exportFailed, error)
		throw error
	}
}

const copyLogs = async () => {
	const TEXT = FILTERED_LOGS.value
		.map((item) => `[${item.time}] [${item.level}] [${item.source}] ${item.message}`)
		.join("\n")
	if (!TEXT) return
	await writeTextSafely(TEXT, I18N.value.logs.copyFailed)
}

const copyDiagnostic = async () => {
	const TEXT = Object.entries(diagnostic.value)
		.map(([key, value]) => `${key}: ${value}`)
		.join("\n")
	if (!TEXT) return
	await writeTextSafely(TEXT, I18N.value.diagnostic.copyFailed)
}

const writeTextSafely = async (text: string, failureText: string) => {
	try {
		await RUNTIME.copyText(text)
	} catch (error) {
		feedback.error(failureText, error)
		throw error
	}
}

const openLogFolder = async () => {
	try {
		await RUNTIME.openLogFolder()
	} catch (error) {
		feedback.error(I18N.value.actions.openFolderFailed, error)
	}
}

const runGc = async () => {
	busy.value = true
	gcReleased.value = ""
	gcFailed.value = false
	try {
		const RESULT = await RUNTIME.runGcCollect()
		gcReleased.value = formatBytes(RESULT.released_bytes)
	} catch (error) {
		gcFailed.value = true
		feedback.error(I18N.value.tests.gcFailed, error)
	} finally {
		busy.value = false
	}
}

const writeTestLog = async () => {
	try {
		await RUNTIME.writeLog("warn", "调试页测试日志: 看到这条说明前端 → 宿主日志链路正常")
		await refreshLogs()
	} catch (error) {
		feedback.error(I18N.value.tests.writeLogFailed, error)
	}
}

// ---- 危险操作 ----

const crashUiThread = async () => {
	// 命中 Dispatcher 兜底: 弹出非致命崩溃窗, 关窗后应用继续运行
	await triggerCrashTest("ui_thread")
}

const crashBackgroundThread = async () => {
	// 命中域级兜底 (IsTerminating): 弹出致命崩溃窗, 进程随后退出
	await triggerCrashTest("background_thread")
}

const crashUnobservedTask = async () => {
	// 延迟到 GC 才浮出, 记入未观察任务异常日志, 不弹窗
	await triggerCrashTest("unobserved_task")
}

const triggerCrashTest = async (mode: string) => {
	try {
		await RUNTIME.debugCrashTest(mode)
	} catch (error) {
		feedback.error(`${I18N.value.danger.crashFailed} [${mode}]`, error)
	}
}

// 前端本地同步抛出, 由 main.ts 安装的 app.config.errorHandler 捕获并转发到宿主日志
const throwFrontendError = () => {
	throw new Error("前端异常测试: Vue errorHandler 链路验证")
}

const formatBytes = (bytes: number): string => {
	const KB = 1024
	if (bytes >= KB ** 3) return `${(bytes / KB ** 3).toFixed(1)} GB`
	if (bytes >= KB * KB) return `${(bytes / (KB * KB)).toFixed(1)} MB`
	if (bytes >= KB) return `${(bytes / KB).toFixed(1)} KB`
	return `${bytes} B`
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.subtitle"/>

		<!-- 警告横幅 (对应 ClassIsland InfoBar Severity=Error) -->
		<div
			class="shrink-0 flex items-center gap-2.5 px-3.5 py-2.5 rounded-md bg-overlay-4 border border-danger/35
				text-xs text-danger-text leading-relaxed"
			role="alert"
		>
			<Icon name="alert" :size="16" class="shrink-0"/>
			<span>{{ I18N.warningBanner }}</span>
		</div>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 运行诊断 -->
			<AppCard :title="I18N.diagnostic.title" icon="tool">
				<template #actions>
					<AppButton size="sm" @click="refreshDiagnostic">{{ I18N.actions.refresh }}</AppButton>
					<AppActionButton
						size="sm"
						:label="I18N.diagnostic.copy"
						:done-label="UI_I18N.copied"
						:action="copyDiagnostic"
					/>
					<AppActionButton
						size="sm"
						:label="I18N.diagnostic.export"
						:running-label="I18N.diagnostic.exporting"
						:action="exportDiagnostics"
					/>
					<AppButton size="sm" @click="openLogFolder">{{ I18N.actions.openFolder }}</AppButton>
				</template>

				<div class="flex flex-col">
					<div
						v-for="(VALUE, KEY) in diagnostic"
						:key="KEY"
						class="flex items-center justify-between gap-4 py-1.5 border-b border-line-subtle last:border-b-0"
					>
						<span class="shrink-0 mono text-xs text-text-muted">{{ KEY }}</span>
						<span class="mono text-xs text-text-primary text-right break-all">{{ VALUE }}</span>
					</div>
				</div>
			</AppCard>

			<!-- 2. 运行日志 (AppLogsWindow 简化版) -->
			<AppCard :title="I18N.logs.title" icon="terminal">
				<template #actions>
					<span class="text-hint">{{ I18N.logs.buffer }}</span>
				</template>

				<div class="flex items-center justify-between gap-2.5 flex-wrap">
					<div class="flex flex-wrap gap-2">
						<label
							v-for="FILTER in LEVEL_FILTERS"
							:key="FILTER"
							class="pill-choice focus-ring-within px-3.5 py-1 text-xs"
							:class="levelFilter === FILTER ? 'pill-choice-on' : 'pill-choice-off'"
						>
							<input v-model="levelFilter" type="radio" :value="FILTER" class="sr-only"/>
							{{ FILTER_LABELS[FILTER] }}
						</label>
					</div>
					<span class="inline-flex items-center gap-1.5">
						<AppButton size="sm" @click="refreshLogs">{{ I18N.actions.refresh }}</AppButton>
						<AppButton size="sm" @click="clearLogs">{{ I18N.actions.clear }}</AppButton>
						<AppActionButton
							size="sm"
							:label="I18N.logs.copy"
							:done-label="UI_I18N.copied"
							:disabled="FILTERED_LOGS.length === 0"
							:action="copyLogs"
						/>
					</span>
				</div>

				<div
					class="max-h-[26rem] scroll-area flex flex-col gap-0.5 p-2 rounded-sm
						border border-line-subtle bg-bg-abyss mono text-xs"
					role="log"
				>
					<p v-if="FILTERED_LOGS.length === 0" class="my-4 text-center text-text-faint">{{ I18N.logs.empty }}</p>
					<div
						v-for="(ITEM, INDEX) in FILTERED_LOGS"
						:key="INDEX"
						class="flex items-baseline gap-2 px-1 py-0.5 rounded-xs hover:bg-overlay-6"
					>
						<span class="shrink-0 text-text-faint">{{ ITEM.time }}</span>
						<span
							class="shrink-0 w-[4.2rem] text-center rounded-xs font-600 bg-overlay-6"
							:class="ITEM.level === 'error'
								? 'text-danger-text'
								: ITEM.level === 'warn' ? 'text-warning' : ITEM.level === 'info' ? 'text-nori-teal-bright' : 'text-text-muted'"
						>{{ ITEM.level }}</span>
						<span class="shrink-0 w-[6.4rem] text-text-muted">{{ ITEM.source }}</span>
						<span class="text-text-body break-all whitespace-pre-wrap">{{ ITEM.message }}</span>
					</div>
				</div>
			</AppCard>

			<!-- 3. 功能测试 -->
			<AppCard :title="I18N.tests.title" icon="zap">
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton size="sm" :disabled="busy" @click="runGc">{{ I18N.tests.gc }}</AppButton>
					<span v-if="GC_TEXT" class="text-sm text-nori-teal-bright">{{ GC_TEXT }}</span>
				</div>
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton size="sm" @click="writeTestLog">{{ I18N.tests.writeLog }}</AppButton>
					<span class="text-xs text-text-faint">{{ I18N.tests.writeLogDesc }}</span>
				</div>
			</AppCard>

			<!-- 4. 危险操作 (默认折叠: 崩溃测试不该离误触只有一步) -->
			<AppDangerZone
				v-if="DEBUG_CRASH_TESTS_AVAILABLE"
				:title="I18N.danger.title"
				:desc="I18N.danger.hint"
				:toggle-label="I18N.danger.toggle"
			>
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton variant="danger" size="sm" @click="crashUiThread">{{ I18N.danger.crashUi }}</AppButton>
					<span class="text-xs text-text-faint">{{ I18N.danger.crashUiDesc }}</span>
				</div>
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton variant="danger" size="sm" @click="crashBackgroundThread">{{ I18N.danger.crashBackground }}</AppButton>
					<span class="text-xs text-text-faint">{{ I18N.danger.crashBackgroundDesc }}</span>
				</div>
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton variant="danger" size="sm" @click="crashUnobservedTask">{{ I18N.danger.unobservedTask }}</AppButton>
					<span class="text-xs text-text-faint">{{ I18N.danger.unobservedTaskDesc }}</span>
				</div>
				<div class="flex items-center gap-3 flex-wrap">
					<AppButton variant="danger" size="sm" @click="throwFrontendError">{{ I18N.danger.frontendError }}</AppButton>
					<span class="text-xs text-text-faint">{{ I18N.danger.frontendErrorDesc }}</span>
				</div>
			</AppDangerZone>
		</div>
	</div>
</template>
