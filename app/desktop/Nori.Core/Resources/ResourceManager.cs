using System.Text.Json;
using Nori.Core.Data;

namespace Nori.Core.Resources;

/// <summary>
/// 资源管理
///
/// 对应 Rust 版 resource/mod.rs + resource/live2d.rs + resource/downloader.rs 的编排部分:
/// 检查 → 获取签名 URL → 下载 ZIP → 安全解压 → 校验
/// </summary>
public sealed class ResourceManager(HttpClient httpClient, string? dataDir = null)
{
	/// <summary>资源下载 API</summary>
	private const string DownloadApi = "https://api.elake.top/nori/resource/download_url";

	/// <summary>下载缓冲区大小</summary>
	private const int BufferSize = 64 * 1024;

	private readonly HttpClient _httpClient = httpClient;
	private readonly string _dataDir = dataDir ?? AppPaths.DataDir;

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
	/// 确保资源已安装: 未安装则下载 → 解压 → 校验
	///
	/// 各阶段通过 onStep 回调实时上报, 由调用方转成 resource-download 事件
	/// </summary>
	public async Task EnsureAsync(ResourceType type, string name, Action<ResourceStep> onStep, CancellationToken cancellationToken = default)
	{
		name = ResourceName.Validate(name);
		string typeName = type.AsString();

		if (IsInstalled(type, name))
		{
			onStep(ResourceStep.Installed());
			return;
		}

		Directory.CreateDirectory(TempRoot);
		onStep(ResourceStep.Downloading(DownloadProgress.Create(0, null)));

		string zipPath = await DownloadToZipAsync(type, name, progress => onStep(ResourceStep.Downloading(progress)), cancellationToken);
		onStep(ResourceStep.DownloadDone());

		onStep(ResourceStep.Extracting());
		string targetDir = ResourceDir(type, name);
		// 清理可能残留的旧资源
		if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
		Directory.CreateDirectory(targetDir);
		try
		{
			ZipExtractor.Extract(zipPath, targetDir);
		}
		catch
		{
			// 解压失败清理半成品, 否则下次 IsInstalled 会看到一个坏目录
			TryDeleteDirectory(targetDir);
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
		TryDeleteFile(partPath);

		try
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(new Uri(signedUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode) throw new ResourceException($"下载文件 HTTP {(int)response.StatusCode}");
			long? total = response.Content.Headers.ContentLength;

			await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
			await using (FileStream target = File.Create(partPath))
			{
				byte[] buffer = new byte[BufferSize];
				long downloaded = 0;
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
		catch (Exception exception) when (exception is not ResourceException)
		{
			TryDeleteFile(partPath);
			throw new ResourceException($"网络错误: {exception.Message}", exception);
		}
		catch (ResourceException)
		{
			TryDeleteFile(partPath);
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
		Uri url = new($"{DownloadApi}?type={Uri.EscapeDataString(type.AsString())}&name={Uri.EscapeDataString(name)}");
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
	private static bool HasModel3Json(string dir)
	{
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
