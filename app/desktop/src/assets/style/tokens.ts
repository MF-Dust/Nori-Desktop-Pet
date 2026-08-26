/**
 * Nori 设计令牌 (单一色源)
 *
 * uno.config.ts、naiveTheme.ts 与 theme.less 的 :root 变量全部派生自这里。
 * 三处一致性由 tests/theme/tokens-sync.test.ts 看守, 任何一处漂移都会红。
 *
 * 配色纪律: 深海蓝 / 青绿的色相与品牌色数值不变, 只把不满足 4.5:1 对比度的
 * 次级文字 (--text-muted / --text-faint) 与 naive 的占位/禁用色提亮。
 */

/** 颜色令牌 (键即 CSS 变量名去掉 -- 前缀) */
export const COLORS = {
	// 品牌 (不变)
	"nori-teal": "#5eead4",
	"nori-teal-bright": "#7de3ff",
	"nori-teal-soft": "#7fd4e8",

	// 深色背景层级 (不变)
	"bg-base": "#05070a",
	"bg-abyss": "#050e1a",
	"bg-deep": "#081a2e",
	"bg-panel": "#0f2d47",

	// 玻璃拟态表面 (不变)
	"bg-card": "rgba(10, 28, 44, 0.55)",
	"bg-card-hover": "rgba(16, 46, 72, 0.72)",
	"bg-card-active": "rgba(22, 58, 90, 0.85)",
	"bg-glass": "rgba(8, 24, 38, 0.75)",
	"bg-glass-modal": "rgba(5, 16, 28, 0.92)",

	// 文字: primary/body 不变, muted/faint 提亮到 ≥4.5:1
	"text-primary": "#ecf8ff",
	"text-body": "#cfdde5",
	"text-muted": "#9db2c0",
	"text-faint": "#8398a8",

	// 边框 / 分隔 (不变)
	"line-subtle": "rgba(125, 227, 255, 0.12)",
	"line-strong": "rgba(125, 227, 255, 0.28)",
	"line-glow": "rgba(94, 234, 212, 0.45)",

	// 状态色: danger 只做填充/描边, 文字用 danger-text (纯 #fb3c44 当文字在浅面板上只有 3.9:1)
	"success": "#20e090",
	"warning": "#f1b24a",
	"danger": "#fb3c44",
	"danger-text": "#ff6b72",

	// 光晕 (不变)
	"glow-teal": "rgba(125, 227, 255, 0.45)",
	"glow-teal-soft": "rgba(125, 227, 255, 0.18)",
	"glow-teal-strong": "rgba(94, 234, 212, 0.65)",

	// 前景语义: 亮青绿填充上的深色文字 (对比度由 contrast.test.ts 看守)
	"on-teal": "#03101c",
	// 占位符与禁用态: 占位 ≥4.5:1, 禁用 ≥3:1
	"text-placeholder": "rgba(157, 178, 192, 0.75)",
	"text-disabled": "#6a8496",

	// 品牌交互态 (按下 / info 悬停按下)
	"nori-teal-pressed": "#2dd4bf",
	"info-hover": "#a5f3fc",
	"info-pressed": "#38bdf8",

	// 中性叠加层: 玻璃面上的浅色覆盖, 数字即不透明度百分比
	"overlay-2": "rgba(255, 255, 255, 0.02)",
	"overlay-4": "rgba(255, 255, 255, 0.04)",
	"overlay-6": "rgba(255, 255, 255, 0.06)",
	"overlay-8": "rgba(255, 255, 255, 0.08)",
	"overlay-12": "rgba(255, 255, 255, 0.12)",
	"overlay-20": "rgba(255, 255, 255, 0.2)",

	// 浮层背景 (下拉菜单 / 气泡 / 提示条)
	"bg-popover": "rgba(10, 28, 44, 0.96)",
	"bg-menu": "rgba(8, 24, 40, 0.96)",
	"bg-tooltip": "rgba(6, 20, 32, 0.95)",
} as const

/** 圆角令牌 */
export const RADIUS = {
	xs: "0.4rem",
	sm: "0.8rem",
	md: "1.2rem",
	lg: "1.6rem",
	pill: "99.9rem",
} as const

/**
 * 阴影令牌
 *
 * elev-1/2/3 是层级刻度: 页面 → 卡片 → 卡片内卡片 → 浮层, 每升一级换一档。
 * 之前只有 soft/glow/window 三档, 嵌套卡片没有可用的层次差, 观感扁平。
 */
export const SHADOWS = {
	soft: "0 0.8rem 2.4rem rgba(0, 0, 0, 0.35)",
	glow: "0 0 2rem rgba(125, 227, 255, 0.2)",
	window: "0 1.2rem 3.6rem rgba(0, 0, 0, 0.65)",
	"elev-1": "0 0.2rem 0.8rem rgba(0, 0, 0, 0.32), 0 0 0.1rem rgba(125, 227, 255, 0.08)",
	"elev-2": "0 0.8rem 2.4rem rgba(0, 0, 0, 0.48), 0 0 1.6rem rgba(125, 227, 255, 0.1)",
	"elev-3": "0 1.6rem 4rem rgba(0, 0, 0, 0.7), 0 0 2.4rem rgba(94, 234, 212, 0.14)",
} as const

/**
 * 字号刻度 (1rem = 10px)
 *
 * 最小档位 1.15rem: 旧代码里 1rem / 1.05rem 的说明文字在深色玻璃上几乎不可读。
 */
export const FONT_SIZES = {
	xs: ["1.15rem", "1.5"],
	sm: ["1.2rem", "1.55"],
	base: ["1.3rem", "1.6"],
	md: ["1.4rem", "1.55"],
	lg: ["1.6rem", "1.5"],
	xl: ["1.8rem", "1.45"],
	"2xl": ["2.2rem", "1.35"],
	"3xl": ["2.6rem", "1.3"],
} as const

/**
 * 间距刻度 (4px 网格 → rem)
 *
 * 规范要求 rem-only, 因此这里不出现任何 px。
 */
export const SPACING = {
	"0": "0",
	"0.5": "0.2rem",
	"1": "0.4rem",
	"1.5": "0.6rem",
	"2": "0.8rem",
	"2.5": "1rem",
	"3": "1.2rem",
	"3.5": "1.4rem",
	"4": "1.6rem",
	"5": "2rem",
	"6": "2.4rem",
	"7": "2.8rem",
	"8": "3.2rem",
	"9": "3.6rem",
	"10": "4rem",
	"12": "4.8rem",
	"14": "5.6rem",
	"16": "6.4rem",
	"20": "8rem",
} as const

/** 供运行时使用的 CSS 变量表 (theme.less 的 :root 必须与之逐条一致) */
export const CSS_VARIABLES: Record<string, string> = {
	...Object.fromEntries(Object.entries(COLORS).map(([key, value]) => [`--${key}`, value])),
	...Object.fromEntries(Object.entries(RADIUS).map(([key, value]) => [`--radius-${key}`, value])),
	...Object.fromEntries(Object.entries(SHADOWS).map(([key, value]) => [`--shadow-${key}`, value])),
}

/** 颜色令牌名 */
export type ColorToken = keyof typeof COLORS

/** 取 CSS 变量引用, 组件与 Uno 主题共用同一份间接层 */
export const cssVar = (token: ColorToken): string => `var(--${token})`
