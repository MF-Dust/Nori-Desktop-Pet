import {defineConfig, presetWind3, transformerDirectives, transformerVariantGroup} from "unocss"
import {COLORS, FONT_SIZES, RADIUS, SHADOWS, SPACING} from "./src/assets/style/tokens"

/**
 * UnoCSS 配置 (Nori 深海蓝设计体系)
 *
 * 纪律:
 * - 不引入任何 reset: 基础重置留在 theme.less, 避免与 naive-ui 的运行时样式打架。
 * - 颜色一律走 CSS 变量间接层, 与 theme.less :root 同源 (src/assets/style/tokens.ts)。
 * - 刻度只有 rem: 间距按 4px 网格 (1 = 0.4rem), 字号最小 xs = 1.15rem。
 * - naive-ui 组件的外观只经 naiveThemeOverrides 调整, 不用原子类覆盖其内部 DOM。
 */
export default defineConfig({
	presets: [presetWind3({preflight: false})],
	transformers: [transformerVariantGroup(), transformerDirectives()],
	theme: {
		colors: {
			...COLORS,
			// 语义别名: 深色底上的按钮前景 (青绿按钮上的深色文字)
			"on-teal": "#03101c",
		},
		spacing: SPACING,
		fontSize: FONT_SIZES,
		// 边框/描边宽度也走 rem, 避开 Uno 默认的 1px
		lineWidth: {
			DEFAULT: "0.1rem",
			"0": "0",
			"1": "0.1rem",
			"2": "0.2rem",
			"3": "0.3rem",
			"4": "0.4rem",
		},
		borderRadius: {
			none: "0",
			xs: RADIUS.xs,
			sm: RADIUS.sm,
			DEFAULT: RADIUS.sm,
			md: RADIUS.md,
			lg: RADIUS.lg,
			pill: RADIUS.pill,
			full: "50%",
		},
		boxShadow: {
			soft: SHADOWS.soft,
			glow: SHADOWS.glow,
			window: SHADOWS.window,
		},
		animation: {
			keyframes: {
				breathe: "{0%,100%{transform:scale(1);filter:drop-shadow(0 0 1.4rem rgba(94,234,212,0.35))}"
					+ "50%{transform:scale(1.05);filter:drop-shadow(0 0 2.4rem rgba(125,227,255,0.65))}}",
				"glow-pulse": "{0%,100%{opacity:0.3;transform:scale(1)}50%{opacity:0.65;transform:scale(1.08)}}",
			},
			durations: {
				breathe: "2.4s",
				"glow-pulse": "2.5s",
			},
			counts: {
				breathe: "infinite",
				"glow-pulse": "infinite",
			},
			timingFns: {
				breathe: "ease-in-out",
				"glow-pulse": "ease-in-out",
			},
		},
	},
	shortcuts: [
		// ---- 窗口与容器 ----
		["window-root", "w-100vw h-100vh rounded-lg overflow-hidden select-none text-text-body shadow-window relative"],
		["window-surface", "bg-gradient-to-br from-bg-panel via-bg-deep to-bg-abyss"],
		["glass-panel", "bg-bg-card border border-line-subtle rounded-md backdrop-blur-[1.2rem] transition-all duration-200 hover:(border-line-strong)"],
		["surface-card", "bg-bg-card border border-line-subtle rounded-md transition-all duration-200 hover:(border-line-strong bg-bg-card-hover)"],
		["glow-card", "bg-bg-card/80 border border-nori-teal-bright/25 rounded-md shadow-[0_0.4rem_2rem_rgba(0,0,0,0.4),0_0_1.6rem_var(--glow-teal-soft)] backdrop-blur-[1.4rem]"],
		["hud-panel", "bg-bg-abyss/85 border border-line-subtle rounded-md backdrop-blur-[1.6rem] shadow-[0_0.8rem_3.2rem_rgba(0,0,0,0.6)]"],
		["scroll-area", "min-h-0 overflow-y-auto overflow-x-hidden"],

		// ---- 文字 ----
		["glow-teal", "text-text-primary [text-shadow:0_0_1.2rem_var(--glow-teal-soft)]"],
		["glow-bright", "text-nori-teal-bright [text-shadow:0_0_1.4rem_var(--glow-teal)]"],
		["title-lg", "text-xl font-700 text-text-primary tracking-[-0.015em]"],
		["title-md", "text-lg font-600 text-text-primary tracking-[-0.01em]"],
		["title-sm", "text-md font-600 text-text-primary"],
		["text-sub", "text-sm text-text-muted"],
		["text-hint", "text-xs text-text-faint"],
		["mono", "font-mono tabular-nums"],

		// ---- 焦点可见 (键盘可达性: 所有可交互元素统一焦点环) ----
		[
			"focus-ring",
			"outline-none focus-visible:(outline outline-2 outline-offset-[0.2rem] outline-nori-teal-bright)",
		],

		// ---- 按钮 ----
		[
			"btn-base",
			"inline-flex items-center justify-center gap-2 font-inherit cursor-pointer border-none "
			+ "transition-all duration-150 focus-ring select-none disabled:(opacity-50 cursor-not-allowed pointer-events-none)",
		],
		[
			"btn-primary",
			"btn-base px-4 py-1.8 rounded-sm text-sm font-600 text-on-teal "
			+ "bg-gradient-to-r from-nori-teal-bright via-nori-teal to-nori-teal-soft shadow-[0_0.2rem_1.4rem_rgba(94,234,212,0.3)] "
			+ "hover:not-disabled:(brightness-110 -translate-y-[0.1rem] shadow-[0_0.4rem_2rem_rgba(125,227,255,0.45)]) "
			+ "active:not-disabled:(translate-y-0 scale-98)",
		],
		[
			"btn-ghost",
			"btn-base px-3.5 py-1.8 rounded-sm text-sm font-500 text-text-body bg-white/4 border border-line-subtle "
			+ "hover:not-disabled:(text-text-primary bg-white/8 border-nori-teal-soft/60 -translate-y-[0.1rem] shadow-[0_0.2rem_1rem_rgba(0,0,0,0.2)]) "
			+ "active:not-disabled:(translate-y-0 scale-98)",
		],
		[
			"btn-danger",
			"btn-base px-3.5 py-1.8 rounded-sm text-sm font-500 text-danger-text bg-danger/10 border border-danger/35 "
			+ "hover:not-disabled:(bg-danger/20 border-danger/60 -translate-y-[0.1rem]) active:not-disabled:(translate-y-0 scale-98)",
		],
		[
			"btn-icon",
			"btn-base w-7 h-7 rounded-full bg-transparent text-text-muted transition-all duration-150 "
			+ "hover:(bg-white/8 text-nori-teal-bright scale-108) active:scale-92",
		],
		[
			"btn-close",
			"btn-base w-7 h-7 rounded-full bg-transparent text-text-muted transition-all duration-150 "
			+ "hover:(bg-danger/20 text-danger-text scale-108) active:scale-92",
		],
		// 兼容旧名: 三个窗口标题栏的关闭按钮与图标尺寸
		["close-btn", "btn-close"],
		["close-icon", "w-3.5 h-3.5"],

		// ---- 徽标 / 药丸 ----
		[
			"chip",
			"inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-pill text-xs "
			+ "bg-white/5 border border-line-subtle text-text-muted transition-all duration-150",
		],
		["chip-teal", "chip bg-nori-teal-bright/10 border-nori-teal-bright/30 text-nori-teal-bright shadow-[0_0_1rem_rgba(125,227,255,0.12)]"],
		["chip-success", "chip bg-success/10 border-success/35 text-success shadow-[0_0_1rem_rgba(32,224,144,0.12)]"],
		["chip-warning", "chip bg-warning/10 border-warning/35 text-warning shadow-[0_0_1rem_rgba(241,178,74,0.12)]"],
		["chip-danger", "chip bg-danger/10 border-danger/35 text-danger-text"],

		// ---- 表单 ----
		["field", "flex flex-col gap-1.5"],
		["field-label", "text-sm font-500 text-text-muted"],
		[
			"input-base",
			"w-full px-3.5 py-2 rounded-sm text-sm font-inherit text-text-primary bg-white/4 "
			+ "border border-line-subtle outline-none transition-all duration-150 "
			+ "placeholder:text-text-faint hover:border-nori-teal-soft/50 "
			+ "focus:(border-nori-teal-bright bg-nori-teal-bright/6 shadow-[0_0_1.4rem_rgba(125,227,255,0.18)])",
		],

		// ---- 列表项 / 导航 ----
		[
			"nav-item",
			"relative flex items-center gap-2.5 px-3 py-2 rounded-sm border border-transparent bg-transparent "
			+ "text-sm text-text-muted font-inherit cursor-pointer overflow-hidden focus-ring "
			+ "transition-all duration-150 hover:(bg-white/5 text-text-primary)",
		],
		[
			"nav-item-active",
			"bg-nori-teal-bright/12 border-nori-teal-bright/30 text-nori-teal-bright font-600 shadow-[0_0_1.2rem_rgba(125,227,255,0.1)]",
		],
	],
})
