<script setup lang="ts">
/**
 * 反馈层宿主
 *
 * 挂在 naive 各 provider 内部, 把 message/dialog 实例登记到 services/feedback,
 * 让非组件代码也能弹提示。自身不渲染任何东西。
 */
import {useDialog, useMessage} from "naive-ui"
import {registerFeedback} from "../../services/feedback"

registerFeedback(useMessage(), useDialog())
</script>

<template>
	<span class="hidden" aria-hidden="true"/>
</template>
