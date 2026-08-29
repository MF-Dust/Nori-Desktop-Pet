<script setup lang="ts">
/**
 * 网易云音乐控制卡 (Nori.Plugin.CloudMusic)
 *
 * 插件无独立界面; 桌宠 AI 触发 music_* 动作后此卡片出现在聊天顶部:
 * 播放进度 / 上一曲下一曲 / 播放暂停 / 音量 / 搜索结果选择。
 * 全部经 bridge plugin_action 调用插件 IPluginActionContribution。
 */
import {computed, onBeforeUnmount, onMounted, ref, watch} from "vue"
import {invoke} from "../../services/host/invoke"
import {RUNTIME, type AgentEventPayload} from "../../services/runtime"
import AppButton from "../ui/AppButton.vue"
import Icon from "../Icon.vue"

const PLUGIN_ID = "nori.plugin.cloudmusic"

interface Candidate {
	index: number
	id: number
	name: string
	artist: string
	album: string
}

interface MusicStatus {
	playing: boolean
	hasTrack: boolean
	songName?: string
	artist?: string
	position: number
	duration: number
	volume: number
	queueIndex: number
	queueLength: number
	detail?: string
}

const expanded = ref(false)
const dismissed = ref(false)
const status = ref<MusicStatus | null>(null)
const loggedIn = ref(false)
const loginQrVisible = ref(false)
const loginQrImg = ref("")
const loginUnikey = ref("")
const loginMessage = ref("")
const loginExpired = ref(false)
let loginPollTimer: ReturnType<typeof setInterval> | null = null
const candidates = ref<Candidate[]>([])
const keyword = ref("")
const searching = ref(false)
const pickLoading = ref(-1)
const volume = ref(60)
let pollTimer: ReturnType<typeof setInterval> | null = null

const executingTool = ref("")
let unlisten: (() => void) | null = null

function handleAgentEvent(payload: AgentEventPayload): void {
	if (payload.type === "tool-executing") executingTool.value = payload.toolName ?? ""
	else if (payload.type === "tool-executed") executingTool.value = ""
}

const musicToolRunning = computed(() =>
	executingTool.value.startsWith("plugin__") && executingTool.value.includes("__music_"))

async function call(actionId: string, args?: Record<string, unknown>): Promise<Record<string, unknown>> {
	return invoke("plugin_action", {pluginId: PLUGIN_ID, actionId, args})
}

async function refreshStatus(): Promise<void> {
	try {
		const result = await call("music_status")
		status.value = result as unknown as MusicStatus
		volume.value = Math.round((result.volume as number) ?? 60)
		loggedIn.value = (result.loggedIn as boolean) ?? false
	} catch {
		status.value = null
	}
}

function ensurePolling(): void {
	if (pollTimer) return
	pollTimer = setInterval(() => {
		if (document.hidden) return
		void refreshStatus()
	}, 2000)
	void refreshStatus()
}

watch(musicToolRunning, value => {
	if (value) {
		dismissed.value = false
		expanded.value = true
		ensurePolling()
		void refreshStatus()
	}
})

onMounted(async () => {
	unlisten = await RUNTIME.onAgentEvent(handleAgentEvent)
	// 已有播放中的音乐时恢复卡片
	await refreshStatus()
	if (status.value?.hasTrack) {
		expanded.value = true
		ensurePolling()
	}
})

onBeforeUnmount(() => {
	if (pollTimer) clearInterval(pollTimer)
	stopLoginPolling()
	unlisten?.()
	unlisten = null
})

function toggleExpand(): void {
	expanded.value = !expanded.value
	if (expanded.value) {
		ensurePolling()
		void refreshStatus()
	}
}

async function togglePlay(): Promise<void> {
	const action = status.value?.playing ? "music_pause" : "music_resume"
	await call(action).catch(() => undefined)
	await refreshStatus()
}

async function skip(action: "music_next" | "music_previous"): Promise<void> {
	await call(action).catch(() => undefined)
	await refreshStatus()
}

async function changeVolume(): Promise<void> {
	await call("music_volume", {level: volume.value}).catch(() => undefined)
}

