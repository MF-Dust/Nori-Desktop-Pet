<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import {listen, type UnlistenFn} from "../services/host/event"
import useLanguages from "../services/i18n/useLanguages.ts"
import Icon from "./Icon.vue"
import {agentEngine, type LlmUsageMetrics} from "../services/agent/engine"
import type {AgentState, AgentTextMessage} from "../services/agent/protocol"
import {StreamingJsonParser} from "../services/agent/jsonParser"
import type {PersistedChatMessage} from "../services/history"
import {skillService} from "../services/skills"
import {toolManager} from "../services/agent/tools"

const I18N = computed(() => useLanguages().views.main.chat)

// 事件: 前往设置
const emit = defineEmits<{goSettings: []}>()

// 配置键名
const KEY_BASE = "llm_api_base"
const KEY_APIKEY = "llm_api_key"
const KEY_MODEL = "llm_model"

// AI 是否已配置 (地址 + Key + 模型齐全才算)
const configured = ref(false)
const currentModel = ref("")

// 历史消息
interface Message {
	id: number
	role: string
	content: string
}
const messages = ref<Message[]>([])
const input = ref("")
const sending = ref(false)
const errorMsg = ref("")
const listRef = ref<HTMLElement>()

// Agent 状态与执行中的工具
const agentState = ref<AgentState>("idle")
const executingTool = ref<string>("")

// 上下文用量与缓存命中指标
const metrics = ref<LlmUsageMetrics | null>(null)
const showMetricsDetail = ref(false)
const activeSkillsCount = ref(0)
const activeToolsCount = ref(0)

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
interface Bubble {
	key: string
	role: string
	content: string
}
const bubbles = computed<Bubble[]>(() => {
	const LIST: Bubble[] = []
	for (const msg of messages.value) {
		if (msg.role === "assistant") {
			splitSentences(msg.content).forEach((s, i) =>
				LIST.push({key: `${msg.id}-${i}`, role: "assistant", content: s}),
			)
		} else {
			LIST.push({key: String(msg.id), role: msg.role, content: msg.content})
		}
	}
	return LIST
})

// 滚动到底部
const scrollToBottom = async () => {
	await nextTick()
	listRef.value?.scrollTo({top: listRef.value.scrollHeight})
}

// 读取字符串配置 (失败返回空串)
const readConfig = async (key: string): Promise<string> => {
	try {
		return (await invoke<string | null>("get_config", {key})) ?? ""
	} catch (error) {
		console.error(`读取配置失败: ${key}`, error)
		return ""
	}
}

// 格式化数字
const formatNum = (n?: number) => {
	if (typeof n !== "number") return "0"
	return n.toLocaleString()
}

// 计算生成速度 (Tokens/秒)
const tokensPerSecond = computed(() => {
	if (!metrics.value || metrics.value.durationMs <= 0 || metrics.value.completionTokens <= 0) return 0
	return Math.round((metrics.value.completionTokens / (metrics.value.durationMs / 1000)) * 10) / 10
})

let unlistenChatChunk: UnlistenFn | null = null
let currentStreamId = ""

onMounted(async () => {
	try {
		const [BASE, KEY, MODEL] = await Promise.all([
			readConfig(KEY_BASE),
			readConfig(KEY_APIKEY),
			readConfig(KEY_MODEL),
		])
		configured.value = !!(BASE && KEY && MODEL)
		currentModel.value = MODEL || "未知模型"

		if (configured.value) {
			const historyList = await invoke<Message[]>("get_chat_history")
			const filtered: Message[] = []
			for (const m of historyList) {
				// 旧版本会把工具调用 JSON 与系统反馈一并落库，加载时过滤掉，避免聊天区出现原始 JSON
				if (m.role === "assistant") {
					const PARSED = StreamingJsonParser.parseComplete(m.content)
					const MSG_OBJ = PARSED.find(p => p.type === "message") as AgentTextMessage | undefined
					if (MSG_OBJ?.text) {
						filtered.push({...m, content: MSG_OBJ.text})
					}
					continue
				}
				if (m.content.startsWith("【系统工具执行反馈 -")) {
					continue
				}
				filtered.push(m)
			}
			messages.value = filtered
			await scrollToBottom()
		}

		// 加载活跃技能数与工具数
		const enabledSkills = await skillService.getEnabledSkills()
		activeSkillsCount.value = enabledSkills.length
		activeToolsCount.value = toolManager.list().filter(t => t.enabled !== false).length
	} catch (error) {
		console.error("加载聊天历史失败:", error)
	}

	unlistenChatChunk = await listen("nori:chat-chunk", (event) => {
		const payload = event.payload as {streamId: string; chunk: string; done?: boolean}
		if (payload.streamId === currentStreamId && payload.chunk) {
			const lastMsg = messages.value[messages.value.length - 1]
			if (lastMsg && lastMsg.role === "assistant") {
				lastMsg.content += payload.chunk
				void scrollToBottom()
			}
		}
	})
})

