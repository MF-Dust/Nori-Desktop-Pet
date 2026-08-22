<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import Icon from "../Icon.vue"

/**
 * 调试与诊断页 (参考 ClassIsland 的 DebugPage 与 AppLogsWindow)
 *
 * 页面文案硬编码中文: 本页面向开发者排查问题, 与同目录多数设置组件的既定做法一致.
 */

/** 日志条目, 与宿主 get_recent_logs 返回结构一致 */
type LogItem = {
	time: string
	level: string
	source: string
	message: string
}

const LEVEL_FILTERS = ["all", "error", "warn", "info"] as const
type LevelFilter = (typeof LEVEL_FILTERS)[number]

const FILTER_LABELS: Record<LevelFilter, string> = {
	all: "全部",
	error: "错误",
	warn: "警告",
	info: "信息",
}

const logs = ref<LogItem[]>([])
const levelFilter = ref<LevelFilter>("all")
const diagnostic = ref<Record<string, string>>({})
const gcResult = ref("")
const busy = ref(false)

const FILTERED_LOGS = computed(() =>
	levelFilter.value === "all" ? logs.value : logs.value.filter((item) => item.level === levelFilter.value)
)

const refreshLogs = async () => {
	try {
		logs.value = await RUNTIME.getRecentLogs()
	} catch (error) {
		console.error("读取运行日志失败:", error)
	}
}

const refreshDiagnostic = async () => {
	try {
		diagnostic.value = await RUNTIME.getDiagnosticInfo()
	} catch (error) {
		console.error("读取诊断信息失败:", error)
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
		console.error("清空日志失败:", error)
	}
}

const copyLogs = async () => {
	const TEXT = FILTERED_LOGS.value
		.map((item) => `[${item.time}] [${item.level}] [${item.source}] ${item.message}`)
		.join("\n")
	if (!TEXT) return
	await writeTextSafely(TEXT, "复制日志失败:")
}

const copyDiagnostic = async () => {
	const TEXT = Object.entries(diagnostic.value)
		.map(([key, value]) => `${key}: ${value}`)
		.join("\n")
	if (!TEXT) return
	await writeTextSafely(TEXT, "复制诊断信息失败:")
}

const writeTextSafely = async (text: string, failurePrefix: string) => {
	try {
		await RUNTIME.copyText(text)
	} catch (error) {
		console.error(failurePrefix, error)
	}
}

const openLogFolder = async () => {
	try {
		await RUNTIME.openLogFolder()
	} catch (error) {
		console.error("打开日志目录失败:", error)
	}
}

const runGc = async () => {
	busy.value = true
	gcResult.value = ""
	try {
		const RESULT = await RUNTIME.runGcCollect()
		gcResult.value = `已释放 ${formatBytes(RESULT.released_bytes)}`
	} catch (error) {
		console.error("触发垃圾回收失败:", error)
		gcResult.value = "执行失败"
	} finally {
		busy.value = false
	}
}

