<script setup lang="ts">
import {computed, h, nextTick, onBeforeUnmount, onMounted, ref, watch} from "vue"
import {useDialog} from "naive-ui"
import useLanguages from "../services/i18n/useLanguages.ts"
import Icon from "./Icon.vue"
import {RUNTIME, type ApprovalRequestDto} from "../services/runtime"
import {createChatStore} from "../services/runtime/chatStore"

const I18N = computed(() => useLanguages().views.main.chat)

// 事件: 前往设置
const emit = defineEmits<{goSettings: []}>()

// AI 是否已配置 (后端快照判定)
const configured = computed(() => RUNTIME.snapshot.value?.ai.configured ?? false)
const currentModel = computed(() => {
	const MODEL = RUNTIME.snapshot.value?.ai.model
	return MODEL && MODEL.length > 0 ? MODEL : "未知模型"
})

// 聊天状态机 (业务在后端, 这里只渲染投影)
const CHAT = createChatStore()
const {bubbles, sending, executingTool, metrics, errorMsg} = CHAT

const input = ref("")
const listRef = ref<HTMLElement>()
const DIALOG = useDialog()

// 活跃技能数与工具数 (来自快照)
const activeSkillsCount = computed(() => RUNTIME.snapshot.value?.enabledSkillsCount ?? 0)
const activeToolsCount = computed(() =>
	(RUNTIME.snapshot.value?.tools ?? []).filter(tool => tool.enabled).length)

// 助手回复按句切分成多个气泡 (像日常聊天, 不受模型换行影响)
const splitSentences = (text: string): string[] => {
	const LINES = text
		.split(/\r?\n+/)
		.map(s => s.trim())
		.filter(Boolean)
	if (LINES.length > 1) return LINES
	const SENTENCES = text
		.split(/(?<=[。！？!?；;])/)
		.map(s => s.trim())
		.filter(Boolean)
	return SENTENCES.flatMap(s =>
		s.length > 80
			? s
					.split(/(?<=[，,、])/)
					.map(x => x.trim())
					.filter(Boolean)
			: [s],
	)
}

// 展示列表: 助手一条回复 = 多个气泡
interface DisplayBubble {
	key: string
	role: string
	content: string
}
const displayBubbles = computed<DisplayBubble[]>(() => {
	const LIST: DisplayBubble[] = []
	for (const msg of bubbles.value) {
		if (msg.role === "assistant") {
			splitSentences(msg.content).forEach((s, i) =>
				LIST.push({key: `${msg.key}-${i}`, role: "assistant", content: s}),
			)
		} else {
			LIST.push({key: msg.key, role: msg.role, content: msg.content})
		}
	}
	return LIST
})

// 滚动到底部
const scrollToBottom = async () => {
	await nextTick()
	listRef.value?.scrollTo({top: listRef.value.scrollHeight})
}

// 流式输出期间每个 chunk 都 nextTick+scrollTo 太频, 合并到 ~100ms 一次
let scrollPending = false
const scheduleScrollToBottom = () => {
	if (scrollPending) return
	scrollPending = true
	setTimeout(() => {
		scrollPending = false
		void scrollToBottom()
	}, 100)
}

watch(bubbles, () => scheduleScrollToBottom(), {deep: true})

// 格式化数字与速度
const formatNum = (n?: number) => (typeof n === "number" ? n.toLocaleString() : "0")
const tokensPerSecond = computed(() => {
	if (!metrics.value || metrics.value.durationMs <= 0 || metrics.value.completionTokens <= 0) return 0
	return Math.round((metrics.value.completionTokens / (metrics.value.durationMs / 1000)) * 10) / 10
})

// 逐调用工具授权: 队列驱动, 每个请求单独弹窗; 关闭/拒绝/卸载都解析为拒绝
const shownApprovalIds = new Set<string>()
const activeApproval = computed<ApprovalRequestDto | null>(() => {
	for (const pending of CHAT.pendingApprovals.value) {
		if (!shownApprovalIds.has(pending.request.requestId)) return pending.request
	}
	return null
})

