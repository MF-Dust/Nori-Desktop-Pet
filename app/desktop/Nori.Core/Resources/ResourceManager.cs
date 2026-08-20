using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Nori.Core.Data;

namespace Nori.Core.Resources;

/// <summary>
/// 资源管理
///
/// 对应 Rust 版 resource/mod.rs + resource/live2d.rs + resource/downloader.rs 的编排部分:
/// 检查 → 获取签名 URL → 下载 ZIP → 安全解压 → 校验
/// </summary>
public sealed class ResourceManager(HttpClient httpClient, string? dataDir = null, string? apiBaseUrl = null)
{
	/// <summary>默认资源 API 根地址</summary>
	private const string DefaultApiBaseUrl = "https://api.elake.top/nori";

	/// <summary>下载缓冲区大小</summary>
	private const int BufferSize = 64 * 1024;

	private readonly HttpClient _httpClient = httpClient;
	private readonly string _dataDir = dataDir ?? AppPaths.DataDir;
	private readonly string _apiBaseUrl = (apiBaseUrl ?? DefaultApiBaseUrl).TrimEnd('/');

	private string ResourcesRoot => Path.Combine(_dataDir, AppPaths.ResourcesDirName);
	private string TempRoot => Path.Combine(_dataDir, AppPaths.TempDirName);

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
	/// 获取资源发布清单. 旧网关没有清单接口时返回 null, 不阻断已有下载流程.
	/// </summary>
	public async Task<ResourceManifest?> GetManifestAsync(ResourceType type, string name, CancellationToken cancellationToken = default)
	{
		name = ResourceName.Validate(name);
		Uri url = new($"{_apiBaseUrl}/resource/manifest?type={Uri.EscapeDataString(type.AsString())}&name={Uri.EscapeDataString(name)}");
		using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
		if (response.StatusCode == HttpStatusCode.NotFound) return null;
		if (!response.IsSuccessStatusCode) throw new ResourceException($"Manifest API HTTP {(int)response.StatusCode}");
		try
		{
			using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			if (!document.RootElement.TryGetProperty("body", out JsonElement body) || body.ValueKind == JsonValueKind.Null) return null;
			return body.Deserialize<ResourceManifest>();
		}
		catch (JsonException exception)
		{
			throw new ResourceException($"解析资源 Manifest 失败: {exception.Message}", exception);
		}
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
		using (ZipArchive archive = ZipFile.OpenRead(zipPath))
		{
			var modelEntries = archive.Entries
				.Where(e => e.FullName.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (modelEntries.Count == 0)
			{
				throw new ResourceException("压缩包中未找到任何 .model3.json 模型定义文件");
			}

			foreach (var modelEntry in modelEntries)
			{
				string entryPath = modelEntry.FullName.Replace('\\', '/');
				string modelFileName = Path.GetFileName(entryPath);
				string prefixDir = "";
				int lastSlash = entryPath.LastIndexOf('/');
				if (lastSlash >= 0)
				{
					prefixDir = entryPath[..(lastSlash + 1)];
				}

				string modelId;
				if (modelFileName.Equals("ARGNori.model3.json", StringComparison.OrdinalIgnoreCase))
				{
					modelId = "arg-nori";
				}
				else if (modelFileName.Equals("Nori.model3.json", StringComparison.OrdinalIgnoreCase))
				{
					modelId = "nori";
				}
				else if (!string.IsNullOrEmpty(prefixDir))
				{
					string folderName = prefixDir.TrimEnd('/');
					int prevSlash = folderName.LastIndexOf('/');
					if (prevSlash >= 0) folderName = folderName[(prevSlash + 1)..];
					folderName = folderName.Replace("_web", "", StringComparison.OrdinalIgnoreCase)
						.Replace(" ", "-").ToLowerInvariant();
					modelId = folderName;
				}
				else
				{
					modelId = Path.GetFileNameWithoutExtension(modelFileName)
						.Replace(".model3", "", StringComparison.OrdinalIgnoreCase)
						.ToLowerInvariant();
				}

				string targetDir = ResourceDir(type, modelId);
				if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
				Directory.CreateDirectory(targetDir);

				foreach (var entry in archive.Entries)
				{
					string ePath = entry.FullName.Replace('\\', '/');
					if (!string.IsNullOrEmpty(prefixDir) && !ePath.StartsWith(prefixDir, StringComparison.Ordinal))
					{
						continue;
					}

					string relPath = string.IsNullOrEmpty(prefixDir) ? ePath : ePath[prefixDir.Length..];
					relPath = relPath.TrimStart('/');
					if (string.IsNullOrEmpty(relPath) || relPath.EndsWith('/'))
					{
						continue;
					}

					string destFile = Path.Combine(targetDir, relPath.Replace('/', Path.DirectorySeparatorChar));
					string? destFolder = Path.GetDirectoryName(destFile);
					if (!string.IsNullOrEmpty(destFolder)) Directory.CreateDirectory(destFolder);

					entry.ExtractToFile(destFile, true);
				}

				if (IsInstalled(type, modelId))
				{
					importedModels.Add(modelId);
				}
			}
		}

		if (importedModels.Count == 0)
		{
			throw new ResourceException("未能成功解析和导入任何模型");
		}

		return importedModels;
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
			Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
		}
		foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
		{
			File.Copy(file, file.Replace(sourceDir, targetDir), true);
		}
	}

