import assert from "node:assert/strict"
import {chmodSync, mkdtempSync, mkdirSync, writeFileSync} from "node:fs"
import {tmpdir} from "node:os"
import {join} from "node:path"
import {spawnSync} from "node:child_process"

const script = join(import.meta.dirname, "validate-publish-structure.mjs")
const createFixture = (rid) => {
	const root = mkdtempSync(join(tmpdir(), "nori-publish-fixture-"))
	const slot = join(root, "app-1.2.3-4")
	mkdirSync(slot, {recursive: true})
	const mac = rid.startsWith("osx-")
	const rootBase = mac ? join(root, "Nori.app", "Contents", "MacOS", "Nori") : join(root, "Nori")
	const rootExecutable = mac ? rootBase : rid.startsWith("win-") ? `${rootBase}.exe` : rootBase
	const slotBase = mac ? join(slot, "Nori.Desktop.app", "Contents", "MacOS", "Nori.Desktop") : join(slot, "Nori.Desktop")
	const slotExecutable = mac ? slotBase : rid.startsWith("win-") ? `${slotBase}.exe` : slotBase
	const native = rid.startsWith("win-") ? "Live2DCubismCore.dll" : rid.startsWith("osx-") ? "libLive2DCubismCore.dylib" : "libLive2DCubismCore.so"
	const files = [rootExecutable, `${rootBase}.dll`, `${rootBase}.deps.json`, `${rootBase}.runtimeconfig.json`, join(root, ".current"), join(slot, "deployment.json"), slotExecutable, `${slotBase}.dll`, `${slotBase}.deps.json`, `${slotBase}.runtimeconfig.json`, mac ? join(slot, "Nori.Desktop.app", "Contents", "MacOS", native) : join(slot, native)]
	for (const file of files) {
		mkdirSync(join(file, ".."), {recursive: true})
		writeFileSync(file, "fixture")
		if (process.platform !== "win32") chmodSync(file, 0o755)
	}
	writeFileSync(join(root, ".current"), "app-1.2.3-4\n")
	writeFileSync(join(slot, "deployment.json"), JSON.stringify({schema_version: 1, product_version: "v1.2.3-test", numeric_version: "1.2.3", revision: 4, rid, entrypoint: mac ? "Nori.Desktop.app/Contents/MacOS/Nori.Desktop" : rid.startsWith("win-") ? "Nori.Desktop.exe" : "Nori.Desktop"}))
	return root
}

for (const rid of ["win-x64", "linux-x64", "osx-arm64"]) {
	const root = createFixture(rid)
	const result = spawnSync(process.execPath, [script, root, rid], {encoding: "utf8"})
	assert.equal(result.status, 0, `${rid}: ${result.stderr}`)
}
const root = createFixture("linux-x64")
writeFileSync(join(root, "libhostfxr.so"), "forbidden")
const rejected = spawnSync(process.execPath, [script, root, "linux-x64"], {encoding: "utf8"})
assert.notEqual(rejected.status, 0)
console.log("发布结构 fixture 测试通过")