onBeforeUnmount(() => {
	if (unlistenChatChunk) unlistenChatChunk()
})

// 发送消息
const send = async () => {
	const TEXT = input.value.trim()
	if (!TEXT || sending.value) return
	input.value = ""
	errorMsg.value = ""
	sending.value = true

	// 乐观显示用户消息与助手空占位消息
	messages.value.push({id: -Date.now(), role: "user", content: TEXT})
	messages.value.push({id: -Date.now() - 1, role: "assistant", content: ""})
	await scrollToBottom()

	try {
		const HISTORY = messages.value
			.slice(0, -2)
			.map(m => ({role: (m.role === "assistant" ? "assistant" : "user") as "user" | "assistant", content: m.content}))

		const FINAL = await agentEngine.run(
			TEXT,
			HISTORY,
			{},
			{
				onStateChange: (st) => {
					agentState.value = st
				},
				onToolExecuting: (toolName) => {
					executingTool.value = toolName
				},
				onToolExecuted: () => {
					executingTool.value = ""
				},
				onUsage: (usage) => {
					metrics.value = usage
				},
				onTextChunk: (chunk) => {
					const lastMsg = messages.value[messages.value.length - 1]
					if (lastMsg && lastMsg.role === "assistant") {
						lastMsg.content += chunk
						void scrollToBottom()
					}
				},
			}
		)

		const lastMsg = messages.value[messages.value.length - 1]
		if (lastMsg && lastMsg.role === "assistant") {
			lastMsg.content = FINAL.text || lastMsg.content
		}

		// 引擎内部可能经过多轮工具调用; 只把用户可见的最终一轮对话落库
		const ASSISTANT_CONTENT = FINAL.text || lastMsg?.content || ""
		if (ASSISTANT_CONTENT) {
			try {
				const SAVED_USER = await invoke<PersistedChatMessage>("save_chat_message", {role: "user", content: TEXT})
				const SAVED_ASSISTANT = await invoke<PersistedChatMessage>("save_chat_message", {role: "assistant", content: ASSISTANT_CONTENT})
				const USER_MSG = messages.value.find(m => m.role === "user" && m.content === TEXT && m.id < 0)
				if (USER_MSG) USER_MSG.id = SAVED_USER.id
				if (lastMsg && lastMsg.role === "assistant") {
					lastMsg.id = SAVED_ASSISTANT.id
				}
			} catch (error) {
				// 落库失败不阻断已完成的对话展示
				console.error("保存聊天记录失败:", error)
			}
		}

		await scrollToBottom()
	} catch (error) {
		errorMsg.value = String(error)
		console.error("聊天请求失败:", error)
		const lastMsg = messages.value[messages.value.length - 1]
		if (lastMsg && lastMsg.role === "assistant" && !lastMsg.content) {
			messages.value.pop()
		}
	} finally {
		sending.value = false
		agentState.value = "idle"
		executingTool.value = ""
	}
}

// 清空当前对话历史
const clearChatHistory = async () => {
	try {
		await invoke("clear_chat_history")
		messages.value = []
		metrics.value = null
	} catch (error) {
		console.error("清空历史记录失败:", error)
	}
}

// 语音输入控制
const isRecording = ref(false)
const toggleVoiceInput = async () => {
	const {sttService} = await import("../services/stt")
	if (isRecording.value) {
		isRecording.value = false
		const text = await sttService.stopListening()
		if (text) {
			input.value = text
			await send()
		}
	} else {
		try {
			await sttService.startListening()
			isRecording.value = true
		} catch (err) {
			console.error("启动语音识别失败:", err)
			isRecording.value = false
		}
	}
}
</script>

