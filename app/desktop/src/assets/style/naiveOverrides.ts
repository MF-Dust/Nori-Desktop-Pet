import type {GlobalThemeOverrides} from "naive-ui"
import {COLORS, FONT_SIZES, RADIUS} from "./tokens"

/**
 * Naive UI 深海微光暗黑主题覆盖表 (纯令牌派生)
 *
 * 纪律: naive 组件的外观只在这里调, 组件里不要用原子类去覆盖 naive 的内部 DOM
 * (naive 的样式是运行时注入的 CSS-in-JS, 注入顺序不受我们控制)。
 * 本文件不引入 naive 运行时, 因此可在 node 环境直接单测。
 */

/** naive 不接受 var(...) 作为部分主题值, 因此这里直接用令牌字面量 */
const C = COLORS

export const naiveThemeOverrides: GlobalThemeOverrides = {
	common: {
		primaryColor: C["nori-teal"],
		primaryColorHover: C["nori-teal-bright"],
		primaryColorPressed: "#2dd4bf",
		primaryColorSuppl: "rgba(94, 234, 212, 0.15)",
		infoColor: C["nori-teal-bright"],
		infoColorHover: "#a5f3fc",
		infoColorPressed: "#38bdf8",
		successColor: C.success,
		warningColor: C.warning,
		errorColor: C["danger-text"],
		textColorBase: C["text-primary"],
		textColor1: C["text-primary"],
		textColor2: C["text-body"],
		textColor3: C["text-muted"],
		// 禁用态只需 ≥3:1, 但旧值 #4a677d 只有 2.3:1
		textColorDisabled: "#6a8496",
		placeholderColor: "rgba(157, 178, 192, 0.75)",
		bodyColor: C["bg-abyss"],
		cardColor: C["bg-glass"],
		modalColor: C["bg-glass-modal"],
		popoverColor: "rgba(10, 28, 44, 0.96)",
		borderColor: C["line-subtle"],
		dividerColor: C["line-subtle"],
		borderRadius: RADIUS.sm,
		borderRadiusSmall: "0.6rem",
		fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
		fontSize: FONT_SIZES.base[0],
		fontSizeSmall: FONT_SIZES.sm[0],
	},
	Slider: {
		fillColor: C["nori-teal"],
		fillColorHover: C["nori-teal-bright"],
		dotColor: C["nori-teal"],
		dotBorder: `0.2rem solid ${C["bg-abyss"]}`,
		handleSize: "1.4rem",
		railColor: "rgba(255, 255, 255, 0.12)",
		railColorHover: "rgba(255, 255, 255, 0.2)",
		railHeight: "0.5rem",
	},
	Switch: {
		railColorActive: C["nori-teal"],
		buttonColor: C["bg-abyss"],
		boxShadowFocus: "0 0 1.2rem rgba(94, 234, 212, 0.4)",
	},
	Select: {
		peers: {
			InternalSelection: {
				color: "rgba(255, 255, 255, 0.04)",
				colorActive: "rgba(125, 227, 255, 0.08)",
				border: `0.1rem solid ${C["line-subtle"]}`,
				borderHover: "0.1rem solid rgba(125, 227, 255, 0.4)",
				borderFocus: `0.1rem solid ${C["nori-teal"]}`,
				boxShadowFocus: "0 0 1rem rgba(94, 234, 212, 0.25)",
				borderRadius: "0.6rem",
				textColor: C["text-primary"],
				placeholderColor: "rgba(157, 178, 192, 0.75)",
			},
			InternalSelectMenu: {
				color: "rgba(8, 24, 40, 0.96)",
				optionTextColor: C["text-body"],
				optionTextColorActive: C["nori-teal"],
				optionColorPending: "rgba(125, 227, 255, 0.1)",
				optionColorActive: "rgba(125, 227, 255, 0.16)",
				borderRadius: "0.6rem",
			},
		},
	},
	Input: {
		color: "rgba(255, 255, 255, 0.04)",
		colorFocus: "rgba(125, 227, 255, 0.06)",
		border: `0.1rem solid ${C["line-subtle"]}`,
		borderHover: "0.1rem solid rgba(125, 227, 255, 0.4)",
		borderFocus: `0.1rem solid ${C["nori-teal"]}`,
		boxShadowFocus: "0 0 1.2rem rgba(94, 234, 212, 0.25)",
		borderRadius: "0.6rem",
		textColor: C["text-primary"],
		placeholderColor: "rgba(157, 178, 192, 0.75)",
	},
	Button: {
		borderRadiusMedium: "0.6rem",
		borderRadiusSmall: "0.5rem",
		textColorPrimary: "#03101c",
		colorHoverPrimary: C["nori-teal-bright"],
		colorPrimary: C["nori-teal"],
		colorPressedPrimary: "#2dd4bf",
		colorFocusPrimary: C["nori-teal"],
	},
	Modal: {
		boxShadow: "0 1.6rem 4rem rgba(0, 0, 0, 0.7), 0 0 2.4rem rgba(94, 234, 212, 0.12)",
		borderRadius: RADIUS.md,
	},
	Card: {
		borderRadius: "1rem",
		borderColor: C["line-subtle"],
	},
	Popconfirm: {
		borderRadius: RADIUS.sm,
	},
	Dialog: {
		borderRadius: "1rem",
		color: "rgba(8, 24, 40, 0.96)",
		titleTextColor: C["text-primary"],
		contentTextColor: C["text-body"],
		boxShadow: "0 1.6rem 4rem rgba(0, 0, 0, 0.75), 0 0 2.4rem rgba(94, 234, 212, 0.15)",
	},
	Tabs: {
		tabTextColorLine: C["text-muted"],
		tabTextColorActiveLine: C["nori-teal"],
		tabTextColorHoverLine: C["nori-teal-bright"],
		barColor: C["nori-teal"],
	},
	Tag: {
		borderRadius: RADIUS.pill,
	},
	Tooltip: {
		borderRadius: "0.6rem",
		color: "rgba(6, 20, 32, 0.95)",
		textColor: C["text-primary"],
		boxShadow: "0 0.4rem 1.6rem rgba(0, 0, 0, 0.5), 0 0 1rem rgba(94, 234, 212, 0.15)",
	},
	Message: {
		borderRadius: RADIUS.sm,
		boxShadowInfo: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(125, 227, 255, 0.25)",
		boxShadowSuccess: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(94, 234, 212, 0.25)",
		boxShadowError: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.6), 0 0 1.4rem rgba(255, 107, 114, 0.25)",
	},
}
