import type {IconName} from "../icon"

/**
 * 技能类别
 */
export type SkillCategory = "productivity" | "coding" | "life" | "entertainment" | "roleplay"

/**
 * 技能来源
 */
export type SkillSource = "builtin" | "market" | "custom" | "url"

/**
 * 技能数据模型 (Nori Skill Definition)
 */
export interface Skill {
	/** 技能唯一 ID (如 "code-reviewer", "pomodoro-master") */
	id: string
	/** 技能显示名称 */
	name: string
	/** 技能简要描述 */
	description: string
	/** 作者名称 */
	author: string
	/** 语义化版本号 */
	version: string
	/** 显示图标 */
	icon: IconName
	/** 分类标签列表 */
	tags: string[]
	/** 所属主分类 */
	category: SkillCategory
	/** 注入 Agent System Prompt 的行为指引 / 技能指令 */
	instructions: string
	/** 该技能依赖或推荐启用的工具名称列表 */
	tools?: string[]
	/** 是否已启用 */
	enabled: boolean
	/** 技能来源 */
	source: SkillSource
	/** 安装时间戳 */
	installedAt: number
	/** 远程来源 URL (若从网络安装) */
	url?: string
}

/**
 * 网络技能清单格式 (JSON 导入或从网络安装)
 */
export interface SkillManifest {
	id: string
	name: string
	description: string
	author?: string
	version?: string
	icon?: string
	tags?: string[]
	category?: SkillCategory
	instructions: string
	tools?: string[]
}
