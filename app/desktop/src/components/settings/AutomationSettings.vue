<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import {RUNTIME, type VisionProbeResult} from "../../services/runtime"
import {invoke} from "../../services/host/invoke"
import {useSnapshotSave} from "../../composables/useSnapshotSave"
import {feedback} from "../../services/feedback"
import useLanguages from "../../services/i18n/useLanguages"
import Icon from "../Icon.vue"
import AppButton from "../ui/AppButton.vue"
import AppCard from "../ui/AppCard.vue"
import AppChip from "../ui/AppChip.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import AppSwitchRow from "../ui/AppSwitchRow.vue"

const TEXT = computed(() => useLanguages().views.main.automation)

const SAVE_MGR = useSnapshotSave({
	onError: (_key, error) => feedback.error(TEXT.value.saveFailed, error),
})
const {defineField} = SAVE_MGR

const automationSupported = computed(() => RUNTIME.snapshot.value?.automation !== undefined)
const automationState = computed(() => RUNTIME.snapshot.value?.automation)

const enabledField = defineField(
	"enabled",
	snapshot => snapshot.automation?.enabled ?? false,
	false,
	val => RUNTIME.updateAutomation({enabled: val}),
)
const enabled = enabledField.value

const desktopField = defineField(
	"desktopEnabled",
	snapshot => snapshot.automation?.desktopEnabled ?? false,
	false,
	val => RUNTIME.updateAutomation({desktopEnabled: val}),
)
const desktopEnabled = desktopField.value

const browserField = defineField(
	"browserEnabled",
	snapshot => snapshot.automation?.browserEnabled ?? false,
	false,
	val => RUNTIME.updateAutomation({browserEnabled: val}),
)
const browserEnabled = browserField.value

const visionReady = computed(() => automationState.value?.visionReady ?? false)
const capabilities = computed(() => automationState.value?.capabilities ?? [])
const unavailableReason = computed(() => automationState.value?.unavailableReason ?? null)
const browserRuntimeStatus = ref<{available: boolean; unavailableReason?: string | null} | null>(null)
const browserRuntimeAvailable = computed(() => browserRuntimeStatus.value?.available ?? true)
const browserRuntimeReason = computed(() => browserRuntimeStatus.value?.unavailableReason ?? null)

const probing = ref(false)
const probeResult = ref<VisionProbeResult | null>(null)

const masterDesc = computed(() => {
	if (!automationSupported.value) return TEXT.value.master.statusUnavailable
	return TEXT.value.master.desc
})

const desktopDesc = computed(() => {
	if (!enabled.value) return TEXT.value.desktop.requiresMaster
	return TEXT.value.desktop.desc
})

const browserDesc = computed(() => {
	if (!browserRuntimeAvailable.value) return browserRuntimeReason.value || TEXT.value.browser.desc
	if (!enabled.value) return TEXT.value.browser.requiresMaster
	return TEXT.value.browser.desc
})

const onEnabledChange = (val: boolean) => {
	enabled.value = val
	void enabledField.saveNow()
}

const onDesktopChange = (val: boolean) => {
	desktopEnabled.value = val
	void desktopField.saveNow()
}

const onBrowserChange = (val: boolean) => {
	if (!browserRuntimeAvailable.value) return
	browserEnabled.value = val
	void browserField.saveNow()
}

const onProbeVision = async () => {
	if (probing.value || !automationSupported.value) return
	probing.value = true
	probeResult.value = null
	try {
		const res = await RUNTIME.probeVisionCapability()
		probeResult.value = res
		if (!res.available) {
			feedback.warning(res.message || TEXT.value.vision.notReady)
		}
	} catch (error) {
		feedback.error(TEXT.value.vision.probeFailed, error)
	} finally {
		probing.value = false
	}
}