// 弹窗展示由 watcher 风格的轮询完成: activeApproval 变化时弹出
let dialogShownFor: string | null = null
const showApprovalDialogIfAny = () => {
	const REQUEST = activeApproval.value
	if (!REQUEST || dialogShownFor === REQUEST.requestId) return
	dialogShownFor = REQUEST.requestId
	shownApprovalIds.add(REQUEST.requestId)
	DIALOG.warning({
		title: I18N.value.approvalTitle,
		content: () => h("div", [
			h("p", {style: "margin:0 0 0.8rem;font-size:1.3rem;color:#8bd8ff"},
				`${REQUEST.toolName}${REQUEST.description ? ` — ${REQUEST.description}` : ""}`),
			h("pre", {
				style: "margin:0;max-height:16rem;overflow:auto;background:rgba(255,255,255,0.05);"
					+ "padding:1rem;border-radius:0.6rem;font-size:1.2rem;line-height:1.5;white-space:pre-wrap",
			}, JSON.stringify(REQUEST.arguments ?? {}, null, 2)),
		]),
		positiveText: I18N.value.approve,
		negativeText: I18N.value.deny,
		closable: true,
		onPositiveClick: () => void CHAT.decideApproval(REQUEST.requestId, true),
		onNegativeClick: () => void CHAT.decideApproval(REQUEST.requestId, false),
		onClose: () => void CHAT.decideApproval(REQUEST.requestId, false),
		onMaskClick: () => void CHAT.decideApproval(REQUEST.requestId, false),
	})
}

// 历史翻页
const loadOlderHistory = async () => {
	try {
		await CHAT.loadOlder()
	} catch (error) {
		console.error("加载更早的历史失败:", error)
	}
}

let unlistenAgent: (() => void) | null = null

onMounted(async () => {
	await RUNTIME.init()
	unlistenAgent = await CHAT.connect()
	if (RUNTIME.snapshot.value?.ai.configured) {
		await CHAT.loadRecent()
		await scrollToBottom()
	}
	showApprovalDialogIfAny()
})

onBeforeUnmount(() => {
	CHAT.dispose()
	unlistenAgent?.()
	unlistenAgent = null
})

// 发送消息
const send = async () => {
	const TEXT = input.value.trim()
	if (!TEXT || sending.value) return
	input.value = ""
	await CHAT.send(TEXT)
	await scrollToBottom()
}

// 停止当前生成
const stopGeneration = () => {
	void CHAT.abort()
}

// 清空当前对话历史
const clearChatHistory = async () => {
	await CHAT.clear()
}

// 语音输入控制 (后端 STT: 开始录音 / 停止并识别)
const isRecording = ref(false)
const toggleVoiceInput = async () => {
	if (isRecording.value) {
		isRecording.value = false
		try {
			const RESULT = await RUNTIME.sttStop()
			if (RESULT.text) {
				input.value = RESULT.text
				await send()
			}
		} catch (error) {
			console.error("语音识别失败:", error)
		}
	} else {
		try {
			await RUNTIME.sttStart()
			isRecording.value = true
		} catch (error) {
			console.error("启动录音失败:", error)
			isRecording.value = false
		}
	}
}

// 订阅驱动滚动与授权弹窗
const POLL = setInterval(() => {
	showApprovalDialogIfAny()
}, 200)

onBeforeUnmount(() => clearInterval(POLL))
</script>

