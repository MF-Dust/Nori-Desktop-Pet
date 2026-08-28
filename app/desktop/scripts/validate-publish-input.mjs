const [version, revision, ...rids] = process.argv.slice(2);
const numeric = (version ?? "").replace(/^v/i, "").split(/[-+]/, 1)[0];
if (version !== "Dev" && !/^\d+\.\d+\.\d+$/.test(numeric)) throw new Error("版本必须是数字 major.minor.patch 或 Dev");
if (!/^\d+$/.test(revision ?? "")) throw new Error("部署 revision 必须是非负整数");
for (const rid of rids) if (!["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"].includes(rid)) throw new Error(`RID 无效: ${rid}`);
