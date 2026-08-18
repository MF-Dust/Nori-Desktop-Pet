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
			},
			main: {
				nav: {
					home: t("views.main.nav.home"),
					talk: t("views.main.nav.talk"),
					model: t("views.main.nav.model"),
					settings: t("views.main.nav.settings"),
					about: t("views.main.nav.about"),
				},
				about: {
					title: t("views.main.about.title"),
					license: t("views.main.about.license"),
					authors: t("views.main.about.authors"),
					desc: t("views.main.about.desc"),
				},
				summonPet: t("views.main.summonPet"),
				hidePet: t("views.main.hidePet"),
				placeholderPrefix: t("views.main.placeholderPrefix"),
				placeholderSuffix: t("views.main.placeholderSuffix"),
				chat: {
					notConfigured: t("views.main.chat.notConfigured"),
					notConfiguredDesc: t("views.main.chat.notConfiguredDesc"),
					goSettings: t("views.main.chat.goSettings"),
					inputPlaceholder: t("views.main.chat.inputPlaceholder"),
					sending: t("views.main.chat.sending"),
					failed: t("views.main.chat.failed"),
				},
				model: {
					title: t("views.main.model.title"),
					sub: t("views.main.model.sub"),
					installed: t("views.main.model.installed"),
					notInstalled: t("views.main.model.notInstalled"),
					current: t("views.main.model.current"),
					download: t("views.main.model.download"),
					downloading: t("views.main.model.downloading"),
					downloadDone: t("views.main.model.downloadDone"),
					extracting: t("views.main.model.extracting"),
					ready: t("views.main.model.ready"),
					downloadFailed: t("views.main.model.downloadFailed"),
					enable: t("views.main.model.enable"),
					enabled: t("views.main.model.enabled"),
					adjust: t("views.main.model.adjust"),
					adjustTitle: t("views.main.model.adjustTitle"),
					done: t("views.main.model.done"),
					scale: t("views.main.model.scale"),
					expression: t("views.main.model.expression"),
					expressionNone: t("views.main.model.expressionNone"),
					expressionHint: t("views.main.model.expressionHint"),
					expressionSelected: t("views.main.model.expressionSelected"),
					expressionCount: t("views.main.model.expressionCount"),
					expressionSelectHint: t("views.main.model.expressionSelectHint"),
					back: t("views.main.model.back"),
					expressionNames: {
						Default: t("views.main.model.expressionNames.Default"),
						KiraKira: t("views.main.model.expressionNames.KiraKira"),
						Dizzy: t("views.main.model.expressionNames.Dizzy"),
						Angry: t("views.main.model.expressionNames.Angry"),
						Shy: t("views.main.model.expressionNames.Shy"),
						Dark: t("views.main.model.expressionNames.Dark"),
						Speechless: t("views.main.model.expressionNames.Speechless"),
						Smile: t("views.main.model.expressionNames.Smile"),
						Tears: t("views.main.model.expressionNames.Tears"),
						Troubled: t("views.main.model.expressionNames.Troubled"),
						Doubt: t("views.main.model.expressionNames.Doubt"),
						Disgust: t("views.main.model.expressionNames.Disgust"),
						Serious: t("views.main.model.expressionNames.Serious"),
						Happy: t("views.main.model.expressionNames.Happy"),
						Surprised: t("views.main.model.expressionNames.Surprised"),
						Sleep: t("views.main.model.expressionNames.Sleep"),
						Chibi: t("views.main.model.expressionNames.Chibi"),
						Shojo: t("views.main.model.expressionNames.Shojo"),
						TailOFF: t("views.main.model.expressionNames.TailOFF"),
						LongHairOFF: t("views.main.model.expressionNames.LongHairOFF"),
						Finale_Default: t("views.main.model.expressionNames.Finale_Default"),
						Finale_EyeClosed_Smile: t("views.main.model.expressionNames.Finale_EyeClosed_Smile"),
						Finale_EyeClosed: t("views.main.model.expressionNames.Finale_EyeClosed"),
						Finale_Farewell: t("views.main.model.expressionNames.Finale_Farewell"),
						Finale_Sad_Smile: t("views.main.model.expressionNames.Finale_Sad_Smile"),
						Finale_Sad: t("views.main.model.expressionNames.Finale_Sad"),
						Finale_Smile: t("views.main.model.expressionNames.Finale_Smile"),
					},
				},
				ai: {
					title: t("views.main.ai.title"),
					sub: t("views.main.ai.sub"),
					apiBaseUrl: t("views.main.ai.apiBaseUrl"),
					apiKey: t("views.main.ai.apiKey"),
					model: t("views.main.ai.model"),
					modelEmpty: t("views.main.ai.modelEmpty"),
					getModel: t("views.main.ai.getModel"),
					getting: t("views.main.ai.getting"),
					error: {
						apiBaseUrl: t("views.main.ai.error.apiBaseUrl"),
						apiKey: t("views.main.ai.error.apiKey"),
					},
				},
			}
		}
	}
}