<template>
	<section class="chat-view">
		<!-- 未配置时提示 -->
		<div v-if="!configured" class="chat-empty">
			<div class="empty-icon-wrap">
				<div class="empty-halo"></div>
				<Icon name="cpu" :size="36" class="empty-icon"/>
			</div>
			<h2 class="chat-empty-title glow-teal">{{ I18N.emptyTitle }}</h2>
			<p class="chat-empty-desc">{{ I18N.emptyDesc }}</p>
			<button class="btn-primary btn-config" @click="emit('goSettings')">
				<Icon name="settings" :size="15"/>
				<span>{{ I18N.goSettings }}</span>
			</button>
		</div>

		<!-- 已配置: 聊天界面 -->
		<template v-else>
			<div class="chat-header-bar">
				<div class="chat-title-wrap">
					<span class="chat-header-title">{{ I18N.title }}</span>
					<span class="model-badge">
						<span class="model-dot"/>
						{{ currentModel }}
					</span>
				</div>
				<div class="header-right-ops">
					<button class="btn-clear-chat" title="清空对话历史" @click="clearChatHistory">
						<Icon name="close" :size="12"/>
						<span>{{ I18N.clearHistory || '清空历史' }}</span>
					</button>
				</div>
			</div>

			<!-- 性能指标与 Prompt Caching 指标条 -->
			<div class="chat-metrics-bar">
				<div class="metrics-left">
					<div class="metric-item">
						<span class="metric-label">上下文:</span>
						<span class="metric-val" :class="{highlight: metrics && metrics.totalTokens > 0}">
							{{ metrics ? formatNum(metrics.totalTokens) : '0' }} tok
						</span>
						<span v-if="metrics && metrics.durationMs > 0" class="metric-speed">
							({{ tokensPerSecond }} t/s)
						</span>
					</div>

					<div class="metric-item cache-item" :class="{hit: metrics && metrics.cachedTokens > 0}">
						<span class="metric-label">缓存命中:</span>
						<span class="metric-val" :class="{highlight: metrics && metrics.cachedTokens > 0}">
							{{ metrics ? metrics.cacheHitRate + '%' : '0%' }}
						</span>
					</div>
				</div>

				<div class="metrics-right">
					<span class="metric-addons">
						{{ activeSkillsCount }} 技能 / {{ activeToolsCount }} 工具
					</span>
				</div>
			</div>

			<div ref="listRef" class="chat-list">
				<!-- 更早的历史按需加载, 避免历史表增长后首屏全量拉取 -->
				<button
					v-if="CHAT.hasMoreHistory.value"
					class="btn-load-earlier"
					@click="loadOlderHistory"
				>
					<Icon name="loading" class="btn-icon spin" :size="12"/>
					<span>{{ I18N.loadEarlier }}</span>
				</button>
				<div
					v-for="bubble in displayBubbles"
					:key="bubble.key"
					class="chat-msg"
					:class="bubble.role"
				>
					<div class="chat-bubble">
						<span class="bubble-text">{{ bubble.content }}</span>
					</div>
				</div>

				<!-- 工具调用中提示 -->
				<div v-if="executingTool" class="tool-executing-hint">
					<Icon name="loading" class="tool-icon spin" :size="13"/>
					<span>正在执行工具: {{ executingTool }}...</span>
				</div>
			</div>

			<p v-if="errorMsg" class="chat-error">{{ errorMsg }}</p>

			<div class="chat-input-row">
				<button
					class="voice-btn"
					:class="{active: isRecording}"
					type="button"
					:title="isRecording ? '点击结束语音输入' : '点击开始语音输入'"
					@click="toggleVoiceInput"
				>
					<Icon name="mic" class="btn-icon" :size="16"/>
				</button>
				<input
					v-model="input"
					class="input"
					type="text"
					:placeholder="I18N.inputPlaceholder"
					spellcheck="false"
					@keydown.enter="send"
				/>
				<button
					v-if="sending"
					class="send-btn stop-btn"
					type="button"
					:title="I18N.stopGeneration"
					@click="stopGeneration"
				>
					<Icon name="close" class="btn-icon" :size="16"/>
				</button>
				<button
					v-else
					class="send-btn"
					:disabled="!input.trim()"
					@click="send"
				>
					<Icon name="send" class="btn-icon" :size="16"/>
				</button>
			</div>
		</template>
	</section>
