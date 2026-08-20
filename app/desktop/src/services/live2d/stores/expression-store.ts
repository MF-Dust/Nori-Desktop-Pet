/**
 * 表情存储
 *
 * 管理通过 exp3.json 注册的表情条目，支持切换、重置、查询
 * 参考 AIRI: packages/stage-ui-live2d/src/stores/expression-store.ts
 */
import {reactive, ref} from "vue"

export type ExpressionBlendMode = "Add" | "Multiply" | "Overwrite"

export interface ExpressionEntry {
	name: string
	parameterId: string
	blend: ExpressionBlendMode
	currentValue: number
	defaultValue: number
	modelDefault: number
	targetValue: number
}

export interface ExpressionGroupDefinition {
	name: string
	parameters: {
		parameterId: string
		blend: ExpressionBlendMode
		value: number
	}[]
}

/**
 * 全局表情存储单例
 */
export const expressionStore = {
	expressions: reactive(new Map<string, ExpressionEntry>()),
	expressionGroups: reactive(new Map<string, ExpressionGroupDefinition>()),
	modelId: ref(""),

	/**
	 * 注册模型的所有表情条目
	 */
	registerExpressions(
		id: string,
		groups: ExpressionGroupDefinition[],
		entries: ExpressionEntry[],
	) {
		this.expressions.clear()
		this.expressionGroups.clear()
		this.modelId.value = id

		for (const group of groups) {
			this.expressionGroups.set(group.name, group)
		}
		for (const entry of entries) {
			this.expressions.set(entry.name, {...entry})
		}
	},

	/**
	 * 解析名称：先查表情组，再查参数条目
	 */
	resolve(name: string): {kind: "group"; group: ExpressionGroupDefinition} | {kind: "param"; entry: ExpressionEntry} | null {
		const group = this.expressionGroups.get(name)
		if (group) return {kind: "group", group}
		const entry = this.expressions.get(name)
		if (entry) return {kind: "param", entry}
		return null
	},

	/**
	 * 设置表达式值
	 */
	set(name: string, value: number, duration?: number): boolean {
		const resolved = this.resolve(name)
		if (!resolved) return false

		if (resolved.kind === "group") {
			for (const param of resolved.group.parameters) {
				const entry = this.expressions.get(param.parameterId)
				if (entry) this.applyValue(entry, value, duration)
			}
			return true
		}

		this.applyValue(resolved.entry, value, duration)
		return true
	},

	/**
	 * 激活一个表情组或参数 (设置 exp3 值)
	 */
	play(name: string): boolean {
		const resolved = this.resolve(name)
		if (!resolved) return false
		if (resolved.kind === "group") {
			for (const param of resolved.group.parameters) {
				const entry = this.expressions.get(param.parameterId)
				if (entry) this.applyValue(entry, param.value)
			}
		} else {
			this.applyValue(resolved.entry, resolved.entry.targetValue)
		}
		return true
	},

	/**
	 * 停止所有表情 (重置到模型默认值)
	 */
	stop(): void {
		this.resetAll()
	},

	/**
	 * 切换表情（在默认值与目标值之间切换）
	 */
	toggle(name: string, duration?: number): boolean {
		const resolved = this.resolve(name)
		if (!resolved) return false

		if (resolved.kind === "group") {
			const isActive = resolved.group.parameters.some((p) => {
				if (p.value === 0) return false
				const entry = this.expressions.get(p.parameterId)
				return entry && entry.currentValue === p.value
			})
			for (const param of resolved.group.parameters) {
				const entry = this.expressions.get(param.parameterId)
				if (entry) {
					const newValue = isActive ? entry.modelDefault : param.value
					this.applyValue(entry, newValue, duration)
				}
			}
			return true
		}

		const entry = resolved.entry
		const newValue = entry.currentValue !== entry.modelDefault ? entry.modelDefault : entry.targetValue
		this.applyValue(entry, newValue, duration)
		return true
	},

	/**
	 * 重置所有表情到模型默认值
	 */
	resetAll() {
		for (const entry of this.expressions.values()) {
			entry.currentValue = entry.modelDefault
		}
	},

	/**
	 * 清理所有表情
	 */
	dispose() {
		this.expressions.clear()
		this.expressionGroups.clear()
		this.modelId.value = ""
	},

	/**
	 * 获取所有可用的表情名称
	 */
	allNames(): string[] {
		return Array.from(this.expressions.keys())
	},

	/**
	 * 获取所有表情组名称
	 */
	allGroupNames(): string[] {
		return Array.from(this.expressionGroups.keys())
	},

	// ---- 内部 ----
	applyValue(entry: ExpressionEntry, value: number, _duration?: number) {
		entry.currentValue = value
	},
}