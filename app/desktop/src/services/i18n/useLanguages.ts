import {i18n} from "./index"

export default () => {
	const t = i18n.global.t
	return {
		components: {
			firstRun: {
				welcome: {
					title: t("components.firstRun.welcome.title"),
					subtitle: t("components.firstRun.welcome.subtitle"),
					links: {
						steam: {
							label: t("components.firstRun.welcome.links.steam.label"),
							sub: t("components.firstRun.welcome.links.steam.sub"),
						},
						noriOS: {
							label: t("components.firstRun.welcome.links.noriOS.label"),
							sub: t("components.firstRun.welcome.links.noriOS.sub"),
						},
						qq: {
							label: t("components.firstRun.welcome.links.qq.label"),
							sub: t("components.firstRun.welcome.links.qq.sub"),
						},
						bilibili: {
							label: t("components.firstRun.welcome.links.bilibili.label"),
							sub: t("components.firstRun.welcome.links.bilibili.sub"),
						}
					}
				},
				languageSelect: {
					title: t("components.firstRun.languageSelect.title"),
					langEmpty: t("components.firstRun.languageSelect.langEmpty"),
				},
				modelSelect: {
					title: t("components.firstRun.modelSelect.title"),
					sub: t("components.firstRun.modelSelect.sub"),
				},
				llmConnect: {
					error: {
						apiBaseUrl: t("components.firstRun.llmConnect.error.apiBaseUrl"),
						apiKey: t("components.firstRun.llmConnect.error.apiKey"),
					},
					title: t("components.firstRun.llmConnect.title"),
					sub: t("components.firstRun.llmConnect.sub"),
					apiBaseUrl: t("components.firstRun.llmConnect.apiBaseUrl"),
					apiKey: t("components.firstRun.llmConnect.apiKey"),
					model: t("components.firstRun.llmConnect.model"),
					modelEmpty: t("components.firstRun.llmConnect.modelEmpty"),
					getModel: t("components.firstRun.llmConnect.getModel"),
					getting: t("components.firstRun.llmConnect.getting"),
				},
				ready: {
					title: t("components.firstRun.ready.title"),
					desc: t("components.firstRun.ready.desc"),
					initDesc: t("components.firstRun.ready.initDesc"),
				}
			}
		},
		views: {
			firstRun: {
				back: t("views.firstRun.back"),
				next: t("views.firstRun.next"),
				start: t("views.firstRun.start"),
			},
			init: {
				title: t("views.init.title"),
				live2d: t("views.init.live2d"),
				downloading: t("download.downloading"),
				downloadDone: t("download.downloadDone"),
				extracting: t("download.extracting"),
				ready: t("download.ready"),
				installed: t("download.installed"),
				downloadFailed: t("download.downloadFailed"),
				check: t("download.check"),
			},
			pet: {
				hint: t("views.pet.hint"),
			}
		}
	}
}