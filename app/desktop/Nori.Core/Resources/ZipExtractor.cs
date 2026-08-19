using System.IO.Compression;

namespace Nori.Core.Resources;

/// <summary>
/// ZIP 安全解压
///
/// 对应 Rust 版 resource/downloader.rs 的 sanitize_zip_path / extract_zip.
/// 这是把网络下载的压缩包落到磁盘的唯一入口, 每一条拒绝规则都不要放宽.
/// </summary>
public static class ZipExtractor
{
	/// <summary>
	/// 清理并校验 ZIP 内部路径
	///
	/// ZIP 标准通常用 `/`, 但 Windows 打的包也可能出现 `\`.
	/// 返回归一化后的相对路径; 目录项或空路径返回空串; 非法路径抛 ResourceException.
	/// </summary>
	public static string SanitizePath(string raw)
	{
		if (raw.Length == 0) return string.Empty;
		string normalized = raw.Replace('\\', '/');
		// Windows UNC 路径 (要先于 Unix 绝对路径判断, 否则会被当成普通绝对路径)
		if (normalized.StartsWith("//", StringComparison.Ordinal)) throw new ResourceException($"ZIP 包包含 UNC 路径: {raw}");
		// Unix 绝对路径
		if (normalized.StartsWith('/')) throw new ResourceException($"ZIP 包包含绝对路径: {raw}");
		// Windows 盘符
		if (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
		{
			throw new ResourceException($"ZIP 包包含 Windows 绝对路径: {raw}");
		}
		List<string> parts = [];
		foreach (string part in normalized.Split('/'))
		{
			if (part.Length == 0 || part == ".") continue;
			if (part == "..") throw new ResourceException($"ZIP 条目包含路径穿越: {raw}");
			if (part.Any(char.IsControl)) throw new ResourceException($"ZIP 条目包含非法字符: {raw}");
			parts.Add(part);
		}
		return string.Join('/', parts);
	}

	/// <summary>
	/// 找出所有条目共有的唯一顶层目录
	///
	/// 资源包常见地多包一层同名目录 (arg-nori/arg-nori/...). asset.rs 的候选路径只会删段
	/// 不会加段, 救不了这种包, 所以必须在解压阶段就把这一层剥掉.
	/// 没有共同顶层目录 (或顶层就是文件) 时返回 null.
	/// </summary>
	public static string? FindCommonTopDirectory(IEnumerable<string> entryPaths)
	{
		string? top = null;
		bool any = false;
		foreach (string path in entryPaths)
		{
			if (path.Length == 0) continue;
			int slash = path.IndexOf('/');
			// 顶层就有文件, 不能剥
			if (slash < 0) return null;
			string head = path[..slash];
			if (top is null) top = head;
			else if (!string.Equals(top, head, StringComparison.Ordinal)) return null;
			any = true;
		}
		return any ? top : null;
	}

	/// <summary>
	/// 安全解压到目标目录
	///
	/// 会自动剥掉多余的唯一顶层目录; 拒绝符号链接条目; 每个文件写入前再校验一次父目录未越界.
	/// </summary>
	public static void Extract(string zipPath, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		string canonicalTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir));

		using ZipArchive archive = ZipFile.OpenRead(zipPath);

		// 先扫一遍拿到所有归一化路径, 决定要不要剥顶层目录
		List<(ZipArchiveEntry Entry, string Path)> entries = [];
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string sanitized = SanitizePath(entry.FullName);
			if (sanitized.Length == 0) continue;
			entries.Add((entry, sanitized));
		}
		string? commonTop = FindCommonTopDirectory(entries.Select(item => item.Path));

		foreach ((ZipArchiveEntry entry, string sanitized) in entries)
		{
			string relative = commonTop is null ? sanitized : sanitized[(commonTop.Length + 1)..];
			if (relative.Length == 0) continue;

			string outPath = Path.GetFullPath(Path.Combine(canonicalTarget, relative.Replace('/', Path.DirectorySeparatorChar)));
			if (!outPath.StartsWith(canonicalTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				throw new ResourceException($"ZIP 条目超出目标目录: {entry.FullName}");
			}

			// 目录项: ZipArchiveEntry 的目录项 Name 为空
			if (entry.Name.Length == 0)
			{
				Directory.CreateDirectory(outPath);
				continue;
			}
			if (IsSymlink(entry)) throw new ResourceException($"ZIP 包包含不允许的符号链接: {entry.FullName}");

			string parent = Path.GetDirectoryName(outPath) ?? throw new ResourceException($"ZIP 条目没有父目录: {entry.FullName}");
			Directory.CreateDirectory(parent);
			// 对 parent 再做一次校验, 防止已存在的软链接把路径带出目标目录
			string canonicalParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
			if (!canonicalParent.Equals(canonicalTarget, StringComparison.OrdinalIgnoreCase)
				&& !canonicalParent.StartsWith(canonicalTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				throw new ResourceException($"ZIP 条目超出目标目录: {entry.FullName}");
			}

			entry.ExtractToFile(outPath, overwrite: true);
		}
	}

	/// <summary>
	/// 判断 ZIP 条目是否是符号链接 (读 unix mode 的 S_IFLNK 位)
	/// </summary>
	private static bool IsSymlink(ZipArchiveEntry entry)
	{
		int mode = entry.ExternalAttributes >> 16;
		return (mode & 0xF000) == 0xA000;
	}
}
