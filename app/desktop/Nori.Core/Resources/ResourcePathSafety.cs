namespace Nori.Core.Resources;

/// <summary>
/// 资源文件系统路径安全辅助.
///
/// 所有资源 staging、ZIP 解压与模型引用都必须在进入文件系统前完成词法 containment
/// 检查, 并拒绝路径上的符号链接与其它 reparse point.
/// </summary>
internal static class ResourcePathSafety
{
	public static string FullPath(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string? root = Path.GetPathRoot(fullPath);
		if (root is not null && fullPath.Equals(root, Comparison)) return root;
		return Path.TrimEndingDirectorySeparator(fullPath);
	}

	public static bool IsSameOrWithin(string root, string path)
	{
		string canonicalRoot = FullPath(root);
		string canonicalPath = FullPath(path);
		if (canonicalRoot.Equals(canonicalPath, Comparison)) return true;
		string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
			? canonicalRoot
			: canonicalRoot + Path.DirectorySeparatorChar;
		return canonicalPath.StartsWith(prefix, Comparison);
	}

	public static void EnsureContained(string root, string path, string message)
	{
		if (!IsSameOrWithin(root, path)) throw new ResourceException(message);
	}

	/// <summary>
	/// 从文件系统根到 path 的每一段都检查, 不允许父目录链接把资源带出词法根.
	/// </summary>
	public static void EnsureNoReparsePointsAlongPath(string path, string message)
	{
		string canonicalPath = FullPath(path);
		string root = Path.GetPathRoot(canonicalPath) ?? canonicalPath;
		EnsureNoReparsePoints(root, canonicalPath, message);
	}

	/// <summary>
	/// 检查 root 到 path 的每一段, 不允许符号链接、junction 或其它 reparse point.
	/// 不存在的末尾路径留给调用方报告"不存在"; 已存在的链接即使目标不存在也会拒绝.
	/// </summary>
	public static void EnsureNoReparsePoints(string root, string path, string message)
	{
		string canonicalRoot = FullPath(root);
		string canonicalPath = FullPath(path);
		EnsureContained(canonicalRoot, canonicalPath, message);

		EnsureNoReparsePoint(canonicalRoot, message);
		string relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
		if (relative == ".") return;

		string current = canonicalRoot;
		foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment is "." or "..") throw new ResourceException(message);
			current = Path.Combine(current, segment);
			EnsureNoReparsePoint(current, message);
		}
	}

	private static void EnsureNoReparsePoint(string path, string message)
	{
		// macOS 将 /etc、/tmp、/var 暴露为指向 /private 下目录的系统别名。
		// 这些别名不是资源目录里的用户可控链接, 不能让它们阻断系统临时目录下的合法资源。
		if (IsMacOsSystemAlias(path)) return;

		FileAttributes attributes;
		try
		{
			attributes = File.GetAttributes(path);
		}
		catch (FileNotFoundException)
		{
			return;
		}
		catch (DirectoryNotFoundException)
		{
			return;
		}
		catch (UnauthorizedAccessException exception)
		{
			throw new ResourceException($"无法检查资源路径: {path}", exception);
		}
		catch (IOException exception)
		{
			if (!File.Exists(path) && !Directory.Exists(path)) return;
			throw new ResourceException($"无法检查资源路径: {path}", exception);
		}

		if ((attributes & FileAttributes.ReparsePoint) != 0)
		{
			throw new ResourceException($"资源路径包含符号链接或 reparse point: {path}");
		}

		try
		{
			// 目录段必须用 DirectoryInfo: File.ResolveLinkTarget 对目录会抛 IOException (NORI-1T/1S),
			// 链接判定语义与文件完全一致, 仅 API 按条目类型区分。
			FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
				? new DirectoryInfo(path)
				: new FileInfo(path);
			if (info.LinkTarget is not null || info.ResolveLinkTarget(returnFinalTarget: false) is not null)
			{
				throw new ResourceException($"资源路径包含符号链接或 reparse point: {path}");
			}
		}
		catch (FileNotFoundException)
		{
			// 末尾文件不存在, 由调用方给出更具体的错误.
		}
		catch (DirectoryNotFoundException)
		{
			// 末尾目录不存在, 由调用方给出更具体的错误.
		}
		catch (UnauthorizedAccessException exception)
		{
			throw new ResourceException($"无法检查资源路径: {path}", exception);
		}
		catch (IOException exception)
		{
			throw new ResourceException($"无法检查资源路径: {path}", exception);
		}
	}

	private static bool IsMacOsSystemAlias(string path)
	{
		if (!OperatingSystem.IsMacOS()) return false;
		return path is "/etc" or "/tmp" or "/var";
	}

	private static StringComparison Comparison => OperatingSystem.IsWindows()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;
}
