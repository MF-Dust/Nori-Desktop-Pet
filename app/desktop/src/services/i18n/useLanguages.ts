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
				}
			}
		},
		views: {
			firstRun: {
				back: t("views.firstRun.back"),
				next: t("views.firstRun.next"),
			}
		}
	}
}