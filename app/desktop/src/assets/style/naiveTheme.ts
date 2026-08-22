import {darkTheme, type GlobalThemeOverrides} from "naive-ui"

/**
 * Nori Desktop Pet - Naive UI 深海微光暗黑主题覆盖配置
 */
export const naiveDarkTheme = darkTheme

export const naiveThemeOverrides: GlobalThemeOverrides = {
	common: {
		primaryColor: "#5eead4",
		primaryColorHover: "#7de3ff",
		primaryColorPressed: "#2dd4bf",
		primaryColorSuppl: "rgba(94, 234, 212, 0.15)",
		infoColor: "#7de3ff",
		infoColorHover: "#a5f3fc",
		infoColorPressed: "#38bdf8",
		successColor: "#5eead4",
		warningColor: "#fbbf24",
		errorColor: "#fb7185",
		textColorBase: "#f0f8ff",
		textColor1: "#f0f8ff",
		textColor2: "#c7d9e8",
		textColor3: "#8ba8be",
		textColorDisabled: "#4a677d",
		placeholderColor: "rgba(139, 168, 190, 0.55)",
		bodyColor: "#050e1a",
		cardColor: "rgba(8, 24, 38, 0.75)",
		modalColor: "rgba(6, 18, 30, 0.95)",
		popoverColor: "rgba(10, 28, 44, 0.96)",
		borderColor: "rgba(125, 227, 255, 0.15)",
		dividerColor: "rgba(125, 227, 255, 0.12)",
		borderRadius: "0.8rem",
		borderRadiusSmall: "0.6rem",
		fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
		fontSize: "1.25rem",
		fontSizeSmall: "1.15rem",
	},
	Slider: {
		fillColor: "#5eead4",
		fillColorHover: "#7de3ff",
		dotColor: "#5eead4",
		dotBorder: "0.2rem solid #050e1a",
		handleSize: "1.4rem",
		railColor: "rgba(255, 255, 255, 0.12)",
		railColorHover: "rgba(255, 255, 255, 0.2)",
		railHeight: "0.5rem",
	},
	Switch: {
		railColorActive: "#5eead4",
		buttonColor: "#050e1a",
		boxShadowFocus: "0 0 1.2rem rgba(94, 234, 212, 0.4)",
	},
	Select: {
		peers: {
			InternalSelection: {
				color: "rgba(255, 255, 255, 0.04)",
				colorActive: "rgba(125, 227, 255, 0.08)",
				border: "0.1rem solid rgba(125, 227, 255, 0.15)",
				borderHover: "0.1rem solid rgba(125, 227, 255, 0.4)",
				borderFocus: "0.1rem solid #5eead4",
				boxShadowFocus: "0 0 1rem rgba(94, 234, 212, 0.25)",
				borderRadius: "0.6rem",
				textColor: "#f0f8ff",
			},
			InternalSelectMenu: {
				color: "rgba(8, 24, 40, 0.96)",
				optionTextColor: "#c7d9e8",
				optionTextColorActive: "#5eead4",
				optionColorPending: "rgba(125, 227, 255, 0.1)",
				optionColorActive: "rgba(125, 227, 255, 0.16)",
				borderRadius: "0.6rem",
			},
		},
	},
	Input: {
		color: "rgba(255, 255, 255, 0.04)",
		colorFocus: "rgba(125, 227, 255, 0.06)",
		border: "0.1rem solid rgba(125, 227, 255, 0.15)",
		borderHover: "0.1rem solid rgba(125, 227, 255, 0.4)",
		borderFocus: "0.1rem solid #5eead4",
		boxShadowFocus: "0 0 1.2rem rgba(94, 234, 212, 0.25)",
		borderRadius: "0.6rem",
		textColor: "#f0f8ff",
	},
	Button: {
		borderRadiusMedium: "0.6rem",
		borderRadiusSmall: "0.5rem",
		textColorPrimary: "#03101c",
		colorHoverPrimary: "#7de3ff",
		colorPrimary: "#5eead4",
		colorPressedPrimary: "#2dd4bf",
		colorFocusPrimary: "#5eead4",
	},
	Modal: {
		boxShadow: "0 1.6rem 4rem rgba(0, 0, 0, 0.7), 0 0 2.4rem rgba(94, 234, 212, 0.12)",
		borderRadius: "1.2rem",
	},
	Card: {
		borderRadius: "1rem",
		borderColor: "rgba(125, 227, 255, 0.12)",
	},
	Popconfirm: {
		borderRadius: "0.8rem",
	},
	Dialog: {
		borderRadius: "1rem",
		color: "rgba(8, 24, 40, 0.96)",
		titleTextColor: "#ecf8ff",
		contentTextColor: "#cfdde5",
		boxShadow: "0 1.6rem 4rem rgba(0, 0, 0, 0.75), 0 0 2.4rem rgba(94, 234, 212, 0.15)",
	},
	Tabs: {
		tabTextColorLine: "#7e94a3",
		tabTextColorActiveLine: "#5eead4",
		tabTextColorHoverLine: "#7de3ff",
		barColor: "#5eead4",
	},
	Tag: {
		borderRadius: "99.9rem",
	},
	Tooltip: {
		borderRadius: "0.6rem",
		color: "rgba(6, 20, 32, 0.95)",
		textColor: "#f0f8ff",
		boxShadow: "0 0.4rem 1.6rem rgba(0, 0, 0, 0.5), 0 0 1rem rgba(94, 234, 212, 0.15)",
	},
	Message: {
		borderRadius: "0.8rem",
		boxShadowInfo: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(125, 227, 255, 0.25)",
		boxShadowSuccess: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(94, 234, 212, 0.25)",
		boxShadowError: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(251, 113, 133, 0.25)",
	},
}
