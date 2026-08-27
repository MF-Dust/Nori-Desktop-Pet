<script setup lang="ts">
import {computed, onMounted, ref} from "vue"
import Icon from "../Icon.vue"
import AppButton from "../ui/AppButton.vue"
import AppChip from "../ui/AppChip.vue"
import AppModal from "../ui/AppModal.vue"
import AppSectionHeader from "../ui/AppSectionHeader.vue"
import {feedback} from "../../services/feedback"
import usePluginLanguages from "../../services/i18n/usePluginLanguages"
import {RUNTIME} from "../../services/runtime"
import {
	disablePlugin,
	enablePlugin,
	installLocalPlugin,
	listPlugins,
	uninstallPlugin,
	type PluginInfo,
	type PluginState,
} from "../../services/plugins"

const I18N = computed(() => usePluginLanguages().plugins)
const plugins = ref<PluginInfo[]>([])
const loading = ref(false)
const busyId = ref<string | null>(null)
const riskModal = ref(false)
const detailPlugin = ref<PluginInfo | null>(null)
const uninstallTarget = ref<PluginInfo | null>(null)
const deleteData = ref(false)

const TRUST_KEY = "nori.plugins.trusted-in-process-warning.v1"
const safeMode = computed(() => RUNTIME.snapshot.value?.app.safeMode === true)

const statusLabel = (state: PluginState): string => I18N.value.status[state]
const isBusy = (plugin: PluginInfo): boolean =>
	busyId.value === plugin.id || plugin.state === "loading" || plugin.state === "stopping"

const replacePlugin = (next: PluginInfo): void => {
	const index = plugins.value.findIndex(item => item.id === next.id)
	if (index < 0) plugins.value.push(next)
	else plugins.value[index] = next
	plugins.value = [...plugins.value].sort((left, right) => left.name.localeCompare(right.name))
}

const refresh = async (): Promise<void> => {
	loading.value = true
	try {
		plugins.value = await listPlugins()
	} catch (error) {
		feedback.error(I18N.value.error.load, error)
	} finally {
		loading.value = false
	}
}

const executeInstall = async (): Promise<void> => {
	loading.value = true
	try {
		const result = await installLocalPlugin()
		if (!result.cancelled && result.plugin) replacePlugin(result.plugin)
	} catch (error) {
		feedback.error(I18N.value.error.install, error)
	} finally {
		loading.value = false
	}
}

const beginInstall = (): void => {
	if (safeMode.value) return
	if (localStorage.getItem(TRUST_KEY) === "1") {
		void executeInstall()
		return
	}
	riskModal.value = true
}

const confirmRisk = (): void => {
	localStorage.setItem(TRUST_KEY, "1")
	riskModal.value = false
	void executeInstall()
}

const enable = async (plugin: PluginInfo): Promise<void> => {
	if (safeMode.value || isBusy(plugin)) return
	busyId.value = plugin.id
	try {
		replacePlugin(await enablePlugin(plugin.id))
	} catch (error) {
		feedback.error(I18N.value.error.enable, error)
	} finally {
		busyId.value = null
	}
}

const disable = async (plugin: PluginInfo): Promise<void> => {
	if (isBusy(plugin)) return
	busyId.value = plugin.id
	try {
		replacePlugin(await disablePlugin(plugin.id))
	} catch (error) {
		feedback.error(I18N.value.error.disable, error)
	} finally {
		busyId.value = null
	}
}

const beginUninstall = (plugin: PluginInfo): void => {
	deleteData.value = false
	uninstallTarget.value = plugin
}

const confirmUninstall = async (): Promise<void> => {
	const target = uninstallTarget.value
	if (!target) return
	busyId.value = target.id
	try {
		const result = await uninstallPlugin(target.id, deleteData.value)
		if (result.plugin) replacePlugin(result.plugin)
		else plugins.value = plugins.value.filter(item => item.id !== target.id)
		uninstallTarget.value = null
		deleteData.value = false
	} catch (error) {
		feedback.error(I18N.value.error.uninstall, error)
	} finally {
		busyId.value = null
	}
}

const capabilityLabel = (plugin: PluginInfo, id: string): string => {
	const status = plugin.capabilityStatuses.find(item => item.id === id)
	if (!status?.declared) return I18N.value.permissions.undeclared
	if (!status.granted) return I18N.value.permissions.unavailable
	return status.available ? I18N.value.permissions.granted : I18N.value.permissions.unavailable
}

onMounted(async () => {
	try { await RUNTIME.init() } catch { }
	await refresh()
})
</script>