async function startLogin(): Promise<void> {
	loginExpired.value = false
	loginMessage.value = "正在获取登录码…"
	try {
		const result = await call("music_login_qr")
		if (result.loggedIn) {
			loginMessage.value = (result.message as string) ?? "已登录"
			return
		}
		loginUnikey.value = (result.unikey as string) ?? ""
		loginQrImg.value = (result.qrimg as string) ?? ""
		loginQrVisible.value = true
		loginMessage.value = (result.message as string) ?? "请用网易云音乐 App 扫码"
		startLoginPolling()
	} catch {
		loginMessage.value = "获取登录码失败, 请稍后重试"
	}
}

function startLoginPolling(): void {
	if (loginPollTimer) clearInterval(loginPollTimer)
	loginPollTimer = setInterval(async () => {
		if (!loginQrVisible.value) return
		try {
			const result = await call("music_login_check", {unikey: loginUnikey.value})
			const code = result.code as number
			loginMessage.value = (result.message as string) ?? loginMessage.value
			if (code === 803) {
				stopLoginPolling()
				loginQrVisible.value = false
				loggedIn.value = true
				loginMessage.value = "登录成功"
				await refreshStatus()
			} else if (code === 800) {
				stopLoginPolling()
				loginExpired.value = true
			}
		} catch {
			// 轮询错误忽略
		}
	}, 2000)
}

function stopLoginPolling(): void {
	if (loginPollTimer) clearInterval(loginPollTimer)
	loginPollTimer = null
}

function closeLogin(): void {
	loginQrVisible.value = false
	stopLoginPolling()
}

async function logout(): Promise<void> {
	await call("music_logout").catch(() => undefined)
	loggedIn.value = false
	await refreshStatus()
}

async function runSearch(): Promise<void> {
	if (keyword.value.trim().length === 0) return
	searching.value = true
	try {
		const result = await call("music_search", {keyword: keyword.value.trim()})
		candidates.value = (result.candidates as Candidate[]) ?? []
		if (candidates.value.length === 0 && typeof result.message === "string") {
			status.value = {...(status.value ?? emptyStatus()), detail: result.message}
		}
	} finally {
		searching.value = false
	}
}

async function pick(candidate: Candidate): Promise<void> {
	pickLoading.value = candidate.index
	try {
		await call("music_pick", {index: candidate.index})
		await refreshStatus()
	} finally {
		pickLoading.value = -1
	}
}

function emptyStatus(): MusicStatus {
	return {playing: false, hasTrack: false, position: 0, duration: 0, volume: volume.value, queueIndex: -1, queueLength: 0}
}

function formatTime(seconds: number): string {
	if (!seconds || seconds <= 0) return "00:00"
	const total = Math.floor(seconds)
	return `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`
}

const progressPercent = computed(() =>
	status.value && status.value.duration > 0
		? Math.min(100, (status.value.position / status.value.duration) * 100)
		: 0)
</script>

