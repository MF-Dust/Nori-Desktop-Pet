using Nori.Core.Data;

namespace Nori.Core.Resources;

/// <summary>
/// 本地资源管理
///
/// 负责模型资源的本地检查、列表、删除与从本地 ZIP/目录导入。
/// 不再包含远程下载逻辑。
/// </summary>
public sealed class ResourceManager(string? dataDir = null)
{
	private readonly string _dataDir = dataDir ?? AppPaths.DataDir;

	private string ResourcesRoot => Path.Combine(_dataDir, AppPaths.ResourcesDirName);

	/// <summary>
	/// 指定资源的目录
	/// </summary>
	public string ResourceDir(ResourceType type, string name) =>
		Path.Combine(ResourcesRoot, type.AsString(), ResourceName.Validate(name));

	/// <summary>
	/// 资源是否已安装. Live2D 必须真的包含 .model3.json 才算装好.
	/// </summary>
	public bool IsInstalled(ResourceType type, string name)
	{
		string dir = ResourceDir(type, name);
		if (!Directory.Exists(dir)) return false;
		return type switch
		{
			ResourceType.Live2D => HasModel3Json(dir),
			_ => true,
		};
	}

	/// <summary>
	/// 列出某类型下所有已安装资源 (按名称排序)
	/// </summary>
	public IReadOnlyList<ResourceInfo> List(ResourceType type)
	{
		string root = Path.Combine(ResourcesRoot, type.AsString());
		if (!Directory.Exists(root)) return [];
		List<ResourceInfo> result = [];
		foreach (string dir in Directory.EnumerateDirectories(root))
		{
			string name = Path.GetFileName(dir);
			if (type == ResourceType.Live2D && !HasModel3Json(dir)) continue;
			result.Add(new ResourceInfo
			{
				Name = name,
				ResourceType = type,
				Path = dir,
				Size = DirectorySize(dir),
			});
		}
		return [.. result.OrderBy(item => item.Name, StringComparer.Ordinal)];
	}

	/// <summary>
	/// 删除资源
	/// </summary>
	public void Delete(ResourceType type, string name)
	{
		string dir = ResourceDir(type, name);
		if (!Directory.Exists(dir)) throw new ResourceException($"资源不存在: {name}");
		Directory.Delete(dir, true);
	}

	/// <summary>
	/// 从本地 ZIP 压缩包或目录导入资源
	/// </summary>
	public IReadOnlyList<string> Import(ResourceType type, string sourcePath)
	{
		if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
		{
			throw new ResourceException($"导入源不存在: {sourcePath}");
		}

		if (File.Exists(sourcePath) && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
		{
			return ImportFromZip(type, sourcePath);
		}

		if (Directory.Exists(sourcePath))
		{
			return ImportFromDirectory(type, sourcePath);
		}

		throw new ResourceException("目前仅支持导入 .zip 压缩包或模型文件夹");
	}

	private IReadOnlyList<string> ImportFromZip(ResourceType type, string zipPath)
	{
		List<string> importedModels = [];
		string staging = Path.Combine(Path.GetTempPath(), $"nori-import-{Guid.NewGuid():N}");
		try
		{
			ZipExtractor.Extract(zipPath, staging);
			string[] modelFiles = Directory.GetFiles(staging, "*.model3.json", SearchOption.AllDirectories);

			if (modelFiles.Length == 0)
			{
				throw new ResourceException("压缩包中未找到任何 .model3.json 模型定义文件");
			}

			foreach (string modelFile in modelFiles)
			{
				string modelId = ModelIdFromPath(Path.GetRelativePath(staging, modelFile));
				string sourceDir = Path.GetDirectoryName(modelFile) ?? staging;

				string targetDir = ResourceDir(type, modelId);
				if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
				Directory.CreateDirectory(targetDir);
				CopyDirectory(sourceDir, targetDir);

				if (IsInstalled(type, modelId))
				{
					importedModels.Add(modelId);
				}
			}

			if (importedModels.Count == 0) throw new ResourceException("未能成功解析和导入任何模型");
			return importedModels;
		}
		finally
		{
			if (Directory.Exists(staging))
			{
				try { Directory.Delete(staging, true); }
				catch (IOException) { /* 临时目录清理失败不影响已完成导入 */ }
				catch (UnauthorizedAccessException) { /* 临时目录清理失败不影响已完成导入 */ }
			}
		}
	}

	private static string ModelIdFromPath(string relativePath)
	{
		string fileName = Path.GetFileName(relativePath);
		if (fileName.Equals("ARGNori.model3.json", StringComparison.OrdinalIgnoreCase)) return "arg-nori";
		if (fileName.Equals("Nori.model3.json", StringComparison.OrdinalIgnoreCase)) return "nori";
		string? folder = Path.GetDirectoryName(relativePath);
		string modelId = !string.IsNullOrEmpty(folder) ? Path.GetFileName(folder) : Path.GetFileNameWithoutExtension(fileName);
		return modelId.Replace(".model3", "", StringComparison.OrdinalIgnoreCase)
			.Replace("_web", "", StringComparison.OrdinalIgnoreCase)
			.Replace(" ", "-").ToLowerInvariant();
	}

	private IReadOnlyList<string> ImportFromDirectory(ResourceType type, string sourceDir)
	{
		var modelFiles = Directory.GetFiles(sourceDir, "*.model3.json", SearchOption.AllDirectories);
		if (modelFiles.Length == 0)
		{
			throw new ResourceException("所选目录中未找到 *.model3.json 文件");
		}

		List<string> importedModels = [];
		foreach (string modelFile in modelFiles)
		{
			string dir = Path.GetDirectoryName(modelFile)!;
			string fileName = Path.GetFileName(modelFile);
			string modelId;
			if (fileName.Equals("ARGNori.model3.json", StringComparison.OrdinalIgnoreCase)) modelId = "arg-nori";
			else if (fileName.Equals("Nori.model3.json", StringComparison.OrdinalIgnoreCase)) modelId = "nori";
			else modelId = Path.GetFileName(dir).Replace("_web", "").ToLowerInvariant();

			string targetDir = ResourceDir(type, modelId);
			if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
			Directory.CreateDirectory(targetDir);

			CopyDirectory(dir, targetDir);
			if (IsInstalled(type, modelId)) importedModels.Add(modelId);
		}
		return importedModels;
	}

	private static void CopyDirectory(string sourceDir, string targetDir)
	{
		foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));
		}
		foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
		{
			string destination = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
			string? parent = Path.GetDirectoryName(destination);
			if (parent is not null) Directory.CreateDirectory(parent);
			File.Copy(file, destination, true);
		}
	}

	/// <summary>
	/// 递归判断目录下是否存在 .model3.json
	/// </summary>
	private static bool HasModel3Json(string dir) => IsInstalledAt(ResourceType.Live2D, dir);

	private static bool IsInstalledAt(ResourceType type, string dir)
	{
		if (type != ResourceType.Live2D) return Directory.Exists(dir);
		try
		{
			return Directory.EnumerateFiles(dir, "*.model3.json", SearchOption.AllDirectories).Any();
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// 递归计算目录大小
	/// </summary>
	private static long DirectorySize(string dir)
	{
		try
		{
			return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
		}
		catch (IOException)
		{
			return 0;
		}
		catch (UnauthorizedAccessException)
		{
			return 0;
		}
	}
}
