import { writeFileSync } from "node:fs";

const [output, productVersion, numericVersion, revision, rid, entrypoint] = process.argv.slice(2);
if (!output || !productVersion || productVersion.length > 128 || [...productVersion].some((char) => char.charCodeAt(0) < 0x20) || !/^\d+\.\d+\.\d+$/.test(numericVersion ?? "") || !/^\d+$/.test(revision ?? "") || !/^(win|linux|osx)-[A-Za-z0-9-]+$/.test(rid ?? "") || !entrypoint || entrypoint.length > 256 || [...entrypoint].some((char) => char.charCodeAt(0) < 0x20) || entrypoint.includes("\\") || entrypoint.split("/").some((part) => !part || part === "." || part === "..")) {
	console.error("deployment.json 参数无效");
	process.exit(2);
}
writeFileSync(output, JSON.stringify({
	schema_version: 1,
	product_version: productVersion,
	numeric_version: numericVersion,
	revision: Number.parseInt(revision, 10),
	rid,
	entrypoint,
}) + "\n", { encoding: "utf8", mode: 0o600 });
