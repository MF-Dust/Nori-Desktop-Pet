export const VERSION_COMPONENT_MAX = 65535
export const INT32_MAX = 2147483647

export const numericVersionFromProduct = (productVersion) => {
	if (productVersion === "Dev") return "0.0.0"
	const numeric = (productVersion ?? "").replace(/^v/i, "").split(/[-+]/, 1)[0]
	validateNumericVersion(numeric)
	return numeric
}

export const validateNumericVersion = (value) => {
	if (!/^\d+\.\d+\.\d+$/.test(value ?? "")) throw new Error("版本必须是数字 major.minor.patch")
	for (const segment of value.split(".")) {
		const number = Number(segment)
		if (!Number.isSafeInteger(number) || number > VERSION_COMPONENT_MAX) throw new Error("版本各段必须在 .NET Version(0-65535)、Int32 和 JS 安全整数范围内")
	}
	return value
}

export const validateRevision = (value) => {
	if (!/^\d+$/.test(value ?? "")) throw new Error("部署 revision 必须是非负整数")
	const revision = Number(value)
	if (!Number.isSafeInteger(revision) || revision > INT32_MAX) throw new Error("部署 revision 必须在 .NET Int32 和 JS 安全整数范围内")
	return revision
}

export const validateProductVersion = (value) => {
	if (!value || value.length > 128 || [...value].some((char) => char.charCodeAt(0) < 0x20)) throw new Error("产品版本为空、过长或包含控制字符")
	if (value !== "Dev") numericVersionFromProduct(value)
	return value
}
