import { numericVersionFromProduct, validateProductVersion, validateRevision } from "./version-validation.mjs";

const [version, revision, ...rids] = process.argv.slice(2);
validateProductVersion(version);
numericVersionFromProduct(version);
validateRevision(revision);
for (const rid of rids) if (!["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"].includes(rid)) throw new Error(`RID 无效: ${rid}`);
