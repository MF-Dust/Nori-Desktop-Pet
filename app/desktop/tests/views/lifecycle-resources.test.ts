import {readFileSync} from "node:fs"
import {join} from "node:path"
import {describe, expect, it} from "vitest"

const SRC = join(process.cwd(), "src")

const read = (path: string): string => readFileSync(join(SRC, path), "utf8")

describe("页面与宿主资源生命周期", () => {
	it("主工作区只缓存对话页，其他重资源页面切走后正常卸载", () => {
		const MAIN = read("views/Main.vue")
		const KEEP_ALIVE = MAIN.match(/<KeepAlive>([\s\S]*?)<\/KeepAlive>/)?.[1] ?? ""

		expect(KEEP_ALIVE).toContain("<ChatView")
		expect(KEEP_ALIVE).not.toContain("<HomePanel")
		expect(KEEP_ALIVE).not.toContain("<ModelManagement")
		expect(KEEP_ALIVE).not.toContain("<MemoryPanel")
		expect(KEEP_ALIVE).not.toContain("<SettingsPanel")
		expect(MAIN).toContain("<ModelManagement v-if=\"activeNav === 'model'\"")
		expect(MAIN).toContain("<SettingsPanel\n\t\t\t\t\t\tv-if=\"activeNav === 'settings'\"")
	})

	it("音频宿主卸载会关闭 WebAudio 并取消过期的麦克风启动", () => {
		const AUDIO = read("services/audio/index.ts")

		expect(AUDIO).toContain("CONTEXT.close()")
		expect(AUDIO).toContain("cancelRecording()")
		expect(AUDIO).toContain("releaseAudioGraph()")
		expect(AUDIO).toContain("GENERATION !== recordingGeneration")
		expect(AUDIO).toContain("stopStream(acquiredStream)")
	})

	it("Live2D 销毁会递归释放模型子资源但保留共享纹理", () => {
		const LIVE2D = read("services/live2d/index.ts")
		const DESTROY = "destroy(true, {children: true, texture: false, baseTexture: false})"

		expect(LIVE2D.split(DESTROY).length - 1).toBe(2)
		expect(LIVE2D).not.toContain("app.destroy(true)\n")
		expect(LIVE2D).not.toContain("inner.app.destroy(true)\n")
	})
})
