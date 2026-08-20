import {invoke} from "../host/invoke"
import type {Skill, SkillManifest} from "./types"
import {SKILL_PRESETS} from "./presets"

export * from "./types"
export * from "./presets"

const CONFIG_KEY = "nori_skills"

/**
 * 技能管理器服务 (支持本地技能、市场安装、URL 网络安装与 Prompt 动态注入)
 */
export class SkillService {
	private skills: Map<string, Skill> = new Map()
	private initialized = false

	/**
	 * 初始化加载技能列表
	 */
	public async init(): Promise<void> {
		if (this.initialized) return

		try {
			const SAVED = await invoke<string | null>("get_config", {key: CONFIG_KEY})
			if (SAVED) {
				const LIST = JSON.parse(SAVED) as Skill[]
				if (Array.isArray(LIST) && LIST.length > 0) {
					for (const skill of LIST) {
						this.skills.set(skill.id, skill)
					}
					this.initialized = true
					return
				}
			}
		} catch (error) {
			console.error("读取技能配置失败，使用默认预设:", error)
		}

		// 默认预装内置技能
		for (const preset of SKILL_PRESETS) {
			if (preset.source === "builtin") {
				this.skills.set(preset.id, {...preset})
			}
		}
		this.initialized = true
		await this.save()
	}

	/**
	 * 获取所有已安装技能列表
	 */
	public async getInstalledSkills(): Promise<Skill[]> {
		await this.init()
		return Array.from(this.skills.values())
	}

	/**
	 * 获取所有当前激活启用的技能列表
	 */
	public async getEnabledSkills(): Promise<Skill[]> {
		await this.init()
		return Array.from(this.skills.values()).filter(s => s.enabled)
	}

	/**
	 * 获取技能市场目录 (预设官方与社区精选技能)
	 */
	public getMarketplaceSkills(): Skill[] {
		return SKILL_PRESETS
	}

	/**
	 * 切换技能启用状态
	 */
	public async toggleSkill(id: string, enabled: boolean): Promise<void> {
		await this.init()
		const SKILL = this.skills.get(id)
		if (SKILL) {
			SKILL.enabled = enabled
			await this.save()
		}
	}

	/**
	 * 从市场安装技能
	 */
	public async installFromMarketplace(skillId: string): Promise<Skill> {
		await this.init()
		const TARGET = SKILL_PRESETS.find(s => s.id === skillId)
		if (!TARGET) {
			throw new Error(`未在市场中找到技能 ID: ${skillId}`)
		}

		const INSTALLED_SKILL: Skill = {
			...TARGET,
			enabled: true,
			installedAt: Date.now(),
		}
		this.skills.set(INSTALLED_SKILL.id, INSTALLED_SKILL)
		await this.save()
		return INSTALLED_SKILL
	}

	/**
	 * 从远程网络 URL 安装技能 (支持 JSON 规范与 SKILL.md Markdown 规范)
	 */
	public async installFromUrl(url: string): Promise<Skill> {
		await this.init()

		let text = ""
		try {
			text = await invoke<string>("fetch_remote_text", {url})
		} catch {
			const RES = await fetch(url)
			if (!RES.ok) throw new Error(`HTTP ${RES.status}: ${RES.statusText}`)
			text = await RES.text()
		}

		if (!text.trim()) {
			throw new Error("远程技能文件内容为空")
		}

		let skill: Skill

		// 1. 判断是否为 SKILL.md 格式 (含 YAML frontmatter)
		if (text.startsWith("---")) {
			skill = this.parseSkillMarkdown(text, url)
		} else {
			// 2. 解析 JSON 格式
			try {
				const MANIFEST = JSON.parse(text) as SkillManifest
				skill = {
					id: MANIFEST.id || `skill_${Date.now()}`,
					name: MANIFEST.name || "未命名网络技能",
					description: MANIFEST.description || "从网络导入的技能",
					author: MANIFEST.author || "Online Author",
					version: MANIFEST.version || "1.0.0",
					icon: (MANIFEST.icon as any) || "sparkles",
					tags: MANIFEST.tags || ["网络安装"],
					category: MANIFEST.category || "productivity",
					instructions: MANIFEST.instructions || "",
					tools: MANIFEST.tools || [],
					enabled: true,
					source: "url",
					installedAt: Date.now(),
					url,
				}
			} catch (jsonErr) {
				throw new Error(`解析远程技能格式失败: ${jsonErr instanceof Error ? jsonErr.message : String(jsonErr)}`)
			}
		}

		this.skills.set(skill.id, skill)
		await this.save()
		return skill
	}

