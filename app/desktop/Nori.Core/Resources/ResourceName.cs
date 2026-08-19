namespace Nori.Core.Resources;

/// <summary>
/// 资源名称校验
///
/// 对应 Rust 版 resource/mod.rs 的 validate_resource_name:
/// 资源名称只能表示一个目录名, 不能是路径
/// </summary>
public static class ResourceName
{
	/// <summary>
	/// 校验资源名称, 非法时抛 ResourceException
	/// </summary>
	public static string Validate(string name)
	{
		string trimmed = name.Trim();
		if (trimmed.Length == 0) throw new ResourceException("资源名称不能为空");
		if (trimmed is "." or "..") throw new ResourceException($"非法资源名称: {trimmed}");
		if (trimmed.Contains('/') || trimmed.Contains('\\')) throw new ResourceException($"资源名称不能包含路径分隔符: {trimmed}");
		if (trimmed.Any(char.IsControl)) throw new ResourceException("资源名称不能包含控制字符");
		// Windows 盘符, 例如 C:
		if (trimmed.Length >= 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':') throw new ResourceException($"非法资源名称: {trimmed}");
		return trimmed;
	}

	/// <summary>
	/// 校验资源名称, 只返回是否合法
	/// </summary>
	public static bool IsValid(string name)
	{
		try
		{
			Validate(name);
			return true;
		}
		catch (ResourceException)
		{
			return false;
		}
	}
}
