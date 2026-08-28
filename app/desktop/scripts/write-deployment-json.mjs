import { writeFileSync } from "node:fs";
import { validateNumericVersion, validateProductVersion, validateRevision } from "./version-validation.mjs";

const [output, productVersion, numericVersion, revision, rid, entrypoint] = process.argv.slice(2);
try {
	validateProductVersion(productVersion);
	validateNumericVersion(numericVersion);
	validateRevision(revision);
} catch (error) {
	console.error(`deployment.json 参数无效: ${error.message}`);
	process.exit(2);
}
if (!output || !/^(win|linux|osx)-[A-Za-z0-9-]+$/.test(rid ?? "") || !entrypoint || entrypoint.length > 256 || [...entrypoint].some((char) => char.charCodeAt(0) < 0x20) || entrypoint.includes("\\") || entrypoint.split("/").some((part) => !part || part === "." || part === "..")) {
	console.error("deployment.json 参数无效");
	process.exit(2);
}
const revisionNumber = Number(revision);
writeFileSync(output, JSON.stringify({
	schema_version: 1,
	product_version: productVersion,
	numeric_version: numericVersion,
	revision: revisionNumber,
	rid,
	entrypoint,
}) + "\n", { encoding: "utf8", mode: 0o600 });
