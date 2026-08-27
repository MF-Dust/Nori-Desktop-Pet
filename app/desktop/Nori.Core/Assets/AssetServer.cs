using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Nori.Core.Voice;

namespace Nori.Core.Assets;

/// <summary>资源服务配置</summary>
public sealed record AssetServerOptions
{
	/// <summary>前端 bundle 目录 (生产模式下的 dist)</summary>
	public required string AppRoot { get; init; }

	/// <summary>资源目录 (data/resources)</summary>
	public required string ResourcesRoot { get; init; }

	/// <summary>开发模式: 固定端口且不加随机前缀, 让 vite 能把 /nori-assets 代理过来</summary>
	public bool DevMode { get; init; }

	/// <summary>开发模式下的固定端口</summary>
	public int DevPort { get; init; } = 14201;

	/// <summary>
	/// 一次性媒体交换所 (TTS 下发 / 录音上传)
	///
	/// 不传则自建一个; 宿主侧需要拿到同一个实例, 因此一般由调用方注入。
	/// </summary>
	public Nori.Core.Voice.MediaExchange? Media { get; init; }

	/// <summary>插件公开资源根目录解析回调。回调只应返回插件目录，不得返回宿主内部目录。</summary>
	public Func<string, string?>? PluginRootResolver { get; init; }
}

/// <summary>
/// 本机回环资源服务。静态文件中间件负责缓存/ETag，安全中间件负责路径与符号链接边界。
/// </summary>
public sealed class AssetServer : IAsyncDisposable
{
	private const string AppSegment = "app";
	private const string AssetSegment = "nori-assets";
	private const string MediaSegment = "media";
	private const long MaxMediaUploadBytes = VoiceAudioLimits.MaxBytes;

	private readonly WebApplication _app;
	private readonly AssetServerOptions _options;

	public string Prefix { get; }

	public string Origin { get; }

	/// <summary>一次性媒体交换所</summary>
	public Nori.Core.Voice.MediaExchange Media { get; }

	public string AppUrl => _options.DevMode ? "http://localhost:1420/index.html" : $"{Origin}{Prefix}/{AppSegment}/index.html";

	/// <summary>按 token 拼出媒体端点 URL (下载与上传同一地址, 区分在 HTTP 方法)</summary>
	public string MediaUrl(string token) => _options.DevMode
		? $"/{MediaSegment}/{token}"
		: $"{Origin}{Prefix}/{MediaSegment}/{token}";

	/// <summary>拼出插件公开资源 URL。</summary>
	public string PluginAssetUrl(string pluginId, string relativePath)
	{
		if (!IsSafePluginId(pluginId) || !IsPublicPluginAsset(relativePath)) throw new ArgumentException("插件资源路径无效", nameof(relativePath));
		string escapedPath = string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
		string route = $"/plugins/{Uri.EscapeDataString(pluginId)}/{escapedPath}";
		return _options.DevMode ? route : $"{Origin}{Prefix}{route}";
	}

	private AssetServer(WebApplication app, AssetServerOptions options, string prefix, string origin, Nori.Core.Voice.MediaExchange media)
	{
		_app = app;
		_options = options;
		Prefix = prefix;
		Origin = origin;
		Media = media;
	}

