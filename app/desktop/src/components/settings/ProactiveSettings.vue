<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {i18n} from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"

const TEXT = computed(() => useLanguages().views.main.proactive)

// 主动交互配置 (快照为真相)
const idleEnabled = computed(() => RUNTIME.snapshot.value?.proactive.idleEnabled ?? true)
const dailyGreeting = computed(() => RUNTIME.snapshot.value?.proactive.dailyGreeting ?? true)
const idleMinutes = ref(15)
let syncedIdle = false

// 提醒列表
interface ReminderView {
	id: string
	content: string
	triggerTime: number
}
const reminders = ref<ReminderView[]>([])

// 新建提醒输入
const newReminderText = ref("")
const newReminderMinutes = ref(15)
const REMINDER_OPTIONS = computed(() => [
	{label: `5 ${TEXT.value.minutesLater}`, value: 5},
	{label: `15 ${TEXT.value.minutesLater}`, value: 15},
	{label: `30 ${TEXT.value.minutesLater}`, value: 30},
	{label: `1 ${TEXT.value.hourLater}`, value: 60},
	{label: `2 ${TEXT.value.hoursLater}`, value: 120},
])

const syncReminders = () => {
	const LIST = RUNTIME.snapshot.value?.proactive.reminders ?? []
	reminders.value = LIST.map(item => ({id: item.id, content: item.content, triggerTime: item.triggerTime}))
	if (!syncedIdle) {
		syncedIdle = true
		idleMinutes.value = RUNTIME.snapshot.value?.proactive.idleMinutes ?? 15
	}
}

onMounted(async () => {
	await RUNTIME.init()
	syncReminders()
})

// 手动创建提醒
const createReminder = async () => {
	if (!newReminderText.value.trim()) return
	await RUNTIME.reminderAdd(newReminderText.value.trim(), newReminderMinutes.value).catch(error =>
		console.error("创建提醒失败:", error))
	newReminderText.value = ""
	await RUNTIME.refresh()
	syncReminders()
}

// 取消提醒
const cancelReminder = async (id: string) => {
	await RUNTIME.reminderCancel(id).catch(error => console.error("取消提醒失败:", error))
	await RUNTIME.refresh()
	syncReminders()
}
</script>

<template>
	<div class="proactive-settings">
		<header class="section-header">
			<h2 class="title glow-teal">{{ TEXT.title }}</h2>
			<p class="subtitle">{{ TEXT.subtitle }}</p>
		</header>

		<div class="settings-content">
			<!-- 1. 挂机主动关怀 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="sparkles" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.idle.title }}</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">{{ TEXT.idle.enabled }}</span>
							<p class="switch-desc">{{ TEXT.idle.enabledDesc }}</p>
						</div>
						<n-switch
							:value="idleEnabled"
							@update:value="(val: boolean) => RUNTIME.updateProactive({idleEnabled: val})"
						/>
					</div>

					<div v-if="idleEnabled" class="form-item">
						<label class="label">{{ TEXT.idle.interval }}</label>
						<div class="radio-group">
							<label
								v-for="min in [5, 15, 30, 60]"
								:key="min"
								class="radio-chip"
								:class="{active: idleMinutes === min}"
							>
								<input
									v-model="idleMinutes"
									type="radio"
									:value="min"
									@change="RUNTIME.updateProactive({idleMinutes: min})"
								/>
								{{ min }} {{ TEXT.minutes }}
							</label>
						</div>
					</div>
				</div>
			</div>

			<!-- 2. 日常早晚安日程 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="noriOS" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.daily.title }}</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">{{ TEXT.daily.enabled }}</span>
							<p class="switch-desc">{{ TEXT.daily.enabledDesc }}</p>
						</div>
						<n-switch
							:value="dailyGreeting"
							@update:value="(val: boolean) => RUNTIME.updateProactive({dailyGreeting: val})"
						/>
					</div>
				</div>
			</div>

			<!-- 3. 定时提醒管理 (持久化于后端 SQLite, 重启自动恢复) -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="info" :size="18" class="card-icon"/>
					<span class="card-title">{{ TEXT.reminders.title }}</span>
				</div>
				<div class="card-body">
					<div class="add-reminder-row">
						<input
							v-model="newReminderText"
							class="input flex-1"
							:placeholder="TEXT.reminders.placeholder"
							@keydown.enter="createReminder"
						/>
						<n-select
							v-model:value="newReminderMinutes"
							:options="REMINDER_OPTIONS"
							style="width: 14rem;"
						/>
						<n-button type="primary" :disabled="!newReminderText.trim()" @click="createReminder">
							{{ TEXT.reminders.add }}
						</n-button>
					</div>

					<div class="reminder-list">
						<div v-if="reminders.length === 0" class="empty-hint">
							{{ TEXT.reminders.empty }}
						</div>
						<div
							v-for="item in reminders"
							:key="item.id"
							class="reminder-item"
						>
							<div class="reminder-info">
								<span class="reminder-text">{{ item.content }}</span>
								<span class="reminder-time">
									{{ TEXT.reminders.trigger }}: {{ new Date(item.triggerTime).toLocaleTimeString(i18n.global.locale.value) }}
								</span>
							</div>
							<button class="btn-del" :title="TEXT.reminders.cancel" @click="cancelReminder(item.id)">
								<Icon name="close" :size="14"/>
							</button>
						</div>
					</div>
				</div>
			</div>
		</div>
	</div>
