import {readFileSync} from "node:fs"
import {join, resolve} from "node:path"
import {describe, expect, it} from "vitest"

const ROOT = resolve(__dirname, "../..")
const PROJECT = readFileSync(join(ROOT, "Nori.Desktop/Nori.Desktop.csproj"), "utf8").replace(/\r\n?/g, "\n")

describe("发布包体门禁", () => {
	it("保留 Playwright 编译依赖但基础 Publish 默认不携带 runtime", () => {
		expect(PROJECT).toContain('<PackageReference Include="Microsoft.Playwright" Version="1.56.0" />')
		expect(PROJECT).toContain('<NoriBundlePlaywrightRuntime Condition="\'$(NoriBundlePlaywrightRuntime)\' == \'\'">false</NoriBundlePlaywrightRuntime>')
	})

	it("Publish 结束后移除 Playwright Node 与 JS driver", () => {
		expect(PROJECT).toContain('Name="NoriTrimPlaywrightRuntimeFromPublish" AfterTargets="Publish"')
		expect(PROJECT).toContain('<RemoveDir Directories="$(NoriPublishRoot).playwright"')
		expect(PROJECT).toContain('<Delete Files="$(NoriPublishRoot)playwright.ps1"')
	})

	it("裁剪失败时阻止生成膨胀的基础包", () => {
		expect(PROJECT).toContain('Condition="Exists(\'$(NoriPublishRoot).playwright\')" Text="发布目录仍包含 .playwright；拒绝生成膨胀的基础分发包。"')
		expect(PROJECT).toContain('Condition="Exists(\'$(NoriPublishRoot)playwright.ps1\')" Text="发布目录仍包含 playwright.ps1；Playwright runtime 裁剪未完成。"')
	})
})
