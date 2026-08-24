<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from "vue"
import {useTextareaAutosize} from "@vueuse/core"
import useLanguages from "../services/i18n/useLanguages.ts"
import Icon from "./Icon.vue"
import AppChip from "./ui/AppChip.vue"
import AppEmpty from "./ui/AppEmpty.vue"
import AppButton from "./ui/AppButton.vue"
import AppModal from "./ui/AppModal.vue"
import {RUNTIME} from "../services/runtime"
import {createChatStore} from "../services/runtime/chatStore"
import {renderMarkdown} from "../services/chat/markdown"
import {splitAssistantMessage} from "../services/chat/split"
import {feedback} from "../services/feedback"
import type {IconName} from "../services/icon"

const I18N = computed(() => useLanguages().views.main.chat)
const UI_I18N = computed(() => useLanguages().components.ui.state)

// 事件: 前往设置
const emit = defineEmits<{goSettings: []}>()

// AI 是否已配置 (后端快照判定)
const configured = computed(() => RUNTIME.snapshot.value?.ai.configured ?? false)
const currentModel = computed(() => {
	const MODEL = RUNTIME.snapshot.value?.ai.model
	return MODEL && MODEL.length > 0 ? MODEL : I18N.value.unknownModel
})

// 聊天状态机 (业务在后端, 这里只渲染投影)
const CHAT = createChatStore()
const {bubbles, sending, executingTool, metrics, errorMsg, failedInput, statusCode, pendingApprovals, agentState} = CHAT

const listRef = ref<HTMLElement>()
const copiedBubbleKey = ref<string | null>(null)

// 输入框自适应高度 (1 行起, 最多 10rem)
const {textarea: inputRef, input} = useTextareaAutosize({styleProp: "height"})

// 活跃技能数与工具数 (来自快照)
const activeSkillsCount = computed(() => RUNTIME.snapshot.value?.enabledSkillsCount ?? 0)
const activeToolsCount = computed(() =>
	(RUNTIME.snapshot.value?.tools ?? []).filter(tool => tool.enabled).length)

// 预设对话引导词 (Prompt Chips)
const QUICK_PROMPTS = computed<{icon: IconName; text: string}[]>(() => [
	{icon: "sparkles", text: I18N.value.starter.greeting},
	{icon: "noriOS", text: I18N.value.starter.weather},
	{icon: "play", text: I18N.value.starter.motion},
	{icon: "cpu", text: I18N.value.starter.ability},
])

// 展示列表: 助手一条回复 = 多个气泡 (流式期间不拆, 完成后一次成型, 避免边流边重排)
interface DisplayBubble {
	key: string
	role: string
	content: string
	html?: string
	isFirstInGroup: boolean
}

const MAX_RENDERED_BUBBLES = 500
const MARKDOWN_CACHE = new Map<string, string>()
const markdownFor = (key: string, content: string): string => {
	const CACHE_KEY = `${key}:${content}`
	const CACHED = MARKDOWN_CACHE.get(CACHE_KEY)
	if (CACHED !== undefined) return CACHED
	const HTML = renderMarkdown(content)
	MARKDOWN_CACHE.set(CACHE_KEY, HTML)
	if (MARKDOWN_CACHE.size > 512) MARKDOWN_CACHE.delete(MARKDOWN_CACHE.keys().next().value as string)
	return HTML
}

const displayBubbles = computed<DisplayBubble[]>(() => {
	const LIST: DisplayBubble[] = []
	const SOURCE = bubbles.value.length > MAX_RENDERED_BUBBLES
		? bubbles.value.slice(-MAX_RENDERED_BUBBLES)
		: bubbles.value
	const LAST_INDEX = SOURCE.length - 1
	SOURCE.forEach((msg, index) => {
		if (msg.role !== "assistant") {
			LIST.push({key: msg.key, role: msg.role, content: msg.content, isFirstInGroup: true})
			return
		}
		const STREAMING = sending.value && index === LAST_INDEX
		const SLICES = splitAssistantMessage(msg.content, {streaming: STREAMING})
		SLICES.forEach((slice, sliceIndex) => LIST.push({
			key: `${msg.key}-${sliceIndex}`,
			role: "assistant",
			content: slice,
			html: markdownFor(`${msg.key}-${sliceIndex}`, slice),
			isFirstInGroup: sliceIndex === 0,
		}))
	})
	return LIST
})