</template>

<style scoped lang="less">
.proactive-settings {
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

.card-header {
	display: flex;
	align-items: center;
	gap: 0.8rem;
	color: var(--nori-teal-bright);
}

.card-title {
	font-size: 1.35rem;
	font-weight: 600;
	color: var(--text-primary);
}

.card-body {
	display: flex;
	flex-direction: column;
	gap: 1.2rem;
}

.form-item {
	display: flex;
	flex-direction: column;
	gap: 0.6rem;
}

.label {
	font-size: 1.2rem;
	font-weight: 500;
	color: var(--text-muted);
}

.input {
	padding: 0.9rem 1.4rem;
	background: rgba(255, 255, 255, 0.04);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.3rem;
	font-family: inherit;
	outline: none;
	transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

	&:focus {
		border-color: var(--nori-teal);
		background: rgba(125, 227, 255, 0.06);
		box-shadow: 0 0 1.2rem var(--glow-teal-soft);
	}
}

.flex-1 {
	flex: 1;
}

.radio-group {
	display: flex;
	flex-wrap: wrap;
	gap: 0.8rem;
}

.radio-chip {
	display: inline-flex;
	align-items: center;
	gap: 0.6rem;
	padding: 0.65rem 1.3rem;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-pill);
	background: rgba(255, 255, 255, 0.03);
	color: var(--text-body);
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

.switch-row {
	display: flex;
	align-items: center;
	justify-content: space-between;
}

.switch-title {
	font-size: 1.3rem;
	color: var(--text-primary);
	font-weight: 500;
}

.switch-desc {
	margin: 0.2rem 0 0;
	font-size: 1.15rem;
	color: var(--text-faint);
}

.add-reminder-row {
	display: flex;
	gap: 0.8rem;
}

.reminder-list {
	display: flex;
	flex-direction: column;
	gap: 0.8rem;
	margin-top: 0.6rem;
}

.empty-hint {
	font-size: 1.2rem;
	color: var(--text-faint);
	padding: 0.8rem 0;
}

.reminder-item {
	display: flex;
	align-items: center;
	justify-content: space-between;
	padding: 0.9rem 1.4rem;
	background: rgba(255, 255, 255, 0.03);
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	transition: all 0.2s ease;

	&:hover {
		background: rgba(125, 227, 255, 0.04);
		border-color: var(--line-strong);
	}
}

.reminder-info {
	display: flex;
	flex-direction: column;
	gap: 0.25rem;
}

.reminder-text {
	font-size: 1.3rem;
	color: var(--text-primary);
	font-weight: 500;
}

.reminder-time {
	font-size: 1.1rem;
	color: var(--nori-teal-bright);
	font-family: monospace;
}

.btn-del {
	width: 2.8rem;
	height: 2.8rem;
	border: none;
	border-radius: var(--radius-sm);
	background: rgba(255, 255, 255, 0.05);
	color: var(--text-muted);
	cursor: pointer;
	display: flex;
	align-items: center;
	justify-content: center;
	transition: all 0.15s ease;

	&:hover {
		background: rgba(251, 60, 68, 0.18);
		color: var(--danger);
	}
}
</style>
