import {darkTheme} from "naive-ui"

/**
 * Nori Desktop Pet - Naive UI 主题入口
 *
 * 覆盖表在 naiveOverrides.ts (纯令牌派生, 不引入 naive 运行时, 便于 node 环境测试)。
 */
export {naiveThemeOverrides} from "./naiveOverrides"

export const naiveDarkTheme = darkTheme