	/**
	 * 创建或保存自定义技能
	 */
	public async saveCustomSkill(skill: Omit<Skill, "installedAt">): Promise<Skill> {
		await this.init()
		const COMPLETE_SKILL: Skill = {
			...skill,
			installedAt: this.skills.get(skill.id)?.installedAt || Date.now(),
		}
		this.skills.set(COMPLETE_SKILL.id, COMPLETE_SKILL)
		await this.save()
		return COMPLETE_SKILL
	}

	/**
	 * 卸载删除技能
	 */
	public async uninstallSkill(id: string): Promise<boolean> {
		await this.init()
		const DELETED = this.skills.delete(id)
		if (DELETED) {
			await this.save()
		}
		return DELETED
	}

	/**
	 * 导出技能为 JSON 字符串
	 */
	public exportSkill(id: string): string {
		const SKILL = this.skills.get(id)
		if (!SKILL) throw new Error("技能不存在")
		return JSON.stringify(SKILL, null, 2)
	}

	/**
	 * 导入 JSON 技能
	 */
	public async importSkillFromJson(jsonStr: string): Promise<Skill> {
		await this.init()
		const DATA = JSON.parse(jsonStr) as Skill
		if (!DATA.name || !DATA.instructions) {
			throw new Error("技能 JSON 缺少必要的 name 或 instructions 字段")
		}
		const SKILL: Skill = {
			...DATA,
			id: DATA.id || `custom_${Date.now()}`,
			source: "custom",
			installedAt: Date.now(),
			enabled: true,
		}
		this.skills.set(SKILL.id, SKILL)
		await this.save()
		return SKILL
	}

	/**
	 * 构建注入系统提示词的技能指令集
	 */
	public buildSkillsPrompt(): string {
		const ACTIVE_SKILLS = Array.from(this.skills.values()).filter(s => s.enabled)
		if (ACTIVE_SKILLS.length === 0) return ""

		const LINES: string[] = ["【已激活技能与扩展指令 (Active Skills)】："]
		for (let i = 0; i < ACTIVE_SKILLS.length; i++) {
			const S = ACTIVE_SKILLS[i]
			LINES.push(`\n=== 技能 ${i + 1}：${S.name} (v${S.version}) ===`)
			if (S.description) LINES.push(`简介: ${S.description}`)
			LINES.push(S.instructions)
		}

		return LINES.join("\n")
	}

	/**
	 * 解析 SKILL.md (YAML Frontmatter + Markdown Instructions)
	 */
	private parseSkillMarkdown(content: string, url?: string): Skill {
		const PARTS = content.split("---")
		if (PARTS.length < 3) {
			throw new Error("SKILL.md 格式错误：缺少完整的 YAML frontmatter 头部分隔符 ---")
		}

		const FRONTMATTER = PARTS[1]
		const BODY = PARTS.slice(2).join("---").trim()

		const META: Record<string, string> = {}
		const LINES = FRONTMATTER.split(/\r?\n/)
		for (const line of LINES) {
			const COLON = line.indexOf(":")
			if (COLON > 0) {
				const K = line.substring(0, COLON).trim()
				const V = line.substring(COLON + 1).trim().replace(/^["']|["']$/g, "")
				if (K) META[K] = V
			}
		}

		const ID = META.name ? META.name.toLowerCase().replace(/[^a-z0-9_-]+/g, "-") : `skill_${Date.now()}`

		return {
			id: ID,
			name: META.name || "未命名技能",
			description: META.description || "从 SKILL.md 安装的技能",
			author: META.author || "Online Creator",
			version: META.version || "1.0.0",
			icon: "sparkles",
			tags: META.tags ? META.tags.split(",").map(t => t.trim()) : ["Skill"],
			category: "productivity",
			instructions: BODY,
			enabled: true,
			source: "url",
			installedAt: Date.now(),
			url,
		}
	}

	/**
	 * 持久化已安装技能列表
	 */
	private async save(): Promise<void> {
		const LIST = Array.from(this.skills.values())
		const JSON_STR = JSON.stringify(LIST)
		try {
			await invoke("set_config", {key: CONFIG_KEY, value: JSON_STR})
		} catch (error) {
			console.error("保存技能配置失败:", error)
		}
	}
}

/**
 * 全局技能服务单例
 */
export const skillService = new SkillService()