</template>

<style scoped lang="less">
.chat-view {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	min-height: 0;
	position: relative;
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-lg);
	overflow: hidden;
	box-shadow: 0 0.8rem 2.8rem rgba(0, 0, 0, 0.35);
	backdrop-filter: blur(1.2rem);
}

// 未配置提示
.chat-empty {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.4rem;
	padding: 2rem;
	text-align: center;
}

.empty-icon-wrap {
	position: relative;
	width: 8rem;
	height: 8rem;
	display: flex;
	align-items: center;
	justify-content: center;
	margin-bottom: 0.4rem;

	.empty-halo {
		position: absolute;
		inset: 0;
		border-radius: 50%;
		background: radial-gradient(circle, rgba(125, 227, 255, 0.25) 0%, transparent 70%);
		animation: glow-pulse 2.8s ease-in-out infinite;
	}

	.empty-icon {
		color: var(--nori-teal-bright);
		position: relative;
		z-index: 1;
	}
}

.chat-empty-title {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--text-primary);
}

.chat-empty-desc {
	font-size: 1.3rem;
	color: var(--text-muted);
	max-width: 38rem;
	line-height: 1.6;
}

.btn-config {
	margin-top: 0.6rem;
	padding: 0.9rem 2.4rem;
}

// 头部
.chat-header-bar {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1rem 1.6rem;
	border-bottom: 0.1rem solid var(--line-subtle);
	background: rgba(8, 22, 36, 0.6);
	backdrop-filter: blur(0.8rem);
	flex-shrink: 0;
}

.chat-title-wrap {
	display: flex;
	align-items: center;
	gap: 0.9rem;
}

.chat-header-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.model-badge {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.25rem 0.8rem;
	border-radius: var(--radius-pill);
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal-bright);
	font-size: 1.1rem;
	font-family: monospace;

	.model-dot {
		width: 0.5rem;
		height: 0.5rem;
		border-radius: 50%;
		background: #20e090;
		box-shadow: 0 0 0.6rem #20e090;
	}
}

.header-right-ops {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.btn-clear-chat {
	display: inline-flex;
	align-items: center;
	gap: 0.5rem;
	padding: 0.4rem 0.9rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	font-size: 1.15rem;
	font-family: inherit;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: #ff6b6b;
		border-color: rgba(251, 60, 68, 0.3);
		background: rgba(251, 60, 68, 0.1);
	}
}

// 实用信息与上下文指标条
.chat-metrics-bar {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.45rem 1.6rem;
	background: rgba(0, 0, 0, 0.35);
	border-bottom: 0.1rem solid var(--line-subtle);
	font-size: 1.1rem;
	color: var(--text-muted);
	user-select: none;
	flex-shrink: 0;
}

.metrics-left {
	display: flex;
	align-items: center;
	gap: 1.4rem;
}

.metric-item {
	display: flex;
	align-items: center;
	gap: 0.4rem;
}

.metric-label {
	color: var(--text-faint);
}

.metric-val {
	color: var(--text-body);
	font-family: monospace;

	&.highlight {
		color: var(--nori-teal-bright);
		font-weight: 600;
	}
}

.metric-speed {
	color: var(--text-faint);
	font-size: 1rem;
	font-family: monospace;
}

.cache-item.hit {
	.metric-label {
		color: var(--nori-teal-soft);
	}
}

