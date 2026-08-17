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
			},
			main: {
				live2d: {
					title: t("components.main.live2d.title"),
					notReady: t("components.main.live2d.notReady"),
					hint: t("components.main.live2d.hint"),
					state: {
						unmounted: t("components.main.live2d.state.unmounted"),
						loading: t("components.main.live2d.state.loading"),
						ready: t("components.main.live2d.state.ready"),
						missing: t("components.main.live2d.state.missing"),
						error: t("components.main.live2d.state.error"),
					}
				},
				settings: {
					model: {
						title: t("components.main.settings.model.title"),
						sub: t("components.main.settings.model.sub"),
						notInstalled: t("components.main.settings.model.notInstalled"),
						installed: t("components.main.settings.model.installed"),
						current: t("components.main.settings.model.current"),
					},
					language: {
						title: t("components.main.settings.language.title"),
						sub: t("components.main.settings.language.sub"),
						current: t("components.main.settings.language.current"),
					}
				}
			},
			pet: {
				bubble: {
					default: t("components.pet.bubble.default"),
					thinking: t("components.pet.bubble.thinking"),
				},
				dialog: {
					placeholder: t("components.pet.dialog.placeholder"),
					send: t("components.pet.dialog.send"),
				},
				hint: t("components.pet.hint"),
			}
		},
		views: {
			firstRun: {
				back: t("views.firstRun.back"),
				next: t("views.firstRun.next"),
				start: t("views.firstRun.start"),
			},
			main: {
				title: t("views.main.title"),
				collapse: t("views.main.collapse"),
				expand: t("views.main.expand"),
				empty: t("views.main.empty"),
				close: t("views.main.close"),
				minimize: t("views.main.minimize"),
				nav: {
					live2d: t("views.main.nav.live2d"),
					settings: t("views.main.nav.settings"),
				}
			}
		}
	}
}
