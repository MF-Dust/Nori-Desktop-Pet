<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {useDebouncedSave} from "../../composables/useDebouncedSave"
import {useSnapshotField} from "../../composables/useSnapshotField"
import {feedback} from "../../services/feedback"
import {i18n} from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const TEXT = computed(() => useLanguages().views.main.proactive)

// 主动交互配置: 快照驱动且保留本地脏字段。
const idleEnabledField = useSnapshotField(snapshot => snapshot.proactive.idleEnabled, true)
const dailyGreetingField = useSnapshotField(snapshot => snapshot.proactive.dailyGreeting, true)
const idleMinutesField = useSnapshotField(snapshot => snapshot.proactive.idleMinutes, 15)
const idleEnabled = idleEnabledField.value
const dailyGreeting = dailyGreetingField.value
const idleMinutes = idleMinutesField.value
const SAVE = useDebouncedSave({onError: (_key, error) => feedback.error(TEXT.value.reminders.addFailed, error)})

// 提醒列表
interface ReminderView {
	id: string
	content: string
	triggerTime: number
}
const reminders = computed<ReminderView[]>(() => (RUNTIME.snapshot.value?.proactive.reminders ?? []).map(item => ({
	id: item.id,
	content: item.content,
	triggerTime: item.triggerTime,
})))

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

onMounted(async () => {
	await RUNTIME.init()
})

const saveProactiveField = (key: string, field: {touch: () => void; blur: () => void; reset: () => void; commit: () => void}, task: () => Promise<void>): void => {
	field.touch()
	field.blur()
	void SAVE.saveNow(key, async () => {
		try {
			await task()
			field.commit()
		} catch (error) {
			field.reset()
			throw error
		}
	})
}

const onIdleEnabledChange = (value: boolean) => {
	idleEnabled.value = value
	saveProactiveField("idleEnabled", idleEnabledField, () => RUNTIME.updateProactive({idleEnabled: value}))
}

const onDailyGreetingChange = (value: boolean) => {
	dailyGreeting.value = value
	saveProactiveField("dailyGreeting", dailyGreetingField, () => RUNTIME.updateProactive({dailyGreeting: value}))
}

const onIdleMinutesChange = (value: number) => {
	idleMinutes.value = value
	saveProactiveField("idleMinutes", idleMinutesField, () => RUNTIME.updateProactive({idleMinutes: value}))
}

// 手动创建提醒
const createReminder = async () => {
	if (!newReminderText.value.trim()) return
	try {
		await RUNTIME.reminderAdd(newReminderText.value.trim(), newReminderMinutes.value)
		await RUNTIME.refresh()
		newReminderText.value = ""
	} catch (error) {
		feedback.error(TEXT.value.reminders.addFailed, error)
	}
}

// 取消提醒
const cancelReminder = async (id: string) => {
	try {
		await RUNTIME.reminderCancel(id)
		await RUNTIME.refresh()
	} catch (error) {
		feedback.error(TEXT.value.reminders.cancelFailed, error)
	}
}
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="TEXT.title" :subtitle="TEXT.subtitle"/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 挂机主动关怀 -->
			<AppCard :title="TEXT.idle.title" icon="sparkles">
				<AppSwitchRow :title="TEXT.idle.enabled" :desc="TEXT.idle.enabledDesc">
					<n-switch
						:value="idleEnabled"
						@update:value="onIdleEnabledChange"
					/>
				</AppSwitchRow>

				<div v-if="idleEnabled" class="field">
					<span class="field-label">{{ TEXT.idle.interval }}</span>
					<div class="flex flex-wrap gap-2">
						<!-- 单选按钮本体用 sr-only 隐藏而非 display:none, 保留键盘可达与读屏语义 -->
						<label
							v-for="min in [5, 15, 30, 60]"
							:key="min"
							class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-pill border text-xs cursor-pointer
								transition-all duration-200
								focus-within:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)"
							:class="idleMinutes === min
								? 'border-transparent bg-gradient-to-br from-nori-teal-bright to-nori-teal text-on-teal font-600 shadow-[0_0.2rem_1.2rem_var(--glow-teal-soft)]'
								: 'border-line-subtle bg-white/3 text-text-body hover:(text-nori-teal-bright bg-nori-teal-bright/6 border-nori-teal-soft)'"
						>
							<input
								v-model="idleMinutes"
								type="radio"
								:value="min"
								class="sr-only"
								@change="onIdleMinutesChange(min)"
							/>
							{{ min }} {{ TEXT.minutes }}
						</label>
					</div>
				</div>
			</AppCard>

			<!-- 2. 日常早晚安日程 -->
			<AppCard :title="TEXT.daily.title" icon="noriOS">
				<AppSwitchRow :title="TEXT.daily.enabled" :desc="TEXT.daily.enabledDesc">
					<n-switch
						:value="dailyGreeting"
						@update:value="onDailyGreetingChange"
					/>
				</AppSwitchRow>
			</AppCard>

			<!-- 3. 定时提醒管理 (持久化于后端 SQLite, 重启自动恢复) -->
			<AppCard :title="TEXT.reminders.title" icon="info">
				<div class="flex gap-2">
					<input
						v-model="newReminderText"
						class="input-base flex-1"
						:placeholder="TEXT.reminders.placeholder"
						@keydown.enter="createReminder"
					/>
					<n-select
						v-model:value="newReminderMinutes"
						:options="REMINDER_OPTIONS"
						class="w-[14rem]"
					/>
					<n-button type="primary" :disabled="!newReminderText.trim()" @click="createReminder">
						{{ TEXT.reminders.add }}
					</n-button>
				</div>

				<div class="flex flex-col gap-2 mt-1.5">
					<div v-if="reminders.length === 0" class="py-2 text-sm text-text-faint">
						{{ TEXT.reminders.empty }}
					</div>
					<div
						v-for="item in reminders"
						:key="item.id"
						class="flex items-center justify-between gap-3 px-3.5 py-2 rounded-sm bg-white/3
							border border-line-subtle transition-all duration-200
							hover:(bg-nori-teal-bright/4 border-line-strong)"
					>
						<div class="flex flex-col gap-0.5 min-w-0">
							<span class="text-base text-text-primary font-500">{{ item.content }}</span>
							<span class="text-xs text-nori-teal-bright mono">
								{{ TEXT.reminders.trigger }}: {{ new Date(item.triggerTime).toLocaleTimeString(i18n.global.locale.value) }}
							</span>
						</div>
						<button
							type="button"
							class="btn-base w-7 h-7 shrink-0 rounded-sm bg-white/5 text-text-muted
								hover:(bg-danger/18 text-danger-text)"
							:title="TEXT.reminders.cancel"
							:aria-label="TEXT.reminders.cancel"
							@click="cancelReminder(item.id)"
						>
							<Icon name="close" :size="14"/>
						</button>
					</div>
				</div>
			</AppCard>
		</div>
	</div>
</template>
