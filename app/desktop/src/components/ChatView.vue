<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref} from "vue"
import {invoke} from "../services/host/invoke"
import {listen, type UnlistenFn} from "../services/host/event"
import useLanguages from "../services/i18n/useLanguages.ts"
import Icon from "./Icon.vue"
import {agentEngine} from "../services/agent/engine"
import type {AgentState} from "../services/agent/protocol"

const I18N = computed(() => useLanguages().views.main.chat)

// 事件: 前往设置
const emit = defineEmits<{goSettings: []}>()

// 配置键名
const KEY_BASE = "llm_api_base"
const KEY_APIKEY = "llm_api_key"
const KEY_MODEL = "llm_model"

// AI 是否已配置 (地址 + Key + 模型齐全才算)
const configured = ref(false)

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
		if (configured.value) {
			messages.value = await invoke<Message[]>("get_chat_history")
			await scrollToBottom()
		}
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

// 语音输入控制
const isRecording = ref(false)
const toggleVoiceInput = async () => {
	const {sttService} = await import("../services/stt")
	if (isRecording.value) {
		isRecording.value = false
		const text = await sttService.stopListening()
		if (text.trim()) {
			input.value = (input.value ? input.value + " " : "") + text.trim()
		}
	} else {
		isRecording.value = true
		try {
			await sttService.startListening({
				onInterim: (interim) => {
					input.value = interim
				},
				onFinal: (finalText) => {
					input.value = finalText
				},
				onError: (err) => {
					console.error("STT Error:", err)
					isRecording.value = false
				},
			})
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
	color: var(--text-primary);
}

.chat-empty-desc {
	font-size: 1.2rem;
	color: var(--text-faint);
	line-height: 1.6;
}

// 消息列表
.chat-list {
	flex: 1;
	min-height: 0;
	overflow-y: auto;
	display: flex;
	flex-direction: column;
	gap: 1rem;
	padding: 1.6rem 1.2rem;
	scrollbar-width: none;

	&::-webkit-scrollbar {
		display: none;
	}
}

.chat-msg {
	display: flex;

	&.user {
		justify-content: flex-end;
	}

	&.assistant {
		justify-content: flex-start;
	}
}

.chat-bubble {
	max-width: 72%;
	padding: 0.9rem 1.2rem;
	border-radius: var(--radius-sm);
	font-size: 1.3rem;
	line-height: 1.6;
	word-break: break-word;
	white-space: pre-wrap;

	.user & {
		background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
		color: #05121a;
	}

	.assistant & {
		background: rgba(255, 255, 255, 0.06);
		color: var(--text-body);
		border: 0.1rem solid var(--line-subtle);
	}
}

.chat-error {
	padding: 0 1.2rem;
	font-size: 1.1rem;
	color: var(--danger);
}

// 输入区
.chat-input-row {
	padding: 1rem 1.2rem 1.4rem;
	display: flex;
	gap: 0.8rem;
}

.input {
	flex: 1;
	padding: 0.9rem 1.2rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	outline: none;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
		box-shadow: 0 0 0.8rem var(--glow-teal-soft);
	}
}

.input::placeholder {
	color: var(--text-muted);
	opacity: 0.6;
}

.voice-btn {
	width: 4rem;
	height: 4rem;
	flex-shrink: 0;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.04);
	color: var(--text-muted);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s ease;

	&:hover {
		color: var(--nori-teal-bright);
		border-color: var(--nori-teal-soft);
	}

	&.active {
		background: rgba(255, 75, 75, 0.15);
		border-color: #ff4b4b;
		color: #ff4b4b;
		animation: pulse-recording 1.2s infinite;
	}
}

@keyframes pulse-recording {
	0%, 100% {
		box-shadow: 0 0 0.4rem rgba(255, 75, 75, 0.3);
	}
	50% {
		box-shadow: 0 0 1.2rem rgba(255, 75, 75, 0.8);
	}
}

.send-btn {
	width: 4rem;
	height: 4rem;
	flex-shrink: 0;
	border: none;
	border-radius: var(--radius-sm);
	background-image: linear-gradient(90deg, var(--nori-teal-bright), var(--nori-teal));
	color: #05121a;
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.2s ease;

	&:hover:not(:disabled) {
		box-shadow: 0 0 1.6rem var(--glow-teal-soft);
	}

	&:disabled {
		opacity: 0.6;
		cursor: default;
	}
}

.btn-icon {
	width: 1.6rem;
	height: 1.6rem;
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
	margin-top: 0.4rem;
	align-self: flex-start;
}

.tool-icon {
	width: 1.2rem;
	height: 1.2rem;
}
</style>
