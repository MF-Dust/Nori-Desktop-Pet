<script setup lang="ts">
import {onMounted, ref} from "vue"
import {invoke} from "../../services/host/invoke"
import {proactiveService, type ReminderItem} from "../../services/proactive"
import Icon from "../Icon.vue"

// 挂机主动关怀开关
const idleEnabled = ref(true)
const idleMinutes = ref(15)

// 日常时段问候开关
const dailyGreeting = ref(true)

// 当前排队提醒事项
const reminders = ref<ReminderItem[]>([])

// 新建提醒输入
const newReminderText = ref("")
const newReminderMinutes = ref(15)

// 刷新提醒列表
const refreshReminders = () => {
	reminders.value = proactiveService.listReminders()
}

onMounted(async () => {
	try {
		const [SAVED_IDLE, SAVED_MIN, SAVED_DAILY] = await Promise.all([
			invoke<string | null>("get_config", {key: "proactive_idle_enabled"}),
			invoke<string | null>("get_config", {key: "proactive_idle_minutes"}),
			invoke<string | null>("get_config", {key: "proactive_daily_greeting"}),
		])
		if (SAVED_IDLE !== null) idleEnabled.value = SAVED_IDLE === "true" || SAVED_IDLE === "1"
		if (SAVED_MIN) idleMinutes.value = parseInt(SAVED_MIN, 10) || 15
		if (SAVED_DAILY !== null) dailyGreeting.value = SAVED_DAILY === "true" || SAVED_DAILY === "1"
	} catch (error) {
		console.error("读取主动交互配置失败:", error)
	}
	refreshReminders()
})

const saveConfig = (key: string, value: string) => {
	void invoke("set_config", {key, value})
}

// 手动创建提醒
const createReminder = () => {
	if (!newReminderText.value.trim()) return
	proactiveService.addReminder(newReminderText.value.trim(), newReminderMinutes.value)
	newReminderText.value = ""
	refreshReminders()
}

// 取消提醒
const cancelReminder = (id: string) => {
	proactiveService.cancelReminder(id)
	refreshReminders()
}
</script>

<template>
	<div class="proactive-settings">
		<header class="section-header">
			<h2 class="title glow-teal">主动交互与日常关怀</h2>
			<p class="subtitle">配置桌宠在挂机、特定时段以及定时日程下的主动陪伴与提醒行为</p>
		</header>

		<div class="settings-content">
			<!-- 1. 挂机主动关怀 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="sparkles" :size="18" class="card-icon"/>
					<span class="card-title">挂机主动关怀</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">无操作挂机互动</span>
							<p class="switch-desc">长时间无鼠标或键盘操作时，Nori 会主动向主人表达关怀、伸懒腰或做出小动作</p>
						</div>
						<n-switch
							v-model:value="idleEnabled"
							@update:value="(val: boolean) => saveConfig('proactive_idle_enabled', String(val))"
						/>
					</div>

					<div v-if="idleEnabled" class="form-item">
						<label class="label">挂机触发间隔</label>
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
									@change="saveConfig('proactive_idle_minutes', String(min))"
								/>
								{{ min }} 分钟
							</label>
						</div>
					</div>
				</div>
			</div>

			<!-- 2. 日常早晚安日程 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="noriOS" :size="18" class="card-icon"/>
					<span class="card-title">日常时钟问候</span>
				</div>
				<div class="card-body">
					<div class="switch-row">
						<div>
							<span class="switch-title">定点生活问候</span>
							<p class="switch-desc">在早晨（8:30）、午餐（12:00）和深夜（23:00）主动向主人问安与提醒休息</p>
						</div>
						<n-switch
							v-model:value="dailyGreeting"
							@update:value="(val: boolean) => saveConfig('proactive_daily_greeting', String(val))"
						/>
					</div>
				</div>
			</div>

			<!-- 3. 定时提醒管理 -->
			<div class="setting-card">
				<div class="card-header">
					<Icon name="info" :size="18" class="card-icon"/>
					<span class="card-title">定时提醒任务</span>
				</div>
				<div class="card-body">
					<div class="add-reminder-row">
						<input
							v-model="newReminderText"
							class="input flex-1"
							placeholder="例如: 喝杯水 / 站起来走走 / 准备开会..."
							@keydown.enter="createReminder"
						/>
						<n-select
							v-model:value="newReminderMinutes"
							:options="[
								{label: '5 分钟后', value: 5},
								{label: '15 分钟后', value: 15},
								{label: '30 分钟后', value: 30},
								{label: '1 小时后', value: 60},
								{label: '2 小时后', value: 120}
							]"
							style="width: 14rem;"
						/>
						<n-button type="primary" :disabled="!newReminderText.trim()" @click="createReminder">
							添加提醒
						</n-button>
					</div>

					<div class="reminder-list">
						<div v-if="reminders.length === 0" class="empty-hint">
							暂无排队中的提醒事项（您也可以在聊天中让 Nori 帮您设置提醒哦）
						</div>
						<div
							v-for="item in reminders"
							:key="item.id"
							class="reminder-item"
						>
							<div class="reminder-info">
								<span class="reminder-text">{{ item.content }}</span>
								<span class="reminder-time">
									预计触发: {{ new Date(item.triggerTime).toLocaleTimeString("zh-CN") }}
								</span>
							</div>
							<button class="btn-del" title="取消此提醒" @click="cancelReminder(item.id)">
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

.select-box {
	padding: 0.9rem 1.2rem;
	background: #081a2e;
	border: 0.1rem solid var(--line-subtle);
	border-radius: var(--radius-sm);
	color: var(--text-primary);
	font-size: 1.25rem;
	font-family: inherit;
	outline: none;
	cursor: pointer;
	transition: all 0.2s ease;

	&:focus {
		border-color: var(--nori-teal-soft);
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

.toggle-switch {
	position: relative;
	width: 4.2rem;
	height: 2.4rem;
	cursor: pointer;

	input {
		opacity: 0;
		width: 0;
		height: 0;
	}

	.toggle-slider {
		position: absolute;
		inset: 0;
		background: rgba(255, 255, 255, 0.12);
		border-radius: var(--radius-pill);
		transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);

		&::before {
			position: absolute;
			content: "";
			height: 1.8rem;
			width: 1.8rem;
			left: 0.3rem;
			bottom: 0.3rem;
			background: white;
			border-radius: 50%;
			transition: all 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
		}
	}

	input:checked + .toggle-slider {
		background: var(--nori-teal);
		box-shadow: 0 0 1rem var(--glow-teal);
	}

	input:checked + .toggle-slider::before {
		transform: translateX(1.8rem);
		background: #03101c;
	}
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
