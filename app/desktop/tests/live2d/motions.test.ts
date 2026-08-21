import {describe, expect, it} from "vitest"
import {selectInteractionMotionGroups} from "../../src/services/live2d/motions"
import type {MotionGroup} from "../../src/services/live2d"

const group = (name: string, ...names: string[]): MotionGroup => ({group: name, names})

describe("Live2D 点击动作组选择", () => {
	it("TapBody 优先且保留模型原始组名", () => {
		const groups = selectInteractionMotionGroups([
			group("Idle", "sleep"),
			group("TAP-BODY", "tap"),
			group("Reactions", "nod"),
		])

		expect(groups.map((item) => item.group)).toEqual(["TAP-BODY", "Reactions"])
	})

	it("按点击、反应、动作和普通非待机组排序", () => {
		const groups = selectInteractionMotionGroups([
			group("Effects", "glitch"),
			group("Actions", "wave"),
			group("Reactions", "nod"),
			group("Touch", "pat"),
		])

		expect(groups.map((item) => item.group)).toEqual(["Touch", "Reactions", "Actions", "Effects"])
	})

	it("过滤空组并拒绝只有 Idle 或 Background 的模型", () => {
		expect(selectInteractionMotionGroups([
			group("Reactions"),
			group("Idle", "sleep"),
			group("Background", "back"),
		])).toEqual([])
	})

	it("没有语义组时保留普通非待机动作", () => {
		const groups = selectInteractionMotionGroups([
			group("Background", "back"),
			group("Dance", "dance"),
			group("Idle", "idle"),
		])

		expect(groups.map((item) => item.group)).toEqual(["Dance"])
	})
})
