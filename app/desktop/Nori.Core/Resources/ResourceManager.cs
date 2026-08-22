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
	/// 从本地 ZIP 压缩包或目录导入资源.
	/// 所有候选先在 resources 根目录同卷 staging 中复制并验证, 成功后才交换 target.
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
		string stagingRoot = CreateStagingRoot();
		try
		{
			string extractedRoot = Path.Combine(stagingRoot, "extracted");
			ZipExtractor.Extract(zipPath, extractedRoot);
			IReadOnlyList<ModelCandidate> candidates = CollectCandidates(extractedRoot);
			return PrepareAndSwap(type, candidates, stagingRoot);
		}
		catch (ResourceException)
		{
			throw;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new ResourceException($"导入资源失败: {exception.Message}", exception);
		}
		finally
		{
			TryDeleteDirectory(stagingRoot);
		}
	}

	private IReadOnlyList<string> ImportFromDirectory(ResourceType type, string sourceDir)
	{
		string stagingRoot = CreateStagingRoot();
		try
		{
			IReadOnlyList<ModelCandidate> candidates = CollectCandidates(sourceDir);
			return PrepareAndSwap(type, candidates, stagingRoot);
		}
		catch (ResourceException)
		{
			throw;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new ResourceException($"导入资源失败: {exception.Message}", exception);
		}
		finally
		{
			TryDeleteDirectory(stagingRoot);
		}
	}

	/// <summary>创建与目标目录同卷、且不属于 live2d 枚举范围的 staging 根.</summary>
	private string CreateStagingRoot()
	{
		Directory.CreateDirectory(ResourcesRoot);
		string root = Path.Combine(ResourcesRoot, $".nori-import-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		return root;
	}

	/// <summary>
	/// 收集候选并拒绝重复模型 ID.
	/// 每个候选以包含 model3 JSON 的目录作为复制边界, 保留其纹理和动作相对路径.
	/// </summary>
	private static IReadOnlyList<ModelCandidate> CollectCandidates(string sourceRoot)
	{
		string[] modelFiles = Directory.GetFiles(sourceRoot, "*.model3.json", SearchOption.AllDirectories);
		if (modelFiles.Length == 0)
		{
			throw new ResourceException("所选资源中未找到任何 .model3.json 模型定义文件");
		}

		List<ModelCandidate> candidates = [];
		HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
		foreach (string modelFile in modelFiles)
		{
			string relativePath = Path.GetRelativePath(sourceRoot, modelFile);
			string modelId = ResourceName.Validate(ModelIdFromPath(relativePath));
			if (!ids.Add(modelId))
			{
				throw new ResourceException($"导入资源中存在重复模型 ID: {modelId}");
			}

			string modelDir = Path.GetDirectoryName(modelFile) ?? sourceRoot;
			candidates.Add(new ModelCandidate(modelId, modelDir));
		}
		return candidates;
	}

	/// <summary>
	/// 将所有候选复制到 staging/models 并完整验证, 再批量交换 target.
	/// </summary>
	private IReadOnlyList<string> PrepareAndSwap(
		ResourceType type,
		IReadOnlyList<ModelCandidate> candidates,
		string stagingRoot)
	{
		string preparedRoot = Path.Combine(stagingRoot, "prepared");
		Directory.CreateDirectory(preparedRoot);
		List<string> preparedIds = [];

		foreach (ModelCandidate candidate in candidates)
		{
			string preparedDir = Path.Combine(preparedRoot, candidate.Id);
			CopyDirectory(candidate.SourceDir, preparedDir);
			if (!IsInstalledAt(type, preparedDir))
			{
				throw new ResourceException($"模型 staging 校验失败: {candidate.Id}");
			}
			preparedIds.Add(candidate.Id);
		}

		if (preparedIds.Count == 0) throw new ResourceException("未能成功解析和导入任何模型");
		return SwapPrepared(type, preparedIds, preparedRoot);
	}

	/// <summary>
	/// 同卷目录交换. backup 在所有 target 完成替换前一直保留, 任一失败逆序回滚.
	/// </summary>
	private IReadOnlyList<string> SwapPrepared(ResourceType type, IReadOnlyList<string> modelIds, string preparedRoot)
	{
		string targetRoot = Path.Combine(ResourcesRoot, type.AsString());
		string backupRoot = Path.Combine(ResourcesRoot, $".nori-backup-{Guid.NewGuid():N}");
		Directory.CreateDirectory(targetRoot);
		Directory.CreateDirectory(backupRoot);

		List<(string Target, string Backup)> backups = [];
		List<string> installedTargets = [];
		try
		{
			foreach (string modelId in modelIds)
			{
				string targetDir = ResourceDir(type, modelId);
				string preparedDir = Path.Combine(preparedRoot, modelId);
				if (Directory.Exists(targetDir))
				{
					string backupDir = Path.Combine(backupRoot, modelId);
					Directory.Move(targetDir, backupDir);
					backups.Add((targetDir, backupDir));
				}

				Directory.Move(preparedDir, targetDir);
				installedTargets.Add(targetDir);
			}

			TryDeleteDirectory(backupRoot);
			return [.. modelIds];
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			try
			{
				RollbackSwap(installedTargets, backups);
			}
			catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
			{
				throw new ResourceException($"导入资源失败且回滚失败: {rollbackException.Message}", rollbackException);
			}

			throw new ResourceException($"导入资源失败: {exception.Message}", exception);
		}
		finally
		{
			TryDeleteDirectory(backupRoot);
		}
	}

	/// <summary>删除新 target 并按逆序恢复旧 backup.</summary>
	private static void RollbackSwap(
		IReadOnlyList<string> installedTargets,
		IReadOnlyList<(string Target, string Backup)> backups)
	{
		for (int index = installedTargets.Count - 1; index >= 0; index--)
		{
			string target = installedTargets[index];
			if (Directory.Exists(target)) Directory.Delete(target, true);
		}

		for (int index = backups.Count - 1; index >= 0; index--)
		{
			(string target, string backup) = backups[index];
			if (Directory.Exists(target)) Directory.Delete(target, true);
			if (Directory.Exists(backup)) Directory.Move(backup, target);
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

	private static void CopyDirectory(string sourceDir, string targetDir)
	{
		foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));
		}
		Directory.CreateDirectory(targetDir);
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

	private static void TryDeleteDirectory(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, true);
		}
		catch (IOException)
		{
			// staging/backup 清理失败不改变已经完成的交换结果.
		}
		catch (UnauthorizedAccessException)
		{
			// staging/backup 清理失败不改变已经完成的交换结果.
		}
	}

	private sealed record ModelCandidate(string Id, string SourceDir);
}