	/// <summary>
	/// 确保资源已安装: 未安装则下载 → 解压 → 校验
	///
	/// 各阶段通过 onStep 回调实时上报, 由调用方转成 resource-download 事件
	/// </summary>
	public async Task EnsureAsync(ResourceType type, string name, Action<ResourceStep> onStep, CancellationToken cancellationToken = default)
		=> await EnsureInternalAsync(type, name, onStep, false, cancellationToken);

	/// <summary>
	/// 强制重新下载并原子替换已安装资源.
	/// </summary>
	public async Task UpdateAsync(ResourceType type, string name, Action<ResourceStep> onStep, CancellationToken cancellationToken = default)
		=> await EnsureInternalAsync(type, name, onStep, true, cancellationToken);

	private async Task EnsureInternalAsync(ResourceType type, string name, Action<ResourceStep> onStep, bool force, CancellationToken cancellationToken)
	{
		name = ResourceName.Validate(name);
		string typeName = type.AsString();

		if (!force && IsInstalled(type, name))
		{
			onStep(ResourceStep.Installed());
			return;
		}

		Directory.CreateDirectory(TempRoot);
		onStep(ResourceStep.Downloading(DownloadProgress.Create(0, null)));

		ResourceManifest? manifest = await GetManifestAsync(type, name, cancellationToken);
		string zipPath = await DownloadToZipAsync(type, name, progress => onStep(ResourceStep.Downloading(progress)), cancellationToken);
		if (manifest is not null)
		{
			if (manifest.Size > 0 && new FileInfo(zipPath).Length != manifest.Size)
			{
				TryDeleteFile(zipPath);
				throw new ResourceException($"资源大小校验失败: {name}");
			}
			if (!string.IsNullOrWhiteSpace(manifest.Sha256))
			{
				string actual = await ComputeSha256Async(zipPath, cancellationToken);
				if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					TryDeleteFile(zipPath);
					throw new ResourceException($"资源 SHA-256 校验失败: {name}");
				}
			}
		}
		onStep(ResourceStep.DownloadDone());

		onStep(ResourceStep.Extracting());
		string targetDir = ResourceDir(type, name);
		string stagingDir = targetDir + $".staging-{Guid.NewGuid():N}";
		string? backupDir = null;
		try
		{
			Directory.CreateDirectory(stagingDir);
			ZipExtractor.Extract(zipPath, stagingDir);
			if (!IsInstalledAt(type, stagingDir)) throw new ResourceException($"资源解压后校验失败: type={type.AsString()} name={name}");
			if (Directory.Exists(targetDir))
			{
				backupDir = targetDir + $".backup-{Guid.NewGuid():N}";
				Directory.Move(targetDir, backupDir);
			}
			Directory.Move(stagingDir, targetDir);
			if (backupDir is not null) TryDeleteDirectory(backupDir);
		}
		catch
		{
			TryDeleteDirectory(stagingDir);
			if (backupDir is not null)
			{
				if (!Directory.Exists(targetDir) && Directory.Exists(backupDir)) Directory.Move(backupDir, targetDir);
				else TryDeleteDirectory(backupDir);
			}
			throw;
		}
		finally
		{
			TryDeleteFile(zipPath);
		}