const writeTestLog = async () => {
	try {
		await RUNTIME.writeLog("warn", "调试页测试日志: 看到这条说明前端 → 宿主日志链路正常")
		await refreshLogs()
	} catch (error) {
		console.error("写入测试日志失败:", error)
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
		console.error(`崩溃测试 [${mode}] 触发失败:`, error)
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
	<div class="debug-settings">
		<header class="section-header">
			<h2 class="title glow-teal">调试与诊断</h2>
			<p class="subtitle">运行状态检查、内存日志查看与异常兜底链路测试</p>
		</header>

		<!-- 警告横幅 (对应 ClassIsland InfoBar Severity=Error) -->
		<div class="warning-banner">
			<Icon name="alert" :size="16" class="banner-icon"/>
			<span>本页仅供调试使用。请确认你知道自己在做什么 —— "危险操作"区的按钮会导致应用弹出崩溃窗甚至退出。</span>
		</div>

		<div class="settings-content">
			<!-- 1. 运行诊断 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="tool" :size="18" class="card-icon"/>
					<span class="card-title">运行诊断</span>
					<span class="header-actions">
						<button class="btn-ghost btn-sm" @click="refreshDiagnostic">刷新</button>
						<button class="btn-ghost btn-sm" @click="copyDiagnostic">复制诊断信息</button>
						<button class="btn-ghost btn-sm" @click="openLogFolder">打开日志文件夹</button>
					</span>
				</div>
				<div class="card-body">
					<div v-for="(VALUE, KEY) in diagnostic" :key="KEY" class="info-row">
						<span class="info-label">{{ KEY }}</span>
						<span class="info-val">{{ VALUE }}</span>
					</div>
				</div>
			</div>

			<!-- 2. 运行日志 (AppLogsWindow 简化版) -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="terminal" :size="18" class="card-icon"/>
					<span class="card-title">运行日志</span>
					<span class="card-subtitle">宿主内存缓冲, 最近 500 条</span>
				</div>
				<div class="card-body">
					<div class="log-toolbar">
						<div class="radio-group">
							<label
								v-for="FILTER in LEVEL_FILTERS"
								:key="FILTER"
								class="filter-chip"
								:class="{active: levelFilter === FILTER}"
							>
								<input v-model="levelFilter" type="radio" :value="FILTER"/>
								{{ FILTER_LABELS[FILTER] }}
							</label>
						</div>
						<span class="header-actions">
							<button class="btn-ghost btn-sm" @click="refreshLogs">刷新</button>
							<button class="btn-ghost btn-sm" @click="clearLogs">清空</button>
							<button class="btn-ghost btn-sm" @click="copyLogs">复制日志</button>
						</span>
					</div>
					<div class="log-list">
						<p v-if="FILTERED_LOGS.length === 0" class="log-empty">暂无匹配的日志</p>
						<div v-for="(ITEM, INDEX) in FILTERED_LOGS" :key="INDEX" class="log-row">
							<span class="log-time">{{ ITEM.time }}</span>
							<span class="log-level" :class="`lv-${ITEM.level}`">{{ ITEM.level }}</span>
							<span class="log-source">{{ ITEM.source }}</span>
							<span class="log-message">{{ ITEM.message }}</span>
						</div>
					</div>
				</div>
			</div>

			<!-- 3. 功能测试 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="zap" :size="18" class="card-icon"/>
					<span class="card-title">功能测试</span>
				</div>
				<div class="card-body">
					<div class="action-row">
						<button class="btn-ghost btn-sm" :disabled="busy" @click="runGc">触发垃圾回收</button>
						<span v-if="gcResult" class="action-result">{{ gcResult }}</span>
					</div>
					<div class="action-row">
						<button class="btn-ghost btn-sm" @click="writeTestLog">写入测试日志</button>
						<span class="action-desc">走前端 → 宿主 write_log 链路写一条 warn 记录</span>
					</div>
				</div>
			</div>

			<!-- 4. 危险操作 -->
			<div class="setting-card danger-card">
				<div class="card-header danger">
					<Icon name="alert" :size="18" class="card-icon"/>
					<span class="card-title">危险操作</span>
					<span class="card-subtitle">没事别按。前两个会真的弹崩溃窗。</span>
				</div>
				<div class="card-body">
					<div class="action-row">
						<button class="btn-danger btn-sm" @click="crashUiThread">崩溃测试:UI 线程</button>
						<span class="action-desc">命中 UI 线程兜底 → 弹非致命崩溃窗, 关窗后应用继续运行</span>
					</div>
					<div class="action-row">
						<button class="btn-danger btn-sm" @click="crashBackgroundThread">崩溃测试:后台线程</button>
						<span class="action-desc">命中域级兜底 (进程即将终止) → 弹致命崩溃窗, 随后退出</span>
					</div>
					<div class="action-row">
						<button class="btn-danger btn-sm" @click="crashUnobservedTask">未观察任务异常</button>
						<span class="action-desc">延迟至 GC 才浮出 → 只记入日志, 不弹窗 (可稍后刷新上方日志查看)</span>
					</div>
					<div class="action-row">
						<button class="btn-danger btn-sm" @click="throwFrontendError">前端异常测试</button>
						<span class="action-desc">Vue errorHandler 捕获并转发到宿主日志, 界面不崩</span>
					</div>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.debug-settings {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	overflow-y: auto;
	padding: 1.6rem 2.4rem;
	gap: 1.6rem;
}

.section-header {
	display: flex;
	flex-direction: column;
	gap: 0.4rem;
}

.title {
	margin: 0;
	font-size: 1.8rem;
	font-weight: 700;
	color: var(--text-primary);
}

.subtitle {
	margin: 0;
	font-size: 1.2rem;
	color: var(--text-faint);
}

.warning-banner {
	display: flex;
	align-items: center;
	gap: 1rem;
	padding: 1rem 1.4rem;
	border: 0.1rem solid rgba(251, 60, 68, 0.35);
	border-radius: var(--radius-md);
	background: rgba(251, 60, 68, 0.08);
	color: var(--danger);
	font-size: 1.2rem;
	line-height: 1.6;
	flex-shrink: 0;

	.banner-icon {
		flex-shrink: 0;
	}
}

.settings-content {
	display: flex;
	flex-direction: column;
	gap: 1.4rem;
	padding-bottom: 2rem;
}

.setting-card {
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-md);
	padding: 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
	transition: all 0.2s ease;

	&:hover {
		border-color: var(--line-strong);
	}
}

.danger-card {
	border-color: rgba(251, 60, 68, 0.3);

	&:hover {
		border-color: rgba(251, 60, 68, 0.5);
	}
}

.card-header {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	color: var(--nori-teal-bright);

	&.danger {
		color: var(--danger);
	}
}

.card-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-subtitle {
	font-size: 1.15rem;
	color: var(--text-faint);
	margin-left: auto;
}

.header-actions {
	display: inline-flex;
	gap: 0.6rem;
	margin-left: auto;
}

.log-toolbar .header-actions {
	margin-left: 0;
}

.card-body {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.info-row {
	display: flex;
	justify-content: space-between;
	align-items: center;
	gap: 1.6rem;
	padding: 0.7rem 0;
	border-bottom: 0.1rem solid var(--line-subtle);

	&:last-child {
		border-bottom: none;
	}
}

.info-label {
	font-size: 1.2rem;
	color: var(--text-muted);
	font-family: monospace;
	flex-shrink: 0;
}

.info-val {
	font-size: 1.2rem;
	color: var(--text-primary);
	font-family: monospace;
	text-align: right;
	word-break: break-all;
}

.radio-group {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
}

.filter-chip {
	display: inline-flex;
	align-items: center;
	padding: 0.5rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-muted);
	font-size: 1.15rem;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	input {
		display: none;
	}

	&:hover {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.06);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		border-color: transparent;
		background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
		color: #03101c;
		font-weight: 600;
		box-shadow: 0 0.2rem 1.2rem var(--glow-teal-soft);
	}
}