onMounted(async () => {
	try {
		browserRuntimeStatus.value = await invoke("automation_browser_status")
	} catch {
		// 老宿主没有该状态命令时保持兼容，不阻断设置页。
	}
})
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="TEXT.title" :subtitle="TEXT.subtitle"/>

		<div class="flex flex-col gap-3.5 pb-5">
			<!-- 1. 总开关 -->
			<AppCard :title="TEXT.master.title" icon="bot">
				<AppSwitchRow
					:title="TEXT.master.title"
					:desc="masterDesc"
					:model-value="enabled"
					:disabled="!automationSupported"
					@update:model-value="onEnabledChange"
				/>
				<div v-if="!automationSupported" class="flex items-center gap-2 pt-1">
					<AppChip tone="warning" icon="alert" dot>
						{{ unavailableReason || TEXT.master.statusUnavailable }}
					</AppChip>
				</div>
			</AppCard>

			<!-- 2. 操作权限与执行子项 -->
			<AppCard :title="TEXT.desktop.title" icon="monitor">
				<AppSwitchRow
					:title="TEXT.desktop.title"
					:desc="desktopDesc"
					:model-value="desktopEnabled"
					:disabled="!automationSupported || !enabled"
					@update:model-value="onDesktopChange"
				/>
				<div class="h-[0.1rem] bg-line-subtle"/>
				<AppSwitchRow
					:title="TEXT.browser.title"
					:desc="browserDesc"
					:model-value="browserRuntimeAvailable ? browserEnabled : false"
					:disabled="!automationSupported || !enabled || !browserRuntimeAvailable"
					@update:model-value="onBrowserChange"
				/>
				<div v-if="!browserRuntimeAvailable && browserRuntimeReason" class="flex items-center gap-2 pt-1">
					<AppChip tone="warning" icon="alert" dot>
						{{ browserRuntimeReason }}
					</AppChip>
				</div>
			</AppCard>

			<!-- 3. 视觉能力检测 -->
			<AppCard :title="TEXT.vision.title" icon="eye" :desc="TEXT.vision.desc">
				<div class="flex items-center justify-between gap-4 flex-wrap">
					<div class="flex items-center gap-2.5">
						<AppChip
							:tone="visionReady ? 'success' : 'warning'"
							:icon="visionReady ? 'check' : 'alert'"
							dot
						>
							{{ visionReady ? TEXT.vision.ready : TEXT.vision.notReady }}
						</AppChip>
						<span v-if="probeResult" class="text-xs text-text-faint">
							{{ probeResult.message }}
						</span>
					</div>

					<AppButton
						variant="primary"
						size="sm"
						icon="eye"
						:loading="probing"
						:disabled="probing || !automationSupported"
						@click="onProbeVision"
					>
						{{ probing ? TEXT.vision.probing : TEXT.vision.probe }}
					</AppButton>
				</div>
			</AppCard>

			<!-- 4. 能力状态清单 -->
			<AppCard :title="TEXT.status.title" icon="cpu">
				<div v-if="capabilities.length > 0" class="flex flex-col gap-2">
					<div
						v-for="cap in capabilities"
						:key="cap.id"
						class="flex items-center justify-between gap-3 px-3.5 py-2.5 rounded-sm bg-bg-surface/40 border border-line-subtle"
					>
						<div class="flex flex-col gap-0.5 min-w-0">
							<span class="text-sm font-500 text-text-primary truncate">{{ cap.name }}</span>
							<span v-if="cap.unavailableReason" class="text-xs text-text-faint truncate">
								{{ TEXT.status.reason }}: {{ cap.unavailableReason }}
							</span>
						</div>
						<AppChip
							:tone="cap.available ? 'success' : 'neutral'"
							:icon="cap.available ? 'check' : 'alert'"
							dot
						>
							{{ cap.available ? TEXT.status.available : TEXT.status.unavailable }}
						</AppChip>
					</div>
				</div>
				<div v-else class="flex items-center gap-2 py-1">
					<AppChip tone="neutral" icon="info">
						{{ TEXT.status.noCapabilities }}
					</AppChip>
				</div>
			</AppCard>

			<!-- 5. 安全与控制提示 -->
			<AppCard :title="TEXT.security.title" icon="shield">
				<div class="flex flex-col gap-2 text-xs text-text-faint leading-relaxed">
					<div class="flex items-start gap-2">
						<Icon name="shield" :size="13" class="text-nori-teal-bright shrink-0 mt-0.5"/>
						<span>{{ TEXT.security.tip1 }}</span>
					</div>
					<div class="flex items-start gap-2">
						<Icon name="shield" :size="13" class="text-nori-teal-bright shrink-0 mt-0.5"/>
						<span>{{ TEXT.security.tip2 }}</span>
					</div>
					<div class="flex items-start gap-2">
						<Icon name="shield" :size="13" class="text-nori-teal-bright shrink-0 mt-0.5"/>
						<span>{{ TEXT.security.tip3 }}</span>
					</div>
				</div>
			</AppCard>
		</div>
	</div>
</template>