	/// <summary>启动服务, 返回可用的实例。</summary>
	public static async Task<AssetServer> StartAsync(AssetServerOptions options, CancellationToken cancellationToken = default)
	{
		string prefix = options.DevMode ? string.Empty : "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
		Nori.Core.Voice.MediaExchange media = options.Media ?? new Nori.Core.Voice.MediaExchange();
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.Listen(IPAddress.Loopback, options.DevMode ? options.DevPort : 0);
			kestrel.AddServerHeader = false;
		});

		WebApplication app = builder.Build();
		PhysicalFileProvider appProvider = new(options.AppRoot);
		PhysicalFileProvider resourceProvider = new(options.ResourcesRoot, Microsoft.Extensions.FileProviders.Physical.ExclusionFilters.None);
		PathString appPath = new($"{prefix}/{AppSegment}");
		PathString resourcePath = new($"{prefix}/{AssetSegment}");

		// 主机过滤拒绝未知 Host，并返回旧回环服务约定的 403，避免泄露框架诊断正文。
		HashSet<string> allowedHosts = new(StringComparer.OrdinalIgnoreCase)
		{
			"127.0.0.1", "localhost", "[::1]", "::1",
		};
		app.Use(async (context, next) =>
		{
			if (!allowedHosts.Contains(context.Request.Host.Host))
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return;
			}
			await next();
		});

		// 插件公开资源端点排在静态文件之前。插件运行时回调只提供插件根目录,
		// 此处仍执行公开 allowlist、路径和符号链接校验, 不信任回调返回的请求路径。
		PathString pluginPath = new($"{prefix}/plugins");
		app.Use(async (context, next) =>
		{
			string requestPath = context.Request.Path.Value ?? string.Empty;
			string pluginPathValue = pluginPath.Value ?? string.Empty;
			if (!IsPathUnder(requestPath, pluginPathValue))
			{
				await next();
				return;
			}
			string relative = requestPath[pluginPathValue.Length..].Trim('/');
			string[] parts = relative.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
			string? pluginId = parts.Length == 2 ? AssetPath.PercentDecode(parts[0]) : null;
			string? assetPath = parts.Length == 2 ? AssetPath.PercentDecode(parts[1]) : null;
			if (pluginId is null || assetPath is null || pluginId.Length is 0 or > 128 || !IsSafePluginId(pluginId) || !IsPublicPluginAsset(assetPath))
			{
				await Fail(context);
				return;
			}
			string? root = options.PluginRootResolver?.Invoke(pluginId);
			string? resolved = root is null ? null : AssetPath.ResolveExact(root, assetPath);
			if (resolved is null)
			{
				await Fail(context);
				return;
			}
			context.Response.ContentType = AssetPath.MimeFor(assetPath);
			context.Response.ContentLength = new FileInfo(resolved).Length;
			context.Response.Headers.CacheControl = "public, max-age=3600";
			await context.Response.SendFileAsync(resolved, context.RequestAborted);
		});

		// 一次性媒体端点: GET 取走待播音频, POST 接收前端录音
		// 注意: 要排在静态文件中间件之前, 否则会被 404 兑底接走
		PathString mediaPath = new($"{prefix}/{MediaSegment}");
		app.Use(async (context, next) =>
		{
			string path = context.Request.Path.Value ?? "";
			string mediaPathValue = mediaPath.Value ?? string.Empty;
			if (mediaPathValue.Length == 0 || !path.StartsWith(mediaPathValue + "/", StringComparison.Ordinal))
			{
				await next();
				return;
			}

			string token = path[(mediaPathValue.Length + 1)..].Trim('/');
			if (token.Length is 0 or > 64 || !token.All(char.IsAsciiLetterOrDigit))
			{
				await Fail(context);
				return;
			}

			if (HttpMethods.IsGet(context.Request.Method))
			{
				if (!media.TryTakeAudio(token, out byte[] data, out string mime))
				{
					await Fail(context);
					return;
				}
				context.Response.ContentType = mime;
				context.Response.ContentLength = data.Length;
				context.Response.Headers.CacheControl = "no-store";
				await context.Response.Body.WriteAsync(data, context.RequestAborted);
				return;
			}

			if (HttpMethods.IsPost(context.Request.Method))
			{
				IHttpMaxRequestBodySizeFeature? bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
				if (bodyLimit is not null) bodyLimit.MaxRequestBodySize = MaxMediaUploadBytes;
				if (context.Request.ContentLength > MaxMediaUploadBytes)
				{
					media.TryFailUpload(token, "录音上传超过 32MiB 限制");
					context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
					return;
				}

				string mime;
				try
				{
					// 旧的手写测试请求没有 Content-Type；真实 MediaRecorder 请求始终带实际 MIME。
					mime = AudioMime.Validate(context.Request.ContentType ?? "audio/wav");
				}
				catch (InvalidOperationException)
				{
					media.TryFailUpload(token, "录音 MIME 类型无效");
					context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
					return;
				}

				byte[]? bytes = await ReadCappedBodyAsync(context.Request.Body, context.RequestAborted);
				if (bytes is null)
				{
					media.TryFailUpload(token, "录音上传超过 32MiB 限制");
					context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
					return;
				}
				if (bytes.Length == 0)
				{
					media.TryFailUpload(token, "录音内容为空");
					context.Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}

				string fileName = context.Request.Headers["X-Nori-Audio-Filename"].ToString();
				RecordedAudio audio = new(bytes, mime, fileName);
				if (!media.TryCompleteUpload(token, audio))
				{
					if (!media.TryFailUpload(token, "录音内容无效")) await Fail(context);
					else context.Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}
				context.Response.StatusCode = StatusCodes.Status204NoContent;
				return;
			}

			context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
		});
		app.UseDefaultFiles(new DefaultFilesOptions
		{
			FileProvider = appProvider,
			RequestPath = appPath,
			DefaultFileNames = ["index.html"],
		});
		app.Use(async (context, next) =>
		{
			string requestPath = context.Request.Path.Value ?? "";
			string appPathValue = appPath.Value ?? string.Empty;
			string resourcePathValue = resourcePath.Value ?? string.Empty;
			string? rootPath = IsPathUnder(requestPath, appPathValue)
				? appPathValue
				: IsPathUnder(requestPath, resourcePathValue) ? resourcePathValue : null;
			if (rootPath is null)
			{
				await next();
				return;
			}

			string relative = requestPath[rootPath.Length..].TrimStart('/');
			string? decoded = AssetPath.PercentDecode(relative);
			string root = rootPath == appPathValue ? options.AppRoot : options.ResourcesRoot;
			string? resolved = decoded is not null && AssetPath.IsSafeRelativePath(decoded)
				? AssetPath.Resolve(root, decoded)
				: null;
			if (resolved is null)
			{
				await Fail(context);
				return;
			}

			string normalized = Path.GetRelativePath(root, resolved).Replace(Path.DirectorySeparatorChar, '/');
			context.Request.Path = new PathString($"{rootPath}/{normalized}");
			await next();
		});

		FileExtensionContentTypeProvider contentTypes = new();
		contentTypes.Mappings[".moc3"] = "application/octet-stream";
		contentTypes.Mappings[".motion3"] = "application/json; charset=utf-8";
		contentTypes.Mappings[".physics3"] = "application/json; charset=utf-8";
		contentTypes.Mappings[".exp3"] = "application/json; charset=utf-8";
		app.UseStaticFiles(new StaticFileOptions
		{
			FileProvider = appProvider,
			RequestPath = appPath,
			ContentTypeProvider = contentTypes,
			OnPrepareResponse = context =>
			{
				context.Context.Response.Headers.CacheControl = "no-cache";
				context.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
				if (string.Equals(Path.GetExtension(context.File.Name), ".html", StringComparison.OrdinalIgnoreCase))
				{
					context.Context.Response.ContentType = "text/html; charset=utf-8";
				}
			},
		});
		app.UseStaticFiles(new StaticFileOptions
		{
			FileProvider = resourceProvider,
			RequestPath = resourcePath,
			ServeUnknownFileTypes = true,
			DefaultContentType = "application/octet-stream",
			ContentTypeProvider = contentTypes,
			OnPrepareResponse = context =>
			{
				context.Context.Response.Headers.CacheControl = "public, max-age=3600";
				context.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
				if (string.Equals(Path.GetExtension(context.File.Name), ".json", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetExtension(context.File.Name), ".motion3", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetExtension(context.File.Name), ".physics3", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetExtension(context.File.Name), ".exp3", StringComparison.OrdinalIgnoreCase))
				{
					context.Context.Response.ContentType = "application/json; charset=utf-8";
				}
			},
		});
		app.Run(async context =>
		{
			if (!context.Response.HasStarted)
			{
				await Fail(context);
			}
		});

		await app.StartAsync(cancellationToken);
		string origin = app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal))
			?? throw new InvalidOperationException("资源服务未能绑定到回环地址");
		origin = origin.Replace("//localhost:", "//127.0.0.1:", StringComparison.Ordinal).TrimEnd('/');
		return new AssetServer(app, options, prefix, origin, media);
	}

	private static async Task<byte[]?> ReadCappedBodyAsync(Stream body, CancellationToken cancellationToken)
	{
		using MemoryStream buffer = new();
		byte[] chunk = new byte[64 * 1024];
		int read;
		while ((read = await body.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
		{
			if (buffer.Length + read > VoiceAudioLimits.MaxBytes) return null;
			buffer.Write(chunk, 0, read);
		}
		return buffer.ToArray();
	}

	private static async Task Fail(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status404NotFound;
		context.Response.ContentType = "text/plain; charset=utf-8";
		context.Response.Headers["Access-Control-Allow-Origin"] = "*";
		await context.Response.WriteAsync("资源不存在", context.RequestAborted);
	}

	private static bool IsSafePluginId(string value)
	{
		if (value.Length > 128) return false;
		string[] parts = value.Split('.');
		return parts.Length >= 2 && parts.All(part => part.Length > 0 && part.All(character => character is >= 'a' and <= 'z' || char.IsAsciiDigit(character) || character is '_' or '-'));
	}

	private static bool IsPublicPluginAsset(string path)
	{
		if (!AssetPath.IsSafeRelativePath(path) || path.Contains('\\') || path.Split('/').Any(segment => segment.Length == 0 || segment is "." or "..")) return false;
		return path.Equals("icon.png", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("web/", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("locales/", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPathUnder(string path, string root) =>
		root.Length > 0 && (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
			path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));

	public string WindowUrl(string label) => $"{AppUrl}?window={Uri.EscapeDataString(label)}";

	public async ValueTask DisposeAsync()
	{
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