<template>
	<section class="chat-view">
		<!-- 未配置 AI API: 引导去设置 -->
		<div v-if="!configured" class="chat-empty">
			<h2 class="chat-empty-title glow-teal">{{ I18N.notConfigured }}</h2>
			<p class="chat-empty-desc">{{ I18N.notConfiguredDesc }}</p>
			<button class="btn-primary" @click="emit('goSettings')">
				{{ I18N.goSettings }}
			</button>
		</div>

		<!-- 已配置: 对话 -->
		<template v-else>
			<div class="chat-header-bar">
				<div class="chat-title-wrap">
					<span class="chat-header-title">与 Nori 对话中</span>
					<span v-if="currentModel" class="model-badge">
						<Icon name="sparkles" :size="11"/>
						<span>{{ currentModel }}</span>
					</span>
				</div>

				<div class="header-right-ops">
					<button class="btn-clear-chat" title="清空当前历史，开启新对话" @click="clearChatHistory">
						<Icon name="sparkles" :size="13"/>
						<span>新对话</span>
					</button>
				</div>
			</div>

			<!-- 实用信息与上下文指标条 (Context & Cache Usage Bar) -->
			<div class="chat-metrics-bar" @click="showMetricsDetail = !showMetricsDetail">
				<div class="metrics-left">
					<!-- 上下文用量 -->
					<div class="metric-item" title="当前对话上下文消耗的 Token 总量">
						<Icon name="package" :size="12"/>
						<span class="metric-label">上下文:</span>
						<span class="metric-val">{{ metrics ? formatNum(metrics.totalTokens) : '就绪' }} Tokens</span>
					</div>

					<!-- 缓存命中率 -->
					<div
						class="metric-item cache-item"
						:class="{hit: metrics && metrics.cachedTokens > 0}"
						title="大模型提示词 Prompt 缓存命中状态与节省比例"
					>
						<Icon name="zap" :size="12"/>
						<span class="metric-label">缓存命中:</span>
						<span v-if="metrics && metrics.cachedTokens > 0" class="metric-val highlight">
							{{ metrics.cacheHitRate }}% ({{ formatNum(metrics.cachedTokens) }})
						</span>
						<span v-else-if="metrics" class="metric-val text-faint">0%</span>
						<span v-else class="metric-val text-faint">自动加速中</span>
					</div>

					<!-- 响应耗时与生成速率 -->
					<div v-if="metrics && metrics.durationMs > 0" class="metric-item" title="最近一轮对话生成耗时">
						<Icon name="refresh" :size="11"/>
						<span class="metric-val">{{ (metrics.durationMs / 1000).toFixed(2) }}s</span>
						<span v-if="tokensPerSecond > 0" class="metric-speed">({{ tokensPerSecond }} t/s)</span>
					</div>
				</div>

				<div class="metrics-right">
					<span class="metric-addons" title="当前激活的技能与可用工具数">
						{{ activeSkillsCount }} 技能 · {{ activeToolsCount }} 工具
					</span>
					<Icon name="info" :size="12" class="info-icon" :class="{active: showMetricsDetail}"/>
				</div>
			</div>

			<!-- 点击展开详细指标弹窗 (Metrics Popover) -->
			<div v-if="showMetricsDetail" class="metrics-detail-box">
				<div class="detail-header">
					<div class="detail-title-wrap">
						<Icon name="package" :size="13" class="text-teal"/>
						<h4>上下文用量与缓存统计明细</h4>
					</div>
					<button class="btn-close-detail" @click.stop="showMetricsDetail = false">
						<Icon name="close" :size="12"/>
					</button>
				</div>

				<div class="detail-grid">
					<div class="detail-cell">
						<span class="cell-label">提示词输入 (Prompt)</span>
						<span class="cell-val">{{ metrics ? formatNum(metrics.promptTokens) : '0' }} Tokens</span>
					</div>
					<div class="detail-cell">
						<span class="cell-label">回复输出 (Completion)</span>
						<span class="cell-val">{{ metrics ? formatNum(metrics.completionTokens) : '0' }} Tokens</span>
					</div>
					<div class="detail-cell">
						<span class="cell-label">Prompt 缓存读取 (Cached)</span>
						<span class="cell-val text-teal">{{ metrics ? formatNum(metrics.cachedTokens) : '0' }} Tokens</span>
					</div>
					<div class="detail-cell">
						<span class="cell-label">当前缓存命中率</span>
						<span class="cell-val text-teal">{{ metrics ? metrics.cacheHitRate + '%' : '0.0%' }}</span>
					</div>
					<div class="detail-cell">
						<span class="cell-label">生成耗时与速率</span>
						<span class="cell-val">{{ metrics && metrics.durationMs > 0 ? (metrics.durationMs / 1000).toFixed(2) + 's (' + tokensPerSecond + ' t/s)' : '-' }}</span>
					</div>
					<div class="detail-cell">
						<span class="cell-label">当前运行模型</span>
						<span class="cell-val text-primary">{{ currentModel || '-' }}</span>
					</div>
				</div>

				<div class="detail-tip-box">
					<Icon name="info" :size="12" class="tip-icon"/>
					<p class="detail-tip">
						提示：当使用支持 Prompt Caching 的模型（如 DeepSeek, Claude 3.5, OpenAI 等）时，系统人设、长期记忆与工具清单会被自动缓存，大幅降低响应延迟与 API 计费。
					</p>
				</div>
			</div>

			<div ref="listRef" class="chat-list">
				<div
					v-for="bubble in bubbles"
					:key="bubble.key"
					class="chat-msg"
					:class="bubble.role"
				>
					<div class="chat-bubble">{{ bubble.content }}</div>
				</div>

				<!-- 工具调用中提示 -->
				<div v-if="executingTool" class="tool-executing-hint">
					<Icon name="loading" class="tool-icon spin"/>
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
					<Icon name="mic" class="btn-icon"/>
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
					class="send-btn"
					:disabled="sending || !input.trim()"
					@click="send"
				>
					<Icon v-if="sending" name="loading" class="btn-icon spin"/>
					<Icon v-else name="send" class="btn-icon"/>
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
}

