import {createI18n} from "vue-i18n"
import {invoke} from "@tauri-apps/api/core"
import useLanguages from "./useLanguages.ts"

/**
 * 语言类型
 */
export type LanguageType = string

const MESSAGES: Record<string, () => Promise<any>> = import.meta.glob("./locales/*.ts")

/**
 * 获取系统语言, 映射到可用的 locale 文件名
 */
const getSystemLanguage = async (): Promise<string> => {
	let lang
	try {
		lang = await invoke<string>("get_system_language")
	} catch (error) {
		lang = navigator.language
		console.error("获取系统语言失败:", error)
	}
	const KEY = `./locales/${lang}.ts`
	if (MESSAGES[KEY]) return lang
	const PREFIX = lang.split("-")[0]
	const PREFIX_KEY = `./locales/${PREFIX}.ts`
	if (MESSAGES[PREFIX_KEY]) return PREFIX
	return "zh-CN"
}

/**
 * 国际化实例
 */
export const i18n = createI18n({
	legacy: false,
	locale: "zh-CN",
	fallbackLocale: "zh-CN",
	messages: {}
})

// 配置键名
const CONFIG_KEY = "language"

const useLanguage = {
	useLang: {} as ReturnType<typeof useLanguages>,
	/**
	 * 获取当前语言
	 */
	async getLanguage(): Promise<LanguageType> {
		try {
			const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
			if (typeof SAVED === "string" && SAVED) return SAVED
		} catch (error) {
			console.error("读取语言配置失败:", error)
		}
		return getSystemLanguage()
	},
	/**
	 * 初始化：加载当前语言包
	 */
	async init(): Promise<void> {
		const LANG = await this.getLanguage()
		await this.setLanguage(LANG)
	},
	/**
	 * 设置当前语言
	 */
	async setLanguage(lang: LanguageType): Promise<void> {
		try {
			await invoke("set_config", {key: CONFIG_KEY, value: lang})
			await invoke("write_log", {level: "info", message: `切换语言: ${lang}`})
		} catch (error) {
			console.error("保存语言配置失败:", error)
		}
		if (!i18n.global.availableLocales.includes(lang)) {
			const LOADER = this.getLoader(lang)
			if (LOADER) {
				const MODULE = await LOADER()
				i18n.global.setLocaleMessage(lang, MODULE.default)
			}
		}
		i18n.global.locale.value = lang
		this.useLang = useLanguages()
	},
	/**
	 * 获取可用语言列表
	 */
	async getLanguages(): Promise<string[]> {
		try {
			const LANGUAGES = Object.keys(MESSAGES).map((key) => key.replace("./locales/", "").replace(".ts", ""))
			await invoke("write_log", {level: "info", message: `可用语言列表: ${LANGUAGES}`})
			return LANGUAGES
		} catch (error) {
			console.error("获取可用语言列表失败:", error)
		}
		return []
	},
	/**
	 * 获取 loader
	 */
	getLoader(lang: string) {
		const KEY = `./locales/${lang}.ts`
		return MESSAGES[KEY]
	}
}

export default useLanguage