.log-toolbar {
	display: flex;
	align-items: center;
	justify-content: space-between;
	gap: 1rem;
	flex-wrap: wrap;
}

.log-list {
	max-height: 26rem;
	overflow-y: auto;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(3, 12, 20, 0.55);
	padding: 0.8rem;
	display: flex;
	flex-direction: column;
	gap: 0.2rem;
	font-family: monospace;
	font-size: 1.15rem;
}

.log-empty {
	margin: 1.6rem 0;
	text-align: center;
	color: var(--text-faint);
	font-family: inherit;
}

.log-row {
	display: flex;
	gap: 0.9rem;
	padding: 0.35rem 0.4rem;
	border-radius: 0.4rem;
	line-height: 1.5;
	align-items: baseline;

	&:hover {
		background: rgba(125, 227, 255, 0.05);
	}
}

.log-time {
	color: var(--text-faint);
	flex-shrink: 0;
}

.log-level {
	flex-shrink: 0;
	width: 4.2rem;
	text-align: center;
	border-radius: 0.4rem;
	font-weight: 600;

	&.lv-error {
		color: var(--danger);
		background: rgba(251, 60, 68, 0.12);
	}

	&.lv-warn {
		color: var(--warning);
		background: rgba(241, 178, 74, 0.12);
	}

	&.lv-info {
		color: var(--nori-teal-bright);
		background: rgba(125, 227, 255, 0.08);
	}
}

.log-source {
	color: var(--text-muted);
	flex-shrink: 0;
	width: 6.4rem;
}

.log-message {
	color: var(--text-body);
	word-break: break-all;
	white-space: pre-wrap;
}

.action-row {
	display: flex;
	align-items: center;
	gap: 1.2rem;
	flex-wrap: wrap;
}

.action-result {
	font-size: 1.2rem;
	color: var(--nori-teal-bright);
}

.action-desc {
	font-size: 1.15rem;
	color: var(--text-faint);
}

.btn-sm {
	padding: 0.55rem 1.2rem;
	font-size: 1.2rem;
}

.btn-danger {
	padding: 0.55rem 1.2rem;
	border: 0.1rem solid rgba(251, 60, 68, 0.4);
	border-radius: var(--radius-sm);
	font-size: 1.2rem;
	font-family: inherit;
	font-weight: 500;
	cursor: pointer;
	color: var(--danger);
	background: rgba(251, 60, 68, 0.06);
	display: inline-flex;
	align-items: center;
	justify-content: center;
	gap: 0.6rem;
	transition: all 0.2s ease;

	&:hover:not(:disabled) {
		background: rgba(251, 60, 68, 0.14);
		border-color: rgba(251, 60, 68, 0.6);
		box-shadow: 0 0 1.2rem rgba(251, 60, 68, 0.25);
		transform: translateY(-0.1rem);
	}
}
</style>