// 未配置提示
.chat-empty {
	flex: 1;
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1.2rem;
	padding: 2rem;
	text-align: center;
}

.chat-empty-title {
	font-size: 2.2rem;
	font-weight: 700;
	color: var(--nori-teal-bright);
}

.chat-empty-desc {
	font-size: 1.3rem;
	color: var(--text-muted);
	max-width: 40rem;
	line-height: 1.6;
}

// 头部
.chat-header-bar {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 1rem 1.6rem;
	border-bottom: 0.1rem solid var(--line-subtle);
	background: rgba(10, 26, 36, 0.4);
	flex-shrink: 0;
}

.chat-title-wrap {
	display: flex;
	align-items: center;
	gap: 0.8rem;
}

.chat-header-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.model-badge {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	padding: 0.2rem 0.6rem;
	border-radius: 1rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	color: var(--nori-teal-bright);
	font-size: 1.1rem;
	font-family: monospace;
}

.header-right-ops {
	display: flex;
	align-items: center;
	gap: 0.6rem;
}

.btn-clear-chat {
	display: inline-flex;
	align-items: center;
	gap: 0.4rem;
	padding: 0.4rem 0.9rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	font-size: 1.15rem;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}
}

// 实用信息与上下文指标条
.chat-metrics-bar {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.4rem 1.6rem;
	background: rgba(0, 0, 0, 0.25);
	border-bottom: 0.1rem solid var(--line-subtle);
	font-size: 1.1rem;
	color: var(--text-muted);
	cursor: pointer;
	user-select: none;
	flex-shrink: 0;
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.04);
		color: var(--text-primary);
	}
}