.metrics-right {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.metric-addons {
	font-size: 1.05rem;
	color: var(--text-faint);
}

// 消息列表
.chat-list {
	flex: 1;
	min-height: 0;
	overflow-y: auto;
	padding: 1.6rem 2rem;
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.btn-load-earlier {
	align-self: center;
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.5rem 1.4rem;
	font-size: 1.2rem;
	color: var(--text-muted);
	background: var(--bg-card);
	border: 0.1rem solid var(--line-subtle);
	border-radius: 999rem;
	cursor: pointer;
	transition: color 0.2s, border-color 0.2s;

	&:hover:not(:disabled) {
		color: var(--text-primary);
		border-color: var(--nori-teal-bright);
	}
}

.chat-msg {
	display: flex;
	flex-direction: column;
	max-width: 82%;

	&.user {
		align-self: flex-end;
		.chat-bubble {
			background: linear-gradient(135deg, rgba(94, 234, 212, 0.22) 0%, rgba(125, 227, 255, 0.1) 100%);
			border: 0.1rem solid rgba(125, 227, 255, 0.35);
			color: var(--text-primary);
			border-radius: 1.4rem 1.4rem 0.3rem 1.4rem;
			box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.25), 0 0 1.2rem rgba(94, 234, 212, 0.1);
		}
	}

	&.assistant {
		align-self: flex-start;
		.chat-bubble {
			background: rgba(255, 255, 255, 0.05);
			border: 0.1rem solid var(--line-subtle);
			color: var(--text-primary);
			border-radius: 1.4rem 1.4rem 1.4rem 0.3rem;
			box-shadow: 0 0.4rem 1.6rem rgba(0, 0, 0, 0.2);
		}
	}
}

.chat-bubble {
	padding: 1rem 1.4rem;
	font-size: 1.3rem;
	line-height: 1.6;
	word-break: break-word;
	white-space: pre-wrap;
}

.tool-executing-hint {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1.2rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	color: var(--nori-teal-bright);
	font-size: 1.15rem;
	align-self: flex-start;
	box-shadow: 0 0.2rem 1rem rgba(0, 0, 0, 0.2);
}

.tool-icon {
	color: var(--nori-teal-bright);
}

.chat-error {
	padding: 0.6rem 1.6rem;
	color: var(--danger);
	font-size: 1.15rem;
	background: rgba(251, 60, 68, 0.1);
	border-top: 0.1rem solid rgba(251, 60, 68, 0.2);
	margin: 0;
	flex-shrink: 0;
}

// 输入框行
.chat-input-row {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	padding: 1.2rem 1.6rem;
	border-top: 0.1rem solid var(--line-subtle);
	background: rgba(8, 22, 36, 0.7);
	backdrop-filter: blur(1rem);
	flex-shrink: 0;
}

.voice-btn {
	width: 3.8rem;
	height: 3.8rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	flex-shrink: 0;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
		background: rgba(125, 227, 255, 0.08);
		transform: translateY(-0.1rem);
	}

	&.active {
		color: #ff4b4b;
		border-color: #ff4b4b;
		background: rgba(255, 75, 75, 0.15);
		animation: pulse 1.2s infinite;
	}
}

.input {
	flex: 1;
	height: 3.8rem;
	padding: 0 1.4rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 1.3rem;
	outline: none;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&::placeholder {
		color: var(--text-faint);
	}

	&:focus {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.send-btn.stop-btn {
	background-image: linear-gradient(135deg, #ff6b6b 0%, var(--danger) 100%);
	color: #fff;
}

.send-btn {
	width: 3.8rem;
	height: 3.8rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(135deg, var(--nori-teal-bright) 0%, var(--nori-teal) 100%);
	color: #03101c;
	cursor: pointer;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
	flex-shrink: 0;

	&:hover:not(:disabled) {
		box-shadow: 0 0.4rem 1.6rem var(--glow-teal-strong);
		transform: translateY(-0.15rem);
	}

	&:active:not(:disabled) {
		transform: scale(0.95);
	}

	&:disabled {
		opacity: 0.4;
		cursor: not-allowed;
		filter: grayscale(0.6);
	}
}

.btn-icon {
	color: inherit;
}

@keyframes pulse {
	0% { box-shadow: 0 0 0 0 rgba(255, 75, 75, 0.4); }
	70% { box-shadow: 0 0 0 0.8rem rgba(255, 75, 75, 0); }
	100% { box-shadow: 0 0 0 0 rgba(255, 75, 75, 0); }
}
</style>
