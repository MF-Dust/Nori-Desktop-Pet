using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>
/// 插件运行时的文件系统边界检查。
///
/// 安全检查从宿主管理的插件/数据根开始，而不是从文件系统根开始。
/// 这样既拒绝插件根及其内部的符号链接、junction/reparse point，
/// 也允许宿主运行在 macOS /var 等系统级别名或 CI 工作目录链接之下。
/// </summary>
internal static class PluginPathSafety
{
	public static void EnsureNoReparsePoint(string path, string errorCode, string message)
	{
		string fullPath = FullPath(path);
		FileAttributes attributes;
		try
		{
			attributes = File.GetAttributes(fullPath);
		}
		catch (FileNotFoundException)
		{
			return;
		}
		catch (DirectoryNotFoundException)
		{
			return;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new PluginException(errorCode, message, exception);
		}

		if ((attributes & FileAttributes.ReparsePoint) != 0)
			throw new PluginException(errorCode, message);

		try
		{
			FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
				? new DirectoryInfo(fullPath)
				: new FileInfo(fullPath);
			if (info.LinkTarget is not null)
				throw new PluginException(errorCode, message);
		}
		catch (PluginException)
		{
			throw;
		}
		catch (FileNotFoundException)
		{
		}
		catch (DirectoryNotFoundException)
		{
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new PluginException(errorCode, message, exception);
		}
	}

	public static void EnsureNoReparsePoints(string trustedRoot, string path, string errorCode, string message)
	{
		string root = FullPath(trustedRoot);
		string target = FullPath(path);
		if (!IsSameOrWithin(root, target))
			throw new PluginException(errorCode, message);

		EnsureNoReparsePoint(root, errorCode, message);
		string relative = Path.GetRelativePath(root, target);
		if (relative == ".") return;

		string current = root;
		foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment is "." or "..") throw new PluginException(errorCode, message);
			current = Path.Combine(current, segment);
			EnsureNoReparsePoint(current, errorCode, message);
		}
	}

	public static IReadOnlyList<string> EnumerateDllFilesWithoutReparsePoints(string root, string errorCode, string message)
	{
		string canonicalRoot = FullPath(root);
		EnsureNoReparsePoint(canonicalRoot, errorCode, message);

		List<string> dlls = [];
		Stack<string> directories = new();
		directories.Push(canonicalRoot);
		while (directories.Count > 0)
		{
			string directory = directories.Pop();
			foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
			{
				EnsureNoReparsePoint(entry, errorCode, message);
				FileAttributes attributes;
				try { attributes = File.GetAttributes(entry); }
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
					throw new PluginException(errorCode, message, exception);
				}

				if ((attributes & FileAttributes.Directory) != 0)
				{
					directories.Push(entry);
				}
				else if (string.Equals(Path.GetExtension(entry), ".dll", StringComparison.OrdinalIgnoreCase))
				{
					dlls.Add(entry);
				}
			}
		}
		return dlls;
	}

	private static string FullPath(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string? root = Path.GetPathRoot(fullPath);
		return root is not null && fullPath.Equals(root, Comparison)
			? root
			: Path.TrimEndingDirectorySeparator(fullPath);
	}

	private static bool IsSameOrWithin(string root, string path)
	{
		if (path.Equals(root, Comparison)) return true;
		string prefix = root.EndsWith(Path.DirectorySeparatorChar)
			? root
			: root + Path.DirectorySeparatorChar;
		return path.StartsWith(prefix, Comparison);
	}

	private static StringComparison Comparison => OperatingSystem.IsWindows()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;
}