// ---- 滚动与未读 ----
const isScrolledUp = ref(false)
const unreadCount = ref(0)

const scrollToBottom = async (smooth = true) => {
	await nextTick()
	const REDUCED_MOTION = typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
	if (listRef.value) {
		listRef.value.scrollTo({
			top: listRef.value.scrollHeight,
			behavior: smooth && !REDUCED_MOTION ? "smooth" : "auto",
		})
	}
	isScrolledUp.value = false
	unreadCount.value = 0
}

const handleListScroll = () => {
	if (!listRef.value) return
	const {scrollTop, scrollHeight, clientHeight} = listRef.value
	const DIST_FROM_BOTTOM = scrollHeight - scrollTop - clientHeight
	isScrolledUp.value = DIST_FROM_BOTTOM > 120
	if (!isScrolledUp.value) unreadCount.value = 0
}

// 流式输出期间合并滚动调度; 用户翻到上面时只累计未读, 不硬拽视口
let scrollPending = false
const scheduleScrollToBottom = () => {
	if (isScrolledUp.value) return
	if (scrollPending) return
	scrollPending = true
	requestAnimationFrame(() => {
		scrollPending = false
		void scrollToBottom(false)
	})
}

watch(() => bubbles.value.length, (next, previous) => {
	if (isScrolledUp.value && next > previous) unreadCount.value += next - previous
	scheduleScrollToBottom()
})
watch(() => bubbles.value.at(-1)?.content, () => scheduleScrollToBottom())

// 屏幕阅读器播报的运行状态
const stateText = computed(() => {
	if (executingTool.value) return `${I18N.value.toolExecuting}: ${executingTool.value}`
	if (agentState.value === "streaming" || sending.value) return I18N.value.sending
	return ""
})
const statusText = computed(() => {
	if (statusCode.value === "cancelled") return I18N.value.cancelled
	if (statusCode.value === "approval-timeout") return I18N.value.approvalTimeout
	return ""
})

watch(failedInput, value => {
	if (value && !input.value.trim()) input.value = value
})

// 格式化数字与速度
const formatNum = (value?: number) => (typeof value === "number" ? value.toLocaleString() : "0")
const tokensPerSecond = computed(() => {
	if (!metrics.value || metrics.value.durationMs <= 0 || metrics.value.completionTokens <= 0) return 0
	return Math.round((metrics.value.completionTokens / (metrics.value.durationMs / 1000)) * 10) / 10
})

// 逐调用工具授权: 队列首项使用 AppModal 展示, 倒计时由 store 每秒更新。
const activeApproval = computed(() => pendingApprovals.value[0] ?? null)
const approvalVisible = computed({
	get: () => activeApproval.value !== null,
	set: (value: boolean) => {
		if (value || !activeApproval.value) return
		void CHAT.decideApproval(activeApproval.value.request.requestId, false).catch(error => {
			feedback.error(I18N.value.cancelFailed, error)
		})
	},
})
const approvalSeconds = computed(() => activeApproval.value?.remainingSeconds ?? 0)

const decideActiveApproval = async (approved: boolean): Promise<void> => {
	const REQUEST = activeApproval.value?.request
	if (!REQUEST) return
	try {
		await CHAT.decideApproval(REQUEST.requestId, approved)
	} catch (error) {
		feedback.error(I18N.value.cancelFailed, error)
	}
}

