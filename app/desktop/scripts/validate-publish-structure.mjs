import { existsSync, lstatSync, readFileSync, readdirSync } from "node:fs";
import { join, relative } from "node:path";

const [rootArg, rid] = process.argv.slice(2);
if (!rootArg || !rid) throw new Error("用法: validate-publish-structure.mjs <package-root> <rid>");
const root = rootArg;
const fail = (message) => { throw new Error(message); };
const file = (path, executable = false) => {
	if (!existsSync(path) || !lstatSync(path).isFile()) fail(`发布目录缺少文件: ${relative(root, path)}`);
	if (executable && process.platform !== "win32" && (lstatSync(path).mode & 0o111) === 0) fail(`发布入口不可执行: ${relative(root, path)}`);
};
const directory = (path) => {
	if (!existsSync(path) || !lstatSync(path).isDirectory()) fail(`发布目录缺少目录: ${relative(root, path)}`);
};
const scan = (path) => {
	const stat = lstatSync(path);
	if (stat.isSymbolicLink()) fail(`发布目录不得包含符号链接: ${relative(root, path)}`);
	if (!stat.isDirectory()) return;
	for (const name of readdirSync(path)) scan(join(path, name));
};
if (!["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"].includes(rid)) fail(`RID 无效: ${rid}`);
directory(root);
scan(root);
const rootLauncher = rid.startsWith("win-") ? join(root, "Nori.exe") : rid.startsWith("osx-") ? join(root, "Nori.app", "Contents", "MacOS", "Nori") : join(root, "Nori");
file(rootLauncher, true);
const rootSidecarBase = rid.startsWith("osx-") ? join(root, "Nori.app", "Contents", "MacOS", "Nori") : join(root, "Nori");
file(`${rootSidecarBase}.dll`);
file(`${rootSidecarBase}.deps.json`);
file(`${rootSidecarBase}.runtimeconfig.json`);
file(join(root, ".current"));
const slotName = readFileSync(join(root, ".current"), "utf8").trim();
if (!/^app-\d+\.\d+\.\d+-\d+$/.test(slotName)) fail(`.current 无效: ${slotName}`);
const slot = join(root, slotName);
directory(slot);
file(join(slot, "deployment.json"));
let entry;
if (rid.startsWith("osx-")) {
	const app = join(slot, "Nori.Desktop.app", "Contents", "MacOS");
	directory(app);
	entry = join(app, "Nori.Desktop");
} else entry = join(slot, rid.startsWith("win-") ? "Nori.Desktop.exe" : "Nori.Desktop");
file(entry, true);
const sidecarBase = rid.startsWith("osx-") ? join(slot, "Nori.Desktop.app", "Contents", "MacOS", "Nori.Desktop") : join(slot, rid.startsWith("win-") ? "Nori.Desktop" : "Nori.Desktop");
file(`${sidecarBase}.dll`);
file(`${sidecarBase}.deps.json`);
file(`${sidecarBase}.runtimeconfig.json`);
const native = rid.startsWith("win-") ? "Live2DCubismCore.dll" : rid.startsWith("osx-") ? "libLive2DCubismCore.dylib" : "libLive2DCubismCore.so";
file(rid.startsWith("osx-")
	? join(slot, "Nori.Desktop.app", "Contents", "MacOS", native)
	: join(slot, native));
const forbidden = new Set(["data", "dotnet", "shared", "coreclr", "hostfxr", "hostpolicy"]);
const lower = (value) => value.toLowerCase();
const walkForbidden = (path) => {
	for (const name of readdirSync(path)) {
		const full = join(path, name);
		const normalized = lower(name.replace(/\.[^.]+$/, ""));
		const withoutLib = normalized.replace(/^lib/, "");
		if (forbidden.has(normalized) || forbidden.has(withoutLib) || lower(name).endsWith(".map")) fail(`FDD 发布目录包含禁止项: ${relative(root, full)}`);
		if (lstatSync(full).isDirectory()) walkForbidden(full);
	}
};
walkForbidden(root);
console.log(`发布结构有效: ${rid} ${slotName}`);
