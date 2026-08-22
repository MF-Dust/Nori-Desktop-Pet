/**
 * 根据脱敏设置判断初始化完成后是否自动显示桌宠。
 * 缺省值保持既有行为: 自动唤出。
 */
export const ShouldAutoSummonPet = (value: boolean | null | undefined): boolean => value ?? true
