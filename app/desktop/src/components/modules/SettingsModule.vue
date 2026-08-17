<script setup lang="ts">
import ModelSetting from "./ModelSetting.vue"
import LanguageSetting from "./LanguageSetting.vue"
</script>

<template>
	<section class="settings-module">
		<ModelSetting/>
		<LanguageSetting/>
	</section>
</template>

<style scoped lang="less">
.settings-module {
	width: 100%;
	height: 100%;
	display: flex;
	flex-direction: column;
	gap: 1.6rem;
	overflow-y: auto;
	padding: 0.4rem;
}
</style>
