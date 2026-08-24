<script setup lang="ts">
import Icon from "../Icon.vue"

export interface ChatDisplayBubble {
	key: string
	role: string
	content: string
	html?: string
	isFirstInGroup: boolean
}

const props = defineProps<{
	bubble: ChatDisplayBubble
	copied: boolean
	copyLabel: string
	copiedLabel: string
}>()

const emit = defineEmits<{
	copy: [key: string, content: string]
}>()

const handleCopy = () => {
	emit("copy", props.bubble.key, props.bubble.content)
}
</script>

<template>
	<div
		v-memo="[props.bubble.content, props.bubble.html, props.bubble.isFirstInGroup, props.copied, props.copyLabel, props.copiedLabel]"
		class="group flex max-w-[84%] animate-bubble-in"
		:class="[
			props.bubble.role === 'user' ? 'self-end flex-row-reverse' : 'self-start',
			props.bubble.isFirstInGroup && props.bubble.role === 'assistant' ? 'mt-1' : '',
		]"
	>
		<div class="relative flex items-center gap-1.5">
			<div
				class="px-4.5 py-3 text-base leading-relaxed break-words"
				:class="props.bubble.role === 'user'
					? 'rounded-[1.4rem_1.4rem_0.4rem_1.4rem] text-text-primary border border-nori-teal-bright/40 bg-[linear-gradient(135deg,rgba(94,234,212,0.22)_0%,rgba(125,227,255,0.1)_100%)] shadow-[0_0.4rem_2rem_rgba(94,234,212,0.12)] whitespace-pre-wrap'
					: 'rounded-[1.4rem_1.4rem_1.4rem_0.4rem] text-text-primary border border-line-subtle bg-bg-card/85 backdrop-blur-[1.2rem] shadow-[0_0.4rem_2rem_rgba(0,0,0,0.35)] chat-markdown'"
			>
				<span v-if="props.bubble.role === 'user'">{{ props.bubble.content }}</span>
				<!-- eslint-disable-next-line vue/no-v-html -->
				<div v-else v-html="props.bubble.html"/>
			</div>

			<button
				type="button"
				class="btn-icon w-6.5 h-6.5 shrink-0 opacity-0 pointer-events-none transition-opacity duration-200
					group-hover:(opacity-100 pointer-events-auto) group-focus-within:(opacity-100 pointer-events-auto)"
				:class="props.copied ? 'opacity-100 pointer-events-auto text-success' : ''"
				:title="props.copied ? props.copiedLabel : props.copyLabel"
				:aria-label="props.copied ? props.copiedLabel : props.copyLabel"
				@click="handleCopy"
			>
				<Icon :name="props.copied ? 'check' : 'copy'" :size="12"/>
			</button>
		</div>
	</div>
</template>
