using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nori.Core.Data;

/// <summary>只用于一次性迁移的旧 Tauri 数据目录解析器。</summary>
public static class LegacyDataPathResolver
{
	/// <summary>返回旧版 app_data_dir()/data，不应被业务读写路径使用。</summary>
	public static string Resolve()
	{
		if (OperatingSystem.IsMacOS())
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			return Path.Combine(home, "Library", "Application Support", AppPaths.Identifier, "data");
		}
		if (OperatingSystem.IsLinux())
		{
			string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			string root = string.IsNullOrWhiteSpace(xdg)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
				: xdg;
			return Path.Combine(root, AppPaths.Identifier, "data");
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPaths.Identifier, "data");
	}
}

/// <summary>启动阶段根据 launcher、开发环境或安全的槽目录推断包根。</summary>
public static class AppStoragePathResolver
{
	private static readonly Regex SlotPattern = new("^app-[0-9]+\\.[0-9]+\\.[0-9]+-[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

	public static AppStoragePaths Resolve(string? launcherPath = null, string? baseDirectory = null)
	{
		string? explicitRoot = Environment.GetEnvironmentVariable("NORI_PACKAGE_ROOT");
		if (!string.IsNullOrWhiteSpace(explicitRoot)) return new AppStoragePaths(explicitRoot);
		if (string.Equals(Environment.GetEnvironmentVariable("NORI_DEV"), "1", StringComparison.Ordinal))
			return new AppStoragePaths(Environment.GetEnvironmentVariable("NORI_DEV_PACKAGE_ROOT") ?? Environment.CurrentDirectory);

		string supplied = launcherPath ?? baseDirectory ?? AppContext.BaseDirectory;
		string full = Path.GetFullPath(supplied);
		string directory = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
		string? packageRoot = ValidatePublishedSlot(directory);
		if (packageRoot is not null) return new AppStoragePaths(packageRoot);
		throw new InvalidOperationException("无法安全推断包根目录，请通过 Nori 启动器启动应用");
	}

	private static string? ValidatePublishedSlot(string directory)
	{
		DirectoryInfo slot = new(directory);
		if (!SlotPattern.IsMatch(slot.Name) || !slot.Exists || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return null;
		string manifestPath = Path.Combine(directory, "deployment.json");
		if (!File.Exists(manifestPath) || (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0) return null;
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("schema_version", out JsonElement schema) || schema.GetInt32() != 1
				|| !root.TryGetProperty("rid", out JsonElement rid) || string.IsNullOrWhiteSpace(rid.GetString())
				|| !root.TryGetProperty("entrypoint", out JsonElement entry) || string.IsNullOrWhiteSpace(entry.GetString())) return null;
			string relative = entry.GetString()!;
			if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or "..")) return null;
			string? processPath = Environment.ProcessPath;
			if (processPath is null) return null;
			string expected = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
			if (!string.Equals(expected, Path.GetFullPath(processPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
			if (!File.Exists(expected) || (File.GetAttributes(expected) & FileAttributes.ReparsePoint) != 0) return null;
			return slot.Parent?.FullName;
		}
		catch (JsonException) { return null; }
		catch (IOException) { return null; }
		catch (UnauthorizedAccessException) { return null; }
	}
}
