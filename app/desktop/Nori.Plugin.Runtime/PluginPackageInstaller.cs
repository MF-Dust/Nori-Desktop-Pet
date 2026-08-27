using System.IO.Compression;
using System.Text.Json;
using Nori.Core.Resources;

namespace Nori.Plugin.Runtime;

/// <summary>插件包目录布局与原子安装。</summary>
public sealed class PluginPackageInstaller
{
	public const string ManifestFileName = "plugin.json";
	public const string LegacyManifestFileName = "manifest.json";
	private readonly string _root;

	public PluginPackageInstaller(string root)
	{
		_root = Path.GetFullPath(root);
		Directory.CreateDirectory(_root);
		Directory.CreateDirectory(Path.Combine(_root, "staging"));
		Directory.CreateDirectory(Path.Combine(_root, "versions"));
	}

	public string RootDirectory => _root;
	public string CurrentDirectory(string pluginId) => Path.Combine(_root, "current", pluginId);
	public string VersionDirectory(string pluginId, string version) => Path.Combine(_root, "versions", pluginId, version);

	public PluginManifest InspectPackage(string packagePath)
	{
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(packagePath);
			ValidateEntries(archive);
			ZipArchiveEntry entry = FindManifestEntry(archive);
			using Stream stream = entry.Open();
			using StreamReader reader = new(stream);
			return PluginManifestReader.ReadJson(reader.ReadToEnd());
		}
		catch (PluginException) { throw; }
		catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException or JsonException or ResourceException)
		{
			throw new PluginException(PluginErrorCodes.PackageInvalid, "插件包无效", exception);
		}
	}

	public PluginManifest Install(string packagePath, CancellationToken cancellationToken = default)
	{
		PluginManifest manifest = InspectPackage(packagePath);
		string staging = Path.Combine(_root, "staging", $"{manifest.PluginId}-{Guid.NewGuid():N}");
		string versionDirectory = VersionDirectory(manifest.PluginId, manifest.Version);
		string currentDirectory = CurrentDirectory(manifest.PluginId);
		try
		{
			Directory.CreateDirectory(staging);
			ZipExtractor.Extract(packagePath, staging, cancellationToken);
			string extractedManifest = File.Exists(Path.Combine(staging, ManifestFileName)) ? ManifestFileName : LegacyManifestFileName;
			PluginManifest extracted = PluginManifestReader.Read(Path.Combine(staging, extractedManifest));
			if (!string.Equals(extracted.PluginId, manifest.PluginId, StringComparison.Ordinal) || !string.Equals(extracted.Version, manifest.Version, StringComparison.Ordinal))
				throw new PluginException(PluginErrorCodes.PackageInvalid, "安装前后插件清单不一致");
			Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
			if (Directory.Exists(versionDirectory)) Directory.Delete(versionDirectory, true);
			Directory.Move(staging, versionDirectory);
			string currentStaging = Path.Combine(_root, "staging", $"current-{manifest.PluginId}-{Guid.NewGuid():N}");
			CopyDirectory(versionDirectory, currentStaging);
			AtomicReplaceCurrent(currentStaging, currentDirectory);
			return extracted;
		}
		catch (PluginException) { TryDelete(staging); throw; }
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ResourceException)
		{
			TryDelete(staging);
			throw new PluginException(PluginErrorCodes.PackageInvalid, "插件包安装失败", exception);
		}
	}

	private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName)
	{
		(List<ZipArchiveEntry> Entries, string? CommonTop) = PackageEntries(archive);
		ZipArchiveEntry? entry = Entries.FirstOrDefault(item =>
		{
			string path = StripTop(ZipExtractor.SanitizePath(item.FullName), CommonTop);
			return path.Equals(fileName, StringComparison.OrdinalIgnoreCase);
		});
		return entry ?? throw new PluginException(PluginErrorCodes.PackageInvalid, "插件包缺少 plugin.json");
	}

	private static void ValidateEntries(ZipArchive archive)
	{
		if (archive.Entries.Count == 0 || archive.Entries.Count > 2048) throw new PluginException(PluginErrorCodes.PackageInvalid, "插件包没有有效内容");
		(List<ZipArchiveEntry> Entries, string? CommonTop) = PackageEntries(archive);
		bool manifest = false;
		foreach (ZipArchiveEntry entry in Entries)
		{
			string path = StripTop(ZipExtractor.SanitizePath(entry.FullName), CommonTop);
			if (path.Length == 0) continue;
			if (IsManifest(path)) manifest = true;
			if (entry.Name.Length == 0) continue;
			bool allowed = path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
				|| path.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
				|| PluginAssetReader.IsPublicAsset(path);
			if (!allowed && !IsManifest(path))
				throw new PluginException(PluginErrorCodes.AssetDenied, $"插件包包含不允许的文件: {path}");
		}
		if (!manifest) throw new PluginException(PluginErrorCodes.PackageInvalid, "插件包缺少 plugin.json");
	}

	private static ZipArchiveEntry FindManifestEntry(ZipArchive archive)
	{
		try { return FindEntry(archive, ManifestFileName); }
		catch (PluginException) { return FindEntry(archive, LegacyManifestFileName); }
	}

	private static (List<ZipArchiveEntry> Entries, string? CommonTop) PackageEntries(ZipArchive archive)
	{
		List<ZipArchiveEntry> entries = archive.Entries.Where(entry => ZipExtractor.SanitizePath(entry.FullName).Length > 0).ToList();
		string? commonTop = ZipExtractor.FindCommonTopDirectory(entries.Where(entry => entry.Name.Length > 0).Select(entry => ZipExtractor.SanitizePath(entry.FullName)));
		return (entries, commonTop);
	}

	private static bool IsManifest(string path) => path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) || path.Equals(LegacyManifestFileName, StringComparison.OrdinalIgnoreCase);

	private static string StripTop(string path, string? commonTop)
	{
		if (commonTop is null) return path;
		string prefix = commonTop + "/";
		return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
	}

	private static void CopyDirectory(string source, string target)
	{
		Directory.CreateDirectory(target);
		foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			string destination = Path.Combine(target, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(file, destination);
		}
	}

	private static void AtomicReplaceCurrent(string source, string current)
	{
		string? parent = Path.GetDirectoryName(current);
		if (parent is null) throw new IOException("当前插件目录没有父目录");
		Directory.CreateDirectory(parent);
		string backup = current + ".old-" + Guid.NewGuid().ToString("N");
		if (Directory.Exists(current)) Directory.Move(current, backup);
		try { Directory.Move(source, current); }
		catch
		{
			if (Directory.Exists(backup) && !Directory.Exists(current)) Directory.Move(backup, current);
			throw;
		}
		TryDelete(backup);
	}

	private static void TryDelete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
