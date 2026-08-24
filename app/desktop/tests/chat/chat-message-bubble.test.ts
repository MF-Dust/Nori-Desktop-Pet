import {afterEach, describe, expect, it, vi} from "vitest"
import {createApp, h, nextTick, ref} from "vue"
import ChatMessageBubble, {type ChatDisplayBubble} from "../../src/components/chat/ChatMessageBubble.vue"

describe("ChatMessageBubble", () => {
	const MOUNTS: Array<{app: ReturnType<typeof createApp>; container: HTMLDivElement}> = []

	afterEach(() => {
		for (const mount of MOUNTS) {
			mount.app.unmount()
			mount.container.remove()
		}
		MOUNTS.length = 0
	})

	it("renders a message and emits its copy payload", () => {
		const BUBBLE: ChatDisplayBubble = {
			key: "assistant-1",
			role: "assistant",
			content: "Hello",
			html: "<p>Hello</p>",
			isFirstInGroup: true,
		}
		const COPY = vi.fn()
		const CONTAINER = document.createElement("div")
		const APP = createApp({
			render: () => h(ChatMessageBubble, {
				bubble: BUBBLE,
				copied: false,
				copyLabel: "Copy",
				copiedLabel: "Copied",
				onCopy: COPY,
			}),
		})
		document.body.appendChild(CONTAINER)
		APP.mount(CONTAINER)
		MOUNTS.push({app: APP, container: CONTAINER})

		expect(CONTAINER.querySelector(".chat-markdown > div")?.innerHTML).toBe("<p>Hello</p>")
		expect(CONTAINER.querySelector("button")?.getAttribute("aria-label")).toBe("Copy")
		CONTAINER.querySelector("button")?.dispatchEvent(new MouseEvent("click", {bubbles: true}))
		expect(COPY).toHaveBeenCalledWith("assistant-1", "Hello")
	})

	it("updates copy state and accessible label", async () => {
		const COPIED = ref(false)
		const CONTAINER = document.createElement("div")
		const APP = createApp({
			render: () => h(ChatMessageBubble, {
				bubble: {key: "user-1", role: "user", content: "Hi", isFirstInGroup: true},
				copied: COPIED.value,
				copyLabel: "Copy",
				copiedLabel: "Copied",
			}),
		})
		document.body.appendChild(CONTAINER)
		APP.mount(CONTAINER)
		MOUNTS.push({app: APP, container: CONTAINER})

		expect(CONTAINER.querySelector("button")?.getAttribute("aria-label")).toBe("Copy")
		COPIED.value = true
		await nextTick()
		expect(CONTAINER.querySelector("button")?.getAttribute("aria-label")).toBe("Copied")
	})
})