// 历史翻页
const loadOlderHistory = async () => {
	const BEFORE = listRef.value?.scrollHeight ?? 0
	try {
		await CHAT.loadOlder()
		// 保持视口停在原来的位置, 不要因为插入历史而跳走
		await nextTick()
		if (listRef.value) listRef.value.scrollTop += listRef.value.scrollHeight - BEFORE
	} catch (error) {
		feedback.error(I18N.value.loadEarlierFailed, error)
	}
}

// 复制单条消息内容
const copyMessage = async (key: string, content: string) => {
	try {
		await RUNTIME.copyText(content)
		copiedBubbleKey.value = key
		setTimeout(() => {
			if (copiedBubbleKey.value === key) copiedBubbleKey.value = null
		}, 1500)
	} catch (error) {
		feedback.error(I18N.value.copyFailed, error)
	}
}

// Markdown 里的外链交给宿主打开, 不在 WebView 内导航 (否则会把界面顶掉)
const onBubbleClick = async (event: MouseEvent) => {
	const ANCHOR = (event.target as HTMLElement | null)?.closest("a[data-external]") as HTMLAnchorElement | null
	if (!ANCHOR) return
	event.preventDefault()
	try {
		await RUNTIME.openUrl(ANCHOR.href)
	} catch (error) {
		feedback.error(I18N.value.openLinkFailed, error)
	}
}

let unlistenAgent: (() => void) | null = null