		if (!IsInstalled(type, name))
		{
			TryDeleteDirectory(targetDir);
			throw new ResourceException($"资源解压后校验失败: type={typeName} name={name}");
		}
		onStep(ResourceStep.Done());
	}

	/// <summary>
	/// 下载 ZIP 到临时目录, 先写 .part 再改名, 避免半截文件被当成完整包
	/// </summary>
	private async Task<string> DownloadToZipAsync(ResourceType type, string name, Action<DownloadProgress> onProgress, CancellationToken cancellationToken)
	{
		string signedUrl = await GetSignedUrlAsync(type, name, cancellationToken);
		string zipPath = Path.Combine(TempRoot, $"{name}.zip");
		string partPath = Path.Combine(TempRoot, $"{name}.zip.part");
		try
		{
			long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
			using HttpRequestMessage request = new(HttpMethod.Get, new Uri(signedUrl));
			if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
			using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode) throw new ResourceException($"下载文件 HTTP {(int)response.StatusCode}");
			if (existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
			{
				existing = 0;
				TryDeleteFile(partPath);
			}
			long? total = response.Content.Headers.ContentLength is { } contentLength ? contentLength + existing : null;

			await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
			await using (FileStream target = new(partPath, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
			{
				byte[] buffer = new byte[BufferSize];
				long downloaded = existing;
				int read;
				while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
				{
					await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					downloaded += read;
					onProgress(DownloadProgress.Create(downloaded, total));
				}
				await target.FlushAsync(cancellationToken);

				if (total is { } expected && downloaded != expected)
				{
					throw new ResourceException($"下载文件大小不完整: {downloaded}/{expected}");
				}
				onProgress(DownloadProgress.Completed(downloaded));
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception) when (exception is not ResourceException)
		{
			throw new ResourceException($"网络错误: {exception.Message}", exception);
		}
		catch (ResourceException)
		{
			throw;
		}

		TryDeleteFile(zipPath);
		File.Move(partPath, zipPath);
		return zipPath;
	}

	/// <summary>
	/// 从网关获取签名下载 URL
	///
	/// 响应信封: {error, message, body, timestamp}, 先看 error 再取 body.url
	/// </summary>
	private async Task<string> GetSignedUrlAsync(ResourceType type, string name, CancellationToken cancellationToken)
	{
		Uri url = new($"{_apiBaseUrl}/resource/download_url?type={Uri.EscapeDataString(type.AsString())}&name={Uri.EscapeDataString(name)}");
		HttpResponseMessage response;
		try
		{
			response = await _httpClient.GetAsync(url, cancellationToken);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
		{
			throw new ResourceException($"网络错误: {exception.Message}", exception);
		}
		using (response)
		{
			if (!response.IsSuccessStatusCode) throw new ResourceException($"下载 API HTTP {(int)response.StatusCode}");
			JsonDocument document;
			try
			{
				document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			}
			catch (JsonException exception)
			{
				throw new ResourceException($"解析 API 响应失败: {exception.Message}", exception);
			}
			using (document)
			{
				JsonElement root = document.RootElement;
				if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.True)
				{
					string message = root.TryGetProperty("message", out JsonElement value) && value.GetString() is { Length: > 0 } text
						? text
						: "接口返回错误";
					throw new ResourceException($"API 错误: {message}");
				}
				if (root.TryGetProperty("body", out JsonElement body)
					&& body.TryGetProperty("url", out JsonElement urlValue)
					&& urlValue.GetString() is { Length: > 0 } signed)
				{
					return signed;
				}
				throw new ResourceException("API 错误: API 响应中缺少 body.url");
			}
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

	private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = File.OpenRead(path);
		byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
		return Convert.ToHexString(hash).ToLowerInvariant();
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

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (File.Exists(path)) File.Delete(path);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, true);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
