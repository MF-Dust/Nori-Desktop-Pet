import {invoke} from "@tauri-apps/api/core"
import {i18n} from "../i18n"
import {live2dController} from "../live2d"

type Listener = () => void

interface BubbleState {
	visible: boolean
	message: string
}

interface DialogState {
	visible: boolean
}

class PetChatController {
	private bubble: BubbleState = {visible: false, message: ""}
	private dialog: DialogState = {visible: false}
	private bubbleTimer: ReturnType<typeof setTimeout> | null = null
	private readonly listeners = new Set<Listener>()

	getBubbleState(): BubbleState {
		return {...this.bubble}
	}

	getDialogState(): DialogState {
		return {...this.dialog}
	}

	showBubble(message: string, duration = 5000): void {
		if (this.bubbleTimer) {
			clearTimeout(this.bubbleTimer)
			this.bubbleTimer = null
		}
		this.bubble = {visible: true, message}
		this.notify()
		void invoke("write_log", {level: "info", message: i18n.global.t("log.pet.bubbleShown", {message: message.slice(0, 80)})}).catch(() => {})
		if (duration > 0) {
			this.bubbleTimer = setTimeout(() => this.hideBubble(), duration)
		}
	}

	hideBubble(): void {
		if (this.bubbleTimer) {
			clearTimeout(this.bubbleTimer)
			this.bubbleTimer = null
		}
		if (!this.bubble.visible) return
		this.bubble = {visible: false, message: ""}
		this.notify()
		void invoke("write_log", {level: "info", message: i18n.global.t("log.pet.bubbleHidden")}).catch(() => {})
	}

	showDialog(): void {
		if (this.dialog.visible) return
		this.dialog = {visible: true}
		this.notify()
		void invoke("write_log", {level: "info", message: i18n.global.t("log.pet.dialogShown")}).catch(() => {})
	}

	hideDialog(): void {
		if (!this.dialog.visible) return
		this.dialog = {visible: false}
		this.notify()
		void invoke("write_log", {level: "info", message: i18n.global.t("log.pet.dialogHidden")}).catch(() => {})
	}

	toggleDialog(): void {
		if (this.dialog.visible) this.hideDialog()
		else this.showDialog()
	}

	async sendMessage(text: string): Promise<void> {
		void invoke("write_log", {level: "info", message: i18n.global.t("log.pet.messageSent", {text: text.slice(0, 80)})}).catch(() => {})
		this.showBubble(i18n.global.t("components.pet.bubble.thinking"), 0)
		live2dController.setEmotion("surprised", 0.6)
		live2dController.playMotion("TapBody").catch(() => {})

		const RESPONSE = await this.fetchAIResponse(text)
		this.showBubble(RESPONSE, 8000)
		live2dController.setEmotion("happy", 0.8)
		live2dController.playMotion("TapBody").catch(() => {})
	}

	private async fetchAIResponse(text: string): Promise<string> {
		try {
			const BASE_URL = await invoke<string>("get_config", {key: "llm_base_url"})
			const API_KEY = await invoke<string>("get_config", {key: "llm_api_key"})
			const MODEL = await invoke<string>("get_config", {key: "llm_model"})
			if (!BASE_URL || !API_KEY || !MODEL) {
				return i18n.global.t("components.pet.bubble.default")
			}
			const RES = await fetch(`${BASE_URL}/chat/completions`, {
				method: "POST",
				headers: {
					"Content-Type": "application/json",
					"Authorization": `Bearer ${API_KEY}`,
				},
				body: JSON.stringify({
					model: MODEL,
					messages: [
						{role: "system", content: "You are Nori, a cute desktop pet companion. Keep responses short and playful, under 50 words. Respond in the user's language."},
						{role: "user", content: text},
					],
					max_tokens: 100,
					temperature: 0.8,
				}),
			})
			if (!RES.ok) throw new Error(`HTTP ${RES.status}`)
			const DATA = await RES.json()
			const CONTENT = DATA?.choices?.[0]?.message?.content
			return CONTENT?.trim() || i18n.global.t("components.pet.bubble.default")
		} catch {
			return i18n.global.t("components.pet.bubble.default")
		}
	}

	subscribe(cb: Listener): () => void {
		this.listeners.add(cb)
		return () => this.listeners.delete(cb)
	}

	private notify(): void {
		for (const cb of this.listeners) {
			try {
				cb()
			} catch {
			}
		}
	}

	destroy(): void {
		if (this.bubbleTimer) {
			clearTimeout(this.bubbleTimer)
			this.bubbleTimer = null
		}
		this.bubble = {visible: false, message: ""}
		this.dialog = {visible: false}
		this.listeners.clear()
	}
}

export const petChatController = new PetChatController()