onMounted(async () => {
	await RUNTIME.init()
	unlistenAgent = await CHAT.connect()
	if (RUNTIME.snapshot.value?.ai.configured) {
		try {
			await CHAT.loadRecent()
			await scrollToBottom(false)
		} catch (error) {
			feedback.error(I18N.value.loadEarlierFailed, error)
		}
	}
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
	await scrollToBottom(true)
}

// 点击快速 Prompt 发送
const sendQuickPrompt = (text: string) => {
	input.value = text
	void send()
}

// 键盘按键处理: Enter 发送, Shift+Enter 换行
// 中文输入法选字时的 Enter 不能当发送 (isComposing / keyCode 229 两道保险)
const handleKeyDown = (event: KeyboardEvent) => {
	if (event.key !== "Enter" || event.shiftKey) return
	if (event.isComposing || event.keyCode === 229) return
	event.preventDefault()
	void send()
}

// 停止当前生成
const stopGeneration = async () => {
	try {
		await CHAT.abort()
		feedback.info(I18N.value.cancelled)
	} catch (error) {
		feedback.error(I18N.value.cancelFailed, error)
	}
}

// 清空当前对话历史
const clearChatHistory = async () => {
	try {
		await CHAT.clear()
	} catch (error) {
		feedback.error(I18N.value.clearFailed, error)
	}
}

const retryLast = async () => {
	if (!failedInput.value) return
	input.value = failedInput.value
	await CHAT.retryLast()
	await scrollToBottom(true)
}

// ---- 语音输入状态机: idle → recording → transcribing → idle ----
type VoiceState = "idle" | "recording" | "transcribing"
const voiceState = ref<VoiceState>("idle")
const recordSeconds = ref(0)
let recordTimer: ReturnType<typeof setInterval> | null = null

const stopRecordTimer = () => {
	if (recordTimer) clearInterval(recordTimer)
	recordTimer = null
	recordSeconds.value = 0
}

onBeforeUnmount(stopRecordTimer)

const recordLabel = computed(() => {
	const MINUTES = Math.floor(recordSeconds.value / 60).toString().padStart(2, "0")
	const SECONDS = (recordSeconds.value % 60).toString().padStart(2, "0")
	return `${MINUTES}:${SECONDS}`
})

const voiceTitle = computed(() => {
	if (voiceState.value === "recording") return I18N.value.voice.stop
	if (voiceState.value === "transcribing") return I18N.value.voice.transcribing
	return I18N.value.voice.start
})

const toggleVoiceInput = async () => {
	if (voiceState.value === "transcribing") return

	if (voiceState.value === "recording") {
		voiceState.value = "transcribing"
		stopRecordTimer()
		try {
			const RESULT = await RUNTIME.sttStop()
			if (RESULT.text) {
				input.value = RESULT.text
				await send()
			}
		} catch (error) {
			feedback.error(I18N.value.voice.failed, error)
		} finally {
			voiceState.value = "idle"
		}
		return
	}

	try {
		await RUNTIME.sttStart()
		voiceState.value = "recording"
		recordSeconds.value = 0
		recordTimer = setInterval(() => {
			recordSeconds.value += 1
		}, 1000)
	} catch (error) {
		voiceState.value = "idle"
		stopRecordTimer()
		feedback.error(I18N.value.voice.startFailed, error)
	}
}
</script>

<template>
	<section class="w-full h-full min-h-0 flex flex-col relative overflow-hidden glass-panel rounded-lg shadow-[0_0.8rem_3.2rem_rgba(0,0,0,0.45)]">
		<!-- 未配置时提示 -->
		<AppEmpty v-if="!configured" icon="cpu" :title="I18N.emptyTitle" :desc="I18N.emptyDesc" large>
			<AppButton variant="primary" icon="settings" @click="emit('goSettings')">{{ I18N.goSettings }}</AppButton>
		</AppEmpty>

		<!-- 已配置: 聊天界面 -->
		<template v-else>
			<!-- 顶部标题与控制 -->
			<div class="shrink-0 flex items-center justify-between gap-3 px-4.5 py-3 border-b border-line-subtle bg-bg-deep/70 backdrop-blur-[1.2rem]">
				<div class="flex items-center gap-2.5 min-w-0">
					<span class="title-sm truncate text-text-primary">{{ I18N.title }}</span>
					<AppChip tone="teal" dot class="mono font-500">{{ currentModel }}</AppChip>
				</div>

				<n-popconfirm
					:positive-text="I18N.clearConfirmYes"
					:negative-text="UI_I18N.cancel"
					@positive-click="clearChatHistory"
				>
					<template #trigger>
						<button type="button" class="btn-ghost px-3 py-1.5 text-xs text-text-muted hover:text-danger-text hover:border-danger/40" :title="I18N.clearHistory" :aria-label="I18N.clearHistory">
							<Icon name="trash" :size="12"/>
							<span>{{ I18N.clearHistory }}</span>
						</button>
					</template>
					{{ I18N.clearConfirm }}
				</n-popconfirm>
			</div>

			<!-- 性能指标与 Prompt Caching 遥测 HUD 条 -->
			<div class="shrink-0 flex items-center justify-between gap-3 px-4.5 py-1.2 text-xs text-text-muted select-none bg-bg-abyss/85 border-b border-line-subtle backdrop-blur-[0.8rem]">
				<div class="flex items-center gap-4">
					<span class="flex items-center gap-1.2">
						<span class="text-text-faint">{{ I18N.metrics.context }}:</span>
						<span
							class="mono"
							:class="metrics && metrics.totalTokens > 0 ? 'text-nori-teal-bright font-600' : 'text-text-body'"
						>{{ metrics ? formatNum(metrics.totalTokens) : "0" }} {{ I18N.tokens }}</span>
						<span v-if="metrics && metrics.durationMs > 0" class="mono text-text-faint">({{ tokensPerSecond }} t/s)</span>
					</span>

					<span class="flex items-center gap-1.2">
						<span :class="metrics && metrics.cachedTokens > 0 ? 'text-nori-teal-soft' : 'text-text-faint'">{{ I18N.metrics.cache }}:</span>
						<span
							class="mono"
							:class="metrics && metrics.cachedTokens > 0 ? 'text-nori-teal-bright font-600' : 'text-text-body'"
						>{{ metrics ? metrics.cacheHitRate + "%" : "0%" }}</span>
					</span>
				</div>

				<span class="text-text-faint font-500">
					{{ activeSkillsCount }} {{ I18N.metrics.skills }} / {{ activeToolsCount }} {{ I18N.metrics.tools }}
				</span>
			</div>

			<!-- 消息滚动流 -->
			<div
				ref="listRef"
				class="flex-1 scroll-area flex flex-col gap-2.5 px-5 py-4 relative"
				role="log"
				aria-live="polite"
				aria-relevant="additions text"
				@scroll="handleListScroll"
				@click="onBubbleClick"
			>
				<!-- 更早的历史按需加载 -->
				<button
					v-if="CHAT.hasMoreHistory.value"
					type="button"
					class="btn-ghost self-center rounded-pill px-4 py-1 text-xs"
					:disabled="CHAT.loadingHistory.value"
					@click="loadOlderHistory"
				>
					<Icon
						:name="CHAT.loadingHistory.value ? 'loading' : 'arrow-up'"
						:class="{spin: CHAT.loadingHistory.value}"
						:size="12"
					/>
					<span>{{ I18N.loadEarlier }}</span>
				</button>

				<!-- 空历史时的快捷破冰卡片 -->
				<div v-if="displayBubbles.length === 0" class="my-auto flex flex-col items-center gap-5 px-3 py-8">
					<div class="flex flex-col items-center gap-2 text-center">
						<span class="w-12 h-12 rounded-full flex items-center justify-center bg-nori-teal-bright/10 border border-nori-teal-bright/30 text-nori-teal-bright shadow-[0_0_2rem_var(--glow-teal-soft)]">
							<Icon name="sparkles" :size="24"/>
						</span>
						<h3 class="title-md text-text-primary">{{ I18N.starter.title }}</h3>
						<p class="text-sub max-w-[34rem]">{{ I18N.starter.desc }}</p>
					</div>

					<div class="grid grid-cols-1 md:grid-cols-2 gap-3 w-full max-w-[48rem]">
						<button
							v-for="item in QUICK_PROMPTS"
							:key="item.text"
							type="button"
							class="group inline-flex items-center gap-3 px-4 py-3 rounded-md text-left text-sm text-text-body
								bg-white/4 border border-line-subtle backdrop-blur-[1rem] cursor-pointer transition-all duration-200 focus-ring
								hover:(bg-nori-teal-bright/10 border-nori-teal-soft/80 text-nori-teal-bright shadow-[0_0.4rem_1.6rem_rgba(125,227,255,0.12)] -translate-y-[0.15rem])"
							@click="sendQuickPrompt(item.text)"
						>
							<span class="w-7 h-7 rounded-sm flex items-center justify-center bg-white/6 border border-line-subtle text-nori-teal-bright transition-transform duration-200 group-hover:scale-110">
								<Icon :name="item.icon" :size="14"/>
							</span>
							<span class="font-500">{{ item.text }}</span>
						</button>
					</div>
				</div>

				<!-- 气泡消息列表 (助手连发多条气泡) -->
				<div
					v-for="bubble in displayBubbles"
					:key="bubble.key"
					v-memo="[bubble.content, bubble.html, bubble.isFirstInGroup]"
					class="group flex max-w-[84%] animate-bubble-in"
					:class="[
						bubble.role === 'user' ? 'self-end flex-row-reverse' : 'self-start',
						bubble.isFirstInGroup && bubble.role === 'assistant' ? 'mt-1' : '',
					]"
				>
					<div class="relative flex items-center gap-1.5">
						<div
							class="px-4.5 py-3 text-base leading-relaxed break-words"
							:class="bubble.role === 'user'
								? 'rounded-[1.4rem_1.4rem_0.4rem_1.4rem] text-text-primary border border-nori-teal-bright/40 bg-[linear-gradient(135deg,rgba(94,234,212,0.22)_0%,rgba(125,227,255,0.1)_100%)] shadow-[0_0.4rem_2rem_rgba(94,234,212,0.12)] whitespace-pre-wrap'
								: 'rounded-[1.4rem_1.4rem_1.4rem_0.4rem] text-text-primary border border-line-subtle bg-bg-card/85 backdrop-blur-[1.2rem] shadow-[0_0.4rem_2rem_rgba(0,0,0,0.35)] chat-markdown'"
						>
							<span v-if="bubble.role === 'user'">{{ bubble.content }}</span>
							<!-- eslint-disable-next-line vue/no-v-html -->
							<div v-else v-html="bubble.html"/>
						</div>

						<button
							type="button"
							class="btn-icon w-6.5 h-6.5 shrink-0 opacity-0 pointer-events-none transition-opacity duration-200
								group-hover:(opacity-100 pointer-events-auto) group-focus-within:(opacity-100 pointer-events-auto)"
							:class="copiedBubbleKey === bubble.key ? 'opacity-100 pointer-events-auto text-success' : ''"
							:title="copiedBubbleKey === bubble.key ? UI_I18N.copied : UI_I18N.copy"
							:aria-label="copiedBubbleKey === bubble.key ? UI_I18N.copied : UI_I18N.copy"
							@click="copyMessage(bubble.key, bubble.content)"
						>
							<Icon :name="copiedBubbleKey === bubble.key ? 'check' : 'copy'" :size="12"/>
						</button>
					</div>
				</div>

				<!-- 工具调用中提示 -->
				<div v-if="executingTool" class="self-start mt-1">
					<AppChip tone="teal" icon="loading">
						<span>{{ I18N.toolExecuting }}: {{ executingTool }}</span>
					</AppChip>
				</div>
			</div>

			<!-- 供屏幕阅读器播报的运行状态 -->
			<span class="sr-only" role="status" aria-live="polite">{{ stateText }}</span>

			<!-- 浮动「回到底部」指示器 (带未读计数) -->
			<Transition name="fade-scale">
				<button
					v-if="isScrolledUp"
					type="button"
					class="absolute bottom-[8rem] right-6 z-10 inline-flex items-center gap-1.5 px-3.5 py-1.8 rounded-pill
						text-xs font-500 text-nori-teal-bright bg-bg-deep/95 border border-nori-teal-soft cursor-pointer
						backdrop-blur-[1rem] shadow-[0_0.4rem_2rem_rgba(0,0,0,0.5),0_0_1.2rem_var(--glow-teal-soft)] focus-ring
						transition-all duration-200 hover:-translate-y-[0.15rem]"
					:title="I18N.backToLatest"
					@click="scrollToBottom(true)"
				>
					<Icon name="arrow-down" :size="14"/>
					<span>{{ unreadCount > 0 ? `${I18N.newMessages} (${unreadCount})` : I18N.backToLatest }}</span>
				</button>
			</Transition>

			<p
				v-if="statusText"
				class="shrink-0 m-0 px-4 py-1.5 text-xs text-text-muted bg-white/3 border-t border-line-subtle"
				role="status"
			>{{ statusText }}</p>

			<div
				v-if="errorMsg"
				class="shrink-0 flex items-center justify-between gap-3 m-0 px-4 py-1.5 text-xs text-danger-text bg-danger/10 border-t border-danger/20"
				role="alert"
			>
				<span class="min-w-0 break-words">{{ errorMsg }}</span>
				<AppButton v-if="failedInput" size="sm" icon="refresh" @click="retryLast">{{ I18N.retryLast }}</AppButton>
			</div>

			<!-- 底部输入控制台 (自适应多行, Enter 发送, Shift+Enter 换行) -->
			<div class="relative shrink-0 flex items-end gap-3 px-4.5 py-3.5 border-t border-line-subtle bg-bg-deep/85 backdrop-blur-[1.4rem]">
				<span class="absolute top-0 inset-x-0 h-[0.1rem] bg-gradient-to-r from-transparent via-nori-teal-bright/22 to-transparent pointer-events-none"/>

				<!-- 语音输入按钮 -->
				<button
					type="button"
					class="w-[4rem] h-[4rem] shrink-0 flex flex-col items-center justify-center gap-0.5 rounded-sm
						border border-line-subtle bg-white/4 text-text-muted cursor-pointer transition-all duration-200 focus-ring
						hover:(text-nori-teal-bright border-nori-teal-soft bg-nori-teal-bright/10 shadow-[0_0_1.4rem_rgba(125,227,255,0.12)])"
					:class="voiceState === 'recording' ? 'text-danger-text border-danger bg-danger/15 animate-pulse-soft' : ''"
					:title="voiceTitle"
					:aria-label="voiceTitle"
					:disabled="voiceState === 'transcribing'"
					@click="toggleVoiceInput"
				>
					<Icon :name="voiceState === 'transcribing' ? 'loading' : 'mic'" :class="{spin: voiceState === 'transcribing'}" :size="16"/>
					<span v-if="voiceState === 'recording'" class="text-xs mono font-600">{{ recordLabel }}</span>
				</button>

				<!-- 动态自适应输入区 -->
				<textarea
					ref="inputRef"
					v-model="input"
					class="input-base flex-1 min-h-[4rem] max-h-[12rem] resize-none leading-relaxed shadow-inner"
					rows="1"
					:placeholder="I18N.inputPlaceholder"
					spellcheck="false"
					:aria-label="I18N.inputPlaceholder"
					@keydown="handleKeyDown"
				/>

				<!-- 停止生成按钮 -->
				<button
					v-if="sending"
					type="button"
					class="w-[4rem] h-[4rem] shrink-0 flex items-center justify-center rounded-sm border-none cursor-pointer
						text-text-primary bg-gradient-to-br from-danger-text to-danger shadow-[0_0.4rem_1.6rem_rgba(251,60,68,0.35)] transition-all duration-200 focus-ring
						hover:-translate-y-[0.15rem]"
					:title="I18N.stopGeneration"
					:aria-label="I18N.stopGeneration"
					@click="stopGeneration"
				>
					<Icon name="close" :size="16"/>
				</button>

				<!-- 发送按钮 -->
				<button
					v-else
					type="button"
					class="w-[4rem] h-[4rem] shrink-0 flex items-center justify-center rounded-sm border-none cursor-pointer
						text-on-teal bg-gradient-to-r from-nori-teal-bright via-nori-teal to-nori-teal-soft transition-all duration-200 focus-ring
						shadow-[0_0.4rem_1.6rem_var(--glow-teal-soft)]
						hover:not-disabled:(-translate-y-[0.15rem] brightness-110 shadow-[0_0.6rem_2.2rem_var(--glow-teal)])
						active:not-disabled:scale-95 disabled:(opacity-40 cursor-not-allowed grayscale-60)"
					:disabled="!input.trim()"
					:title="I18N.title"
					:aria-label="I18N.title"
					@click="send"
				>
					<Icon name="send" :size="16"/>
				</button>
			</div>
		</template>
	</section>

	<AppModal
		v-model:show="approvalVisible"
		:title="I18N.approvalTitle"
		:close-label="I18N.deny"
		:mask-closable="false"
	>
		<template v-if="activeApproval">
			<p class="m-0 text-base font-600 text-nori-teal-bright">
				{{ activeApproval.request.toolName }}
			</p>
			<p v-if="activeApproval.request.description" class="m-0 text-sm text-text-muted">
				{{ activeApproval.request.description }}
			</p>
			<pre class="m-0 max-h-[16rem] overflow-auto rounded-sm bg-white/5 p-2.5 text-sm leading-relaxed whitespace-pre-wrap mono">{{ JSON.stringify(activeApproval.request.arguments ?? {}, null, 2) }}</pre>
			<p class="m-0 text-xs text-text-muted" role="status" aria-live="polite">
				{{ I18N.approvalCountdown }}: {{ approvalSeconds }}
			</p>
		</template>
		<template #footer>
			<AppButton variant="ghost" @click="decideActiveApproval(false)">{{ I18N.deny }}</AppButton>
			<AppButton variant="primary" @click="decideActiveApproval(true)">{{ I18N.approve }}</AppButton>
		</template>
	</AppModal>
</template>
