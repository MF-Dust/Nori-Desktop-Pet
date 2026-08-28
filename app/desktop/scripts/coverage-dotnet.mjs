import {mkdirSync, rmSync} from "node:fs"
import {resolve} from "node:path"
import {spawnSync} from "node:child_process"

const COVERAGE_ROOT = resolve("artifacts/dotnet-coverage")
const SETTINGS_FILE = resolve("scripts/dotnet-coverage.runsettings")
const PROJECTS = [
	{
		name: "Nori.Core",
		path: "Nori.Core.Tests/Nori.Core.Tests.csproj",
	},
	{
		name: "Nori.Desktop",
		path: "Nori.Desktop.Tests/Nori.Desktop.Tests.csproj",
	},
	{
		name: "Nori.PluginRuntime",
		path: "Nori.PluginRuntime.Tests/Nori.PluginRuntime.Tests.csproj",
	},
]

// 每次从干净目录开始，保证本地与 CI 都能得到可定位的 Cobertura 产物。
rmSync(COVERAGE_ROOT, {recursive: true, force: true})
mkdirSync(COVERAGE_ROOT, {recursive: true})

for (const PROJECT of PROJECTS) {
	const RESULT_DIR = resolve(COVERAGE_ROOT, PROJECT.name)
	const RESULT = spawnSync("dotnet", [
		"test",
		PROJECT.path,
		"--configuration",
		"Release",
		"--collect",
		"XPlat Code Coverage",
		"--settings",
		SETTINGS_FILE,
		"--results-directory",
		RESULT_DIR,
		"-m:1",
	], {stdio: "inherit"})

	if (RESULT.error) {
		console.error(`.NET 覆盖率命令执行失败 (${PROJECT.name}): ${RESULT.error.message}`)
		process.exit(1)
	}
	if (RESULT.status !== 0) {
		console.error(`.NET 覆盖率测试失败 (${PROJECT.name})`)
		process.exit(RESULT.status ?? 1)
	}
}

console.log(`.NET 覆盖率产物已写入 ${COVERAGE_ROOT}`)
