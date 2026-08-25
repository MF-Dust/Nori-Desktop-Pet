<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME} from "../../services/runtime"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {feedback} from "../../services/feedback"
import {i18n} from "../../services/i18n"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppCard from "../ui/AppCard.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"
import AppButton from "../ui/AppButton.vue"

const TEXT = computed(() => useLanguages().views.main.proactive)

const SAVE_MGR = useSnapshotSave({
	onError: (_key, error) => feedback.error(TEXT.value.reminders.addFailed, error),
})
const {defineField} = SAVE_MGR

const idleEnabledField = defineField(
	"idleEnabled",
	snapshot => snapshot.proactive.idleEnabled,
	true,
	val => RUNTIME.updateProactive({idleEnabled: val}),
)
const idleEnabled = idleEnabledField.value

const dailyGreetingField = defineField(
	"dailyGreeting",
	snapshot => snapshot.proactive.dailyGreeting,
	true,
	val => RUNTIME.updateProactive({dailyGreeting: val}),
)
const dailyGreeting = dailyGreetingField.value

const idleMinutesField = defineField(
	"idleMinutes",
	snapshot => snapshot.proactive.idleMinutes,
	15,
	val => RUNTIME.updateProactive({idleMinutes: val}),
)
const idleMinutes = idleMinutesField.value

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

const onIdleEnabledChange = (value: boolean) => {
	idleEnabled.value = value
	void idleEnabledField.saveNow()
}

const onDailyGreetingChange = (value: boolean) => {
	dailyGreeting.value = value
	void dailyGreetingField.saveNow()
}

const onIdleMinutesChange = (value: number) => {
	idleMinutes.value = value
	void idleMinutesField.saveNow()
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
				<AppSwitchRow
					:title="TEXT.idle.enabled"
					:desc="TEXT.idle.enabledDesc"
					:model-value="idleEnabled"
					@update:model-value="onIdleEnabledChange"
				/>

				<div v-if="idleEnabled" class="field">
					<span class="field-label">{{ TEXT.idle.interval }}</span>
					<div class="flex flex-wrap gap-2">
						<!-- 单选按钮本体用 sr-only 隐藏而非 display:none, 保留键盘可达与读屏语义 -->
						<label
							v-for="min in [5, 15, 30, 60]"
							:key="min"
							class="pill-choice focus-ring-within gap-1.5 px-3.5 py-1.5 text-xs"
							:class="idleMinutes === min ? 'pill-choice-on' : 'pill-choice-off'"
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
				<AppSwitchRow
					:title="TEXT.daily.enabled"
					:desc="TEXT.daily.enabledDesc"
					:model-value="dailyGreeting"
					@update:model-value="onDailyGreetingChange"
				/>
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
					<AppButton variant="primary" size="sm" :disabled="!newReminderText.trim()" @click="createReminder">
						{{ TEXT.reminders.add }}
					</AppButton>
				</div>

				<div class="flex flex-col gap-2 mt-1.5">
					<div v-if="reminders.length === 0" class="py-2 text-sm text-text-faint">
						{{ TEXT.reminders.empty }}
					</div>
					<div
						v-for="item in reminders"
						:key="item.id"
						class="flex items-center justify-between gap-3 px-3.5 py-2 rounded-sm bg-overlay-4
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
							class="btn-base w-7 h-7 shrink-0 rounded-sm bg-overlay-6 text-text-muted
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