<template>
	<div v-if="!dismissed" class="mx-4.5 mt-3 rounded-xl border border-line-subtle bg-bg-deep/60 backdrop-blur-[1rem] text-sm overflow-hidden">
		<!-- 折叠头 -->
		<button class="w-full flex items-center gap-2 px-3.5 py-2 text-left hover:bg-bg-hover/40" @click="toggleExpand">
			<span class="text-nori-teal-bright">♪</span>
			<span class="font-medium">网易云音乐</span>
			<span v-if="status?.hasTrack" class="text-text-muted truncate flex-1">
				{{ status.songName }} - {{ status.artist }}
				<span v-if="status.playing" class="text-emerald-400 ml-1">●</span>
			</span>
			<span v-else class="text-text-muted flex-1">{{ status?.detail ?? "未在播放" }}</span>
			<span v-if="!loggedIn" class="text-xs text-nori-teal-bright shrink-0 hover:underline" @click.stop="startLogin">扫码登录</span>
			<span v-else class="text-xs text-text-muted shrink-0 hover:underline" @click.stop="logout">退出</span>
			<Icon :name="expanded ? 'arrow-up' : 'arrow-down'" class="w-4 h-4 text-text-muted" />
		</button>

		<div v-if="expanded" class="px-3.5 pb-3 flex flex-col gap-2.5">
			<!-- 扫码登录 -->
			<div v-if="!loggedIn || loginQrVisible" class="rounded-lg border border-line-subtle bg-bg-hover/30 p-3 flex items-center gap-3">
				<template v-if="loginQrVisible && loginQrImg">
					<div class="relative shrink-0">
						<img :src="loginQrImg" class="w-28 h-28 rounded-md" :class="loginExpired ? 'opacity-25 blur-[1px]' : ''" />
						<button
							v-if="loginExpired"
							class="absolute inset-0 flex items-center justify-center text-xs bg-black/40 rounded-md text-white"
							@click="startLogin"
						>刷新二维码</button>
					</div>
					<div class="text-xs text-text-muted flex flex-col gap-1.5">
						<span>{{ loginMessage }}</span>
						<button class="text-nori-teal-bright text-left hover:underline w-fit" @click="closeLogin">收起二维码</button>
					</div>
				</template>
				<template v-else>
					<span class="text-xs text-text-muted flex-1">{{ loginMessage || "搜索与播放需要登录网易云音乐" }}</span>
					<AppButton variant="primary" size="sm" @click="startLogin">获取登录二维码</AppButton>
				</template>
			</div>

			<!-- 当前播放 -->
			<div class="flex items-center gap-3">
				<div class="min-w-0 flex-1">
					<p class="truncate font-medium" data-music-title>
						{{ status?.hasTrack ? `${status.songName}` : "未在播放" }}
					</p>
					<p class="truncate text-xs text-text-muted" data-music-artist>
						{{ status?.hasTrack ? (status.artist ?? "") : (status?.detail ?? "对 Nori 说: 播放某首歌") }}
					</p>
				</div>
				<div class="flex items-center gap-1">
					<AppButton variant="ghost" size="sm" icon="arrow-left" :disabled="!status?.hasTrack" @click="skip('music_previous')" />
					<AppButton variant="primary" size="sm" :disabled="!status?.hasTrack" @click="togglePlay">
						{{ status?.playing ? "暂停" : "播放" }}
					</AppButton>
					<AppButton variant="ghost" size="sm" icon="arrow-right" :disabled="!status?.hasTrack" @click="skip('music_next')" />
				</div>
			</div>

			<!-- 进度 -->
			<div class="flex items-center gap-2 text-xs text-text-muted" data-music-progress>
				<span>{{ formatTime(status?.position ?? 0) }}</span>
				<div class="flex-1 h-1.5 rounded-full bg-bg-hover/60 overflow-hidden">
					<div class="h-full bg-nori-teal-bright/80 rounded-full transition-[width] duration-500" :style="{width: `${progressPercent}%`}" />
				</div>
				<span>{{ formatTime(status?.duration ?? 0) }}</span>
			</div>

			<!-- 音量 -->
			<div class="flex items-center gap-2 text-xs text-text-muted" data-music-volume>
				<span>音量</span>
				<input
					v-model.number="volume"
					type="range"
					min="0"
					max="100"
					class="flex-1 accent-nori-teal-bright"
					@change="changeVolume"
				/>
				<span class="w-8 text-right">{{ volume }}</span>
			</div>

			<!-- 搜索与候选选择 -->
			<div class="flex items-center gap-2">
				<input
					v-model="keyword"
					placeholder="搜索歌曲加入播放 (需登录)"
					class="flex-1 bg-bg-hover/40 rounded-lg px-2.5 py-1.5 text-xs outline-none focus:ring-1 focus:ring-nori-teal-bright/50"
					@keyup.enter="runSearch"
				/>
				<AppButton variant="ghost" size="sm" :loading="searching" @click="runSearch">搜索</AppButton>
			</div>
			<ul v-if="candidates.length" class="flex flex-col gap-0.5 max-h-40 overflow-y-auto">
				<li v-for="candidate in candidates" :key="candidate.id">
					<button
						class="w-full flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-lg text-xs hover:bg-bg-hover/50 text-left disabled:opacity-50"
						:disabled="pickLoading === candidate.index"
						@click="pick(candidate)"
					>
						<span class="truncate">{{ candidate.name }} <span class="text-text-muted">- {{ candidate.artist }}</span></span>
						<span class="text-nori-teal-bright shrink-0">{{ pickLoading === candidate.index ? "…" : "播放" }}</span>
					</button>
				</li>
			</ul>
		</div>
	</div>
</template>
