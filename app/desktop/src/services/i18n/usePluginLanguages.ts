import {i18n} from "./index"

export default () => {
	const t = i18n.global.t
	return {
		settingsTab: t("views.main.settingsTabs.plugins"),
		plugins: {
			title: t("views.main.plugins.title"),
			subtitle: t("views.main.plugins.subtitle"),
			action: {
				install: t("views.main.plugins.action.install"),
				enable: t("views.main.plugins.action.enable"),
				disable: t("views.main.plugins.action.disable"),
				retry: t("views.main.plugins.action.retry"),
				uninstall: t("views.main.plugins.action.uninstall"),
				details: t("views.main.plugins.action.details"),
				cancel: t("views.main.plugins.action.cancel"),
				confirm: t("views.main.plugins.action.confirm"),
			},
			empty: {
				title: t("views.main.plugins.empty.title"),
				desc: t("views.main.plugins.empty.desc"),
			},
			status: {
				installed: t("views.main.plugins.status.installed"),
				loading: t("views.main.plugins.status.loading"),
				active: t("views.main.plugins.status.active"),
				stopping: t("views.main.plugins.status.stopping"),
				disabled: t("views.main.plugins.status.disabled"),
				failed: t("views.main.plugins.status.failed"),
				incompatible: t("views.main.plugins.status.incompatible"),
				pending_restart: t("views.main.plugins.status.pending_restart"),
			},
			permissions: {
				title: t("views.main.plugins.permissions.title"),
				required: t("views.main.plugins.permissions.required"),
				optional: t("views.main.plugins.permissions.optional"),
				granted: t("views.main.plugins.permissions.granted"),
				unavailable: t("views.main.plugins.permissions.unavailable"),
				undeclared: t("views.main.plugins.permissions.undeclared"),
				none: t("views.main.plugins.permissions.none"),
			},
			risk: {
				title: t("views.main.plugins.risk.title"),
				desc: t("views.main.plugins.risk.desc"),
				confirm: t("views.main.plugins.risk.confirm"),
			},
			uninstall: {
				title: t("views.main.plugins.uninstall.title"),
				desc: t("views.main.plugins.uninstall.desc"),
				deleteData: t("views.main.plugins.uninstall.deleteData"),
				deleteDataHint: t("views.main.plugins.uninstall.deleteDataHint"),
			},
			safeMode: {
				title: t("views.main.plugins.safeMode.title"),
				desc: t("views.main.plugins.safeMode.desc"),
			},
			restart: t("views.main.plugins.restart"),
			error: {
				load: t("views.main.plugins.error.load"),
				install: t("views.main.plugins.error.install"),
				enable: t("views.main.plugins.error.enable"),
				disable: t("views.main.plugins.error.disable"),
				uninstall: t("views.main.plugins.error.uninstall"),
			},
			detail: {
				id: t("views.main.plugins.detail.id"),
				version: t("views.main.plugins.detail.version"),
				author: t("views.main.plugins.detail.author"),
				license: t("views.main.plugins.detail.license"),
				homepage: t("views.main.plugins.detail.homepage"),
				repository: t("views.main.plugins.detail.repository"),
				error: t("views.main.plugins.detail.error"),
			},
		},
	}
}