<template>
	<div class="w-full h-full flex flex-col gap-4 px-6 py-4 scroll-area">
		<AppSectionHeader :title="I18N.title" :subtitle="I18N.subtitle">
			<template #actions>
				<AppButton
					v-if="!safeMode"
					variant="primary"
					size="sm"
					icon="upload"
					:loading="loading"
					@click="beginInstall"
				>{{ I18N.action.install }}</AppButton>
			</template>
		</AppSectionHeader>

		<div v-if="safeMode" class="surface-card border border-line-subtle px-3.5 py-3 flex gap-2.5 items-start">
			<Icon name="info" :size="17" class="text-nori-teal-bright shrink-0 mt-0.5"/>
			<div class="min-w-0">
				<h4 class="m-0 text-sm font-600 text-text-primary">{{ I18N.safeMode.title }}</h4>
				<p class="m-0 mt-1 text-xs text-text-muted leading-relaxed">{{ I18N.safeMode.desc }}</p>
			</div>
		</div>

		<div v-if="loading && plugins.length === 0" class="flex-1 flex items-center justify-center text-text-muted gap-2">
			<Icon name="loading" :size="18" class="spin"/>
		</div>

		<div v-else-if="plugins.length === 0" class="surface-card flex-1 min-h-[14rem] flex flex-col items-center justify-center text-center gap-2 px-6">
			<Icon name="package" :size="30" class="text-text-faint"/>
			<h3 class="m-0 text-base font-600 text-text-primary">{{ I18N.empty.title }}</h3>
			<p class="m-0 max-w-[34rem] text-sm text-text-muted leading-relaxed">{{ I18N.empty.desc }}</p>
			<AppButton v-if="!safeMode" variant="ghost" size="sm" icon="upload" @click="beginInstall">{{ I18N.action.install }}</AppButton>
		</div>

		<div v-else class="grid gap-3 grid-cols-[repeat(auto-fill,minmax(26rem,1fr))] pb-2">
			<article
				v-for="plugin in plugins"
				:key="plugin.id"
				class="surface-card flex flex-col gap-3 px-3.5 py-3 border border-line-subtle"
			>
				<div class="flex items-start justify-between gap-3">
					<div class="flex gap-2.5 min-w-0">
						<div class="w-10 h-10 shrink-0 rounded-sm bg-nori-teal-bright/10 text-nori-teal-bright flex items-center justify-center overflow-hidden">
							<img v-if="plugin.iconUrl" :src="plugin.iconUrl" :alt="plugin.name" class="w-full h-full object-cover"/>
							<Icon v-else name="plug" :size="20"/>
						</div>
						<div class="min-w-0">
							<div class="flex items-center gap-1.5 flex-wrap">
								<h4 class="m-0 text-base font-600 text-text-primary truncate">{{ plugin.name }}</h4>
								<AppChip>{{ statusLabel(plugin.state) }}</AppChip>
								<span class="text-xs text-text-faint mono">v{{ plugin.version }}</span>
							</div>
							<p class="m-0 mt-0.5 text-xs text-text-faint mono truncate">{{ plugin.id }}</p>
							<p v-if="plugin.author" class="m-0 mt-0.5 text-xs text-text-faint">{{ plugin.author }}</p>
						</div>
					</div>
					<Icon v-if="isBusy(plugin)" name="loading" :size="16" class="spin text-nori-teal-bright shrink-0"/>
				</div>

				<p class="m-0 text-xs text-text-muted leading-relaxed">{{ plugin.description }}</p>

				<div class="flex flex-wrap gap-1.5">
					<AppChip v-for="capability in plugin.capabilities" :key="`required:${capability}`">
						{{ capability }} · {{ I18N.permissions.required }} · {{ capabilityLabel(plugin, capability) }}
					</AppChip>
					<AppChip v-for="capability in plugin.optionalCapabilities" :key="`optional:${capability}`">
						{{ capability }} · {{ I18N.permissions.optional }} · {{ capabilityLabel(plugin, capability) }}
					</AppChip>
					<span v-if="plugin.capabilities.length === 0 && plugin.optionalCapabilities.length === 0" class="text-xs text-text-faint">{{ I18N.permissions.none }}</span>
				</div>

				<div v-if="plugin.errorCode || plugin.errorMessage" class="rounded-sm border border-line-subtle bg-bg-deep/45 px-2.5 py-2 text-xs">
					<p v-if="plugin.errorCode" class="m-0 text-text-muted mono">{{ plugin.errorCode }}</p>
					<p v-if="plugin.errorMessage" class="m-0 mt-1 text-text-faint leading-relaxed">{{ plugin.errorMessage }}</p>
				</div>
				<p v-if="plugin.requiresRestart || plugin.state === 'pending_restart'" class="m-0 text-xs text-nori-teal-bright">{{ I18N.restart }}</p>

				<div class="mt-auto pt-1 flex items-center gap-1.5 flex-wrap">
					<AppButton variant="ghost" size="sm" icon="info" @click="detailPlugin = plugin">{{ I18N.action.details }}</AppButton>
					<AppButton
						v-if="plugin.state === 'active'"
						variant="ghost"
						size="sm"
						icon="power"
						:loading="busyId === plugin.id"
						:disabled="isBusy(plugin)"
						@click="disable(plugin)"
					>{{ I18N.action.disable }}</AppButton>
					<AppButton
						v-else-if="plugin.state === 'failed'"
						v-show="!safeMode"
						variant="ghost"
						size="sm"
						icon="refresh"
						:loading="busyId === plugin.id"
						:disabled="isBusy(plugin)"
						@click="enable(plugin)"
					>{{ I18N.action.retry }}</AppButton>
					<AppButton
						v-else-if="plugin.state === 'installed' || plugin.state === 'disabled'"
						v-show="!safeMode"
						variant="primary"
						size="sm"
						icon="power"
						:loading="busyId === plugin.id"
						:disabled="isBusy(plugin)"
						@click="enable(plugin)"
					>{{ I18N.action.enable }}</AppButton>
					<AppButton
						v-if="plugin.state === 'failed'"
						variant="ghost"
						size="sm"
						icon="power"
						:disabled="isBusy(plugin)"
						@click="disable(plugin)"
					>{{ I18N.action.disable }}</AppButton>
					<AppButton
						variant="danger"
						size="sm"
						icon="trash"
						:disabled="isBusy(plugin)"
						@click="beginUninstall(plugin)"
					>{{ I18N.action.uninstall }}</AppButton>
				</div>
			</article>
		</div>

		<AppModal v-model:show="riskModal" :title="I18N.risk.title" :close-label="I18N.action.cancel" :mask-closable="false">
			<p class="m-0 text-sm text-text-muted leading-relaxed">{{ I18N.risk.desc }}</p>
			<template #footer>
				<AppButton variant="ghost" @click="riskModal = false">{{ I18N.action.cancel }}</AppButton>
				<AppButton variant="primary" icon="upload" @click="confirmRisk">{{ I18N.risk.confirm }}</AppButton>
			</template>
		</AppModal>

		<AppModal
			:show="uninstallTarget !== null"
			:title="I18N.uninstall.title"
			:close-label="I18N.action.cancel"
			:mask-closable="false"
			@update:show="value => { if (!value) uninstallTarget = null }"
		>
			<p class="m-0 text-sm text-text-muted leading-relaxed">{{ I18N.uninstall.desc }}</p>
			<label class="surface-card flex items-start gap-2.5 px-3 py-2.5 cursor-pointer">
				<input v-model="deleteData" type="checkbox" class="mt-0.5 accent-nori-teal-bright"/>
				<span class="min-w-0">
					<span class="block text-sm text-text-primary">{{ I18N.uninstall.deleteData }}</span>
					<span class="block mt-0.5 text-xs text-text-faint leading-relaxed">{{ I18N.uninstall.deleteDataHint }}</span>
				</span>
			</label>
			<template #footer>
				<AppButton variant="ghost" @click="uninstallTarget = null">{{ I18N.action.cancel }}</AppButton>
				<AppButton variant="danger" icon="trash" :loading="busyId === uninstallTarget?.id" @click="confirmUninstall">{{ I18N.action.uninstall }}</AppButton>
			</template>
		</AppModal>

		<AppModal
			:show="detailPlugin !== null"
			:title="detailPlugin?.name"
			:close-label="I18N.action.cancel"
			@update:show="value => { if (!value) detailPlugin = null }"
		>
			<div v-if="detailPlugin" class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
				<span class="text-text-faint">{{ I18N.detail.id }}</span><span class="mono text-text-primary break-all">{{ detailPlugin.id }}</span>
				<span class="text-text-faint">{{ I18N.detail.version }}</span><span class="text-text-primary">{{ detailPlugin.version }}</span>
				<span class="text-text-faint">{{ I18N.detail.author }}</span><span class="text-text-primary">{{ detailPlugin.author || '—' }}</span>
				<span class="text-text-faint">{{ I18N.detail.license }}</span><span class="text-text-primary">{{ detailPlugin.license || '—' }}</span>
				<span class="text-text-faint">{{ I18N.detail.homepage }}</span><span class="text-text-primary break-all">{{ detailPlugin.homepage || '—' }}</span>
				<span class="text-text-faint">{{ I18N.detail.repository }}</span><span class="text-text-primary break-all">{{ detailPlugin.repository || '—' }}</span>
				<span class="text-text-faint">{{ I18N.permissions.title }}</span><span class="text-text-primary">{{ [...detailPlugin.capabilities, ...detailPlugin.optionalCapabilities].join(', ') || I18N.permissions.none }}</span>
				<template v-if="detailPlugin.errorCode || detailPlugin.errorMessage">
					<span class="text-text-faint">{{ I18N.detail.error }}</span><span class="text-text-primary break-all">{{ [detailPlugin.errorCode, detailPlugin.errorMessage].filter(Boolean).join(' · ') }}</span>
				</template>
			</div>
			<template #footer>
				<AppButton variant="primary" @click="detailPlugin = null">{{ I18N.action.confirm }}</AppButton>
			</template>
		</AppModal>
	</div>
</template>
