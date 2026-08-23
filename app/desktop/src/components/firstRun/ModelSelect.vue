<script setup lang="ts">
import {ref, onMounted, computed} from "vue"
import useLanguages from "../../services/i18n/useLanguages.ts"
import {RUNTIME} from "../../services/runtime"
import {feedback} from "../../services/feedback"
import Icon from "../../components/Icon.vue"
import {MODEL_LIST} from "../../services/live2d/models"

const I18N = computed(() => useLanguages().components.firstRun.modelSelect)
const WIZARD_I18N = computed(() => useLanguages().views.firstRun)

const emit = defineEmits<{
	/** 保存失败时报错 (空串 = 清除错误) */
	error: [message: string]
	/** 当前选中的模型 id */
	selected: [modelId: string]
}>()

// 可选模型列表
const models = MODEL_LIST

// 选中的模型 id
const selected = ref("arg-nori")
const saving = ref("")

// 组件挂载时读取后端快照
onMounted(async () => {
	try {
		await RUNTIME.init()
		const SAVED = RUNTIME.snapshot.value?.models.selected
		if (SAVED && models.some(model => model.id === SAVED)) selected.value = SAVED
		emit("selected", selected.value)
	} catch (error) {
		feedback.error(WIZARD_I18N.value.error.selectModel, error)
	}
})

// 选中模型: 显式提交 + 失败可见 (原来靠 watch 静默提交, 失败时界面毫无变化)
const selectModel = async (modelId: string): Promise<void> => {
	if (saving.value) return
	const PREVIOUS = selected.value
	selected.value = modelId
	saving.value = modelId
	try {
		await RUNTIME.firstRunSelectModel(modelId)
		emit("error", "")
		emit("selected", modelId)
	} catch (error) {
		selected.value = PREVIOUS
		feedback.error(WIZARD_I18N.value.error.selectModel, error)
		emit("error", WIZARD_I18N.value.error.selectModel)
	} finally {
		saving.value = ""
	}
}
</script>

<template>
	<section key="model-select" class="w-full min-h-full flex flex-col items-center justify-center gap-3.5 px-12 py-3 text-center">
		<div class="flex flex-col items-center gap-1.5">
			<span class="chip-teal">
				<Icon name="package" :size="12"/>
				<span>Character Selection</span>
			</span>
			<h2 class="text-2xl font-700 glow-teal">{{ I18N.title }}</h2>
			<p class="text-sub">{{ I18N.hint }}</p>
		</div>

		<div class="flex flex-row justify-center gap-5">
			<button
				v-for="model in models"
				:key="model.id"
				type="button"
				class="group relative w-[17rem] flex flex-col items-center gap-2 p-2.5 pb-3 rounded-md overflow-hidden
					cursor-pointer border-2 border-line-subtle bg-white/3 transition-all duration-250 focus-ring
					hover:(bg-nori-teal-bright/8 border-nori-teal-soft -translate-y-[0.3rem] shadow-[0_0.8rem_2.4rem_rgba(0,0,0,0.35)])"
				:class="[
					selected === model.id ? 'border-nori-teal bg-nori-teal-bright/12 shadow-[0_0.8rem_2.4rem_rgba(0,0,0,0.4),0_0_2rem_var(--glow-teal)]' : '',
					saving === model.id ? 'opacity-75 cursor-progress' : '',
				]"
				:aria-pressed="selected === model.id"
				@click="selectModel(model.id)"
			>
				<span class="relative w-full aspect-[3/4] max-h-[16rem] rounded-sm overflow-hidden border border-line-subtle bg-black/30">
					<img
						class="w-full h-full object-cover object-top transition-transform duration-300 group-hover:scale-103"
						:src="model.thumb"
						:alt="model.name"
					/>
					<span class="absolute inset-0 bg-gradient-to-b from-transparent via-transparent to-bg-abyss/80 pointer-events-none"/>
					<span
						class="absolute top-2 right-2 w-[2.2rem] h-[2.2rem] rounded-full flex items-center justify-center
							bg-nori-teal text-on-teal shadow-[0_0.2rem_0.8rem_rgba(0,0,0,0.4)] transition-all duration-200"
						:class="selected === model.id || saving === model.id ? 'opacity-100 scale-100' : 'opacity-0 scale-60'"
					>
						<Icon :name="saving === model.id ? 'loading' : 'check'" :class="{spin: saving === model.id}" :size="12"/>
					</span>
				</span>

				<span
					class="text-md font-500"
					:class="selected === model.id ? 'text-nori-teal-bright font-600' : 'text-text-primary'"
				>{{ model.name }}</span>
			</button>
		</div>
	</section>
</template>