.metrics-left {
	display: flex;
	align-items: center;
	gap: 1.2rem;
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

	&.text-faint {
		color: var(--text-faint);
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

.info-icon {
	color: var(--text-faint);
	transition: transform 0.2s ease;

	&.active {
		color: var(--nori-teal-bright);
		transform: rotate(180deg);
	}
}

// 指标详情弹窗
.metrics-detail-box {
	position: absolute;
	top: 7.2rem;
	left: 1.6rem;
	right: 1.6rem;
	background: #091c29;
	border: 0.1rem solid var(--nori-teal-soft);
	border-radius: var(--radius-sm);
	padding: 1.2rem 1.4rem;
	z-index: 20;
	box-shadow: 0 0.8rem 2.4rem rgba(0, 0, 0, 0.6);
	animation: slideDown 0.2s ease-out;
}

@keyframes slideDown {
	from {
		opacity: 0;
		transform: translateY(-0.6rem);
	}
	to {
		opacity: 1;
		transform: translateY(0);
	}
}

.detail-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	margin-bottom: 1rem;

	h4 {
		font-size: 1.25rem;
		font-weight: 600;
		color: var(--text-primary);
	}
}

.btn-close-detail {
	background: transparent;
	border: none;
	color: var(--text-faint);
	cursor: pointer;

	&:hover {
		color: var(--text-primary);
	}
}

.detail-grid {
	display: grid;
	grid-template-columns: 1fr 1fr;
	gap: 0.8rem;
	margin-bottom: 0.8rem;
}

.detail-cell {
	display: flex;
	flex-direction: column;
	gap: 0.2rem;
	padding: 0.6rem 0.8rem;
	background: rgba(255, 255, 255, 0.03);
	border-radius: var(--radius-sm);
}

.cell-label {
	font-size: 1.05rem;
	color: var(--text-faint);
}

.cell-val {
	font-size: 1.2rem;
	font-family: monospace;
	font-weight: 500;
	color: var(--text-primary);

	&.text-teal {
		color: var(--nori-teal-bright);
	}
}

.detail-title-wrap {
	display: flex;
	align-items: center;
	gap: 0.6rem;

	h4 {
		font-size: 1.25rem;
		font-weight: 600;
		color: var(--text-primary);
		margin: 0;
	}
}

.detail-tip-box {
	display: flex;
	align-items: flex-start;
	gap: 0.6rem;
	border-top: 0.1rem solid var(--line-subtle);
	padding-top: 0.8rem;
}

.tip-icon {
	color: var(--nori-teal-soft);
	margin-top: 0.2rem;
	flex-shrink: 0;
}

.detail-tip {
	font-size: 1.1rem;
	color: var(--text-muted);
	line-height: 1.5;
	margin: 0;
}

// 消息列表
.chat-list {
	flex: 1;
	min-height: 0;
	overflow-y: auto;
	padding: 1.6rem;
	display: flex;
	flex-direction: column;
	gap: 1rem;
}

.chat-msg {
	display: flex;
	flex-direction: column;
	max-width: 80%;

	&.user {
		align-self: flex-end;
		.chat-bubble {
			background: linear-gradient(135deg, rgba(125, 227, 255, 0.2), rgba(125, 227, 255, 0.08));
			border: 0.1rem solid var(--nori-teal-soft);
			color: var(--text-primary);
			border-bottom-right-radius: 0.2rem;
		}
	}

	&.assistant {
		align-self: flex-start;
		.chat-bubble {
			background: rgba(255, 255, 255, 0.06);
			border: 0.1rem solid var(--line-subtle);
			color: var(--text-primary);
			border-bottom-left-radius: 0.2rem;
		}
	}
}

.chat-bubble {
	padding: 0.8rem 1.2rem;
	border-radius: var(--radius-sm);
	font-size: 1.25rem;
	line-height: 1.5;
	word-break: break-word;
	white-space: pre-wrap;
}

.tool-executing-hint {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.6rem 1rem;
	background: rgba(125, 227, 255, 0.08);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--nori-teal-bright);
	font-size: 1.15rem;
	align-self: flex-start;
}

.tool-icon {
	color: var(--nori-teal-bright);
}

.chat-error {
	padding: 0.6rem 1.6rem;
	color: var(--danger);
	font-size: 1.15rem;
	background: rgba(255, 75, 75, 0.08);
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
	background: rgba(10, 26, 36, 0.4);
	flex-shrink: 0;
}

.voice-btn {
	width: 3.6rem;
	height: 3.6rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
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
	height: 3.6rem;
	padding: 0 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-primary);
	font-size: 1.25rem;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
	}
}

.send-btn {
	width: 3.6rem;
	height: 3.6rem;
	display: flex;
	align-items: center;
	justify-content: center;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	cursor: pointer;
	transition: all 0.2s ease;

	&:hover:not(:disabled) {
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}

	&:disabled {
		opacity: 0.4;
		cursor: not-allowed;
	}
}

.btn-icon {
	width: 1.6rem;
	height: 1.6rem;
}

.spin {
	animation: spin 1s linear infinite;
}

@keyframes spin {
	from { transform: rotate(0deg); }
	to { transform: rotate(360deg); }
}

@keyframes pulse {
	0% { box-shadow: 0 0 0 0 rgba(255, 75, 75, 0.4); }
	70% { box-shadow: 0 0 0 0.8rem rgba(255, 75, 75, 0); }
	100% { box-shadow: 0 0 0 0 rgba(255, 75, 75, 0); }
}
</style>
