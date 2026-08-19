using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nori.Core.Assets;

/// <summary>
/// 资源服务配置
/// </summary>
public sealed record AssetServerOptions
{
	/// <summary>前端 bundle 目录 (生产模式下的 dist)</summary>
	public required string AppRoot { get; init; }

	/// <summary>资源目录 (data/resources)</summary>
	public required string ResourcesRoot { get; init; }

	/// <summary>
	/// 开发模式: 固定端口且不加随机前缀, 让 vite 能把 /nori-assets 代理过来
	/// </summary>
	public bool DevMode { get; init; }

	/// <summary>开发模式下的固定端口</summary>
	public int DevPort { get; init; } = 14201;
}

/// <summary>
/// 本机回环资源服务
///
/// 取代 Tauri 的 nori-asset:// 自定义协议. Avalonia 的 WebResourceRequested 只读,
/// 无法回写响应, 因此改用只绑回环地址的 Kestrel, 把 asset.rs 的安全逻辑原样搬过来.
///
/// 生产: 随机端口 + 随机路径前缀, 前端与资源同源, 免掉跨域与 CSP 的麻烦
/// 开发: 固定端口且无前缀, 前端仍由 vite 提供, /nori-assets 由 vite 代理到这里
/// </summary>
public sealed class AssetServer : IAsyncDisposable
{
	/// <summary>应用路径段</summary>
	private const string AppSegment = "app";

	/// <summary>资源路径段, 与前端 assetUrl() 保持一致</summary>
	private const string AssetSegment = "nori-assets";

	private readonly WebApplication _app;
	private readonly AssetServerOptions _options;

	/// <summary>随机路径前缀, 开发模式下为空</summary>
	public string Prefix { get; }

	/// <summary>服务根地址, 形如 http://127.0.0.1:51234</summary>
	public string Origin { get; }

	/// <summary>前端入口地址 (不含 window 查询参数)</summary>
	public string AppUrl => _options.DevMode ? "http://localhost:1420/index.html" : $"{Origin}{Prefix}/{AppSegment}/index.html";

	private AssetServer(WebApplication app, AssetServerOptions options, string prefix, string origin)
	{
		_app = app;
		_options = options;
		Prefix = prefix;
		Origin = origin;
	}

	/// <summary>
	/// 启动服务, 返回可用的实例
	/// </summary>
	public static async Task<AssetServer> StartAsync(AssetServerOptions options, CancellationToken cancellationToken = default)
	{
		string prefix = options.DevMode ? string.Empty : "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			// 只绑 IPv4 回环: 不接受来自局域网的连接.
			// 注意不能用 ListenLocalhost(0), Kestrel 不允许 localhost 配动态端口
			kestrel.Listen(System.Net.IPAddress.Loopback, options.DevMode ? options.DevPort : 0);
			kestrel.AddServerHeader = false;
		});

		WebApplication app = builder.Build();

		app.Use(async (context, next) =>
		{
			// Host 头必须是回环地址, 挡掉 DNS rebinding
			string host = context.Request.Host.Host;
			if (host is not ("127.0.0.1" or "localhost" or "[::1]" or "::1"))
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return;
			}
			await next();
		});

		app.MapGet($"{prefix}/{AppSegment}/{{**path}}", (HttpContext context, string? path) =>
			Serve(context, options.AppRoot, string.IsNullOrEmpty(path) ? "index.html" : path, cache: false));

		app.MapGet($"{prefix}/{AssetSegment}/{{**path}}", (HttpContext context, string? path) =>
			Serve(context, options.ResourcesRoot, path ?? string.Empty, cache: true));

		await app.StartAsync(cancellationToken);

		string origin = app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal))
			?? throw new InvalidOperationException("资源服务未能绑定到回环地址");
		// Kestrel 报的是 http://localhost:PORT, 统一成 127.0.0.1 避免 IPv6 解析差异
		origin = origin.Replace("//localhost:", "//127.0.0.1:", StringComparison.Ordinal).TrimEnd('/');

		return new AssetServer(app, options, prefix, origin);
	}

	/// <summary>
	/// 解析并输出一个文件
	/// </summary>
	private static async Task Serve(HttpContext context, string root, string relative, bool cache)
	{
		string? decoded = AssetPath.PercentDecode(relative);
		if (decoded is null)
		{
			await Fail(context, StatusCodes.Status400BadRequest, "URL 路径编码非法");
			return;
		}
		decoded = decoded.TrimStart('/');
		if (decoded.Length == 0)
		{
			await Fail(context, StatusCodes.Status404NotFound, "空路径");
			return;
		}
		if (!AssetPath.IsSafeRelativePath(decoded))
		{
			await Fail(context, StatusCodes.Status403Forbidden, "非法资源路径");
			return;
		}
		string? file = AssetPath.Resolve(root, decoded);
		if (file is null)
		{
			await Fail(context, StatusCodes.Status404NotFound, $"资源不存在: {decoded}");
			return;
		}
		context.Response.ContentType = AssetPath.MimeFor(file);
		context.Response.Headers["Access-Control-Allow-Origin"] = "*";
		context.Response.Headers.CacheControl = cache ? "public, max-age=3600" : "no-cache";
		await context.Response.SendFileAsync(file, context.RequestAborted);
	}

	/// <summary>
	/// 输出纯文本错误
	/// </summary>
	private static async Task Fail(HttpContext context, int status, string message)
	{
		context.Response.StatusCode = status;
		context.Response.ContentType = "text/plain; charset=utf-8";
		context.Response.Headers["Access-Control-Allow-Origin"] = "*";
		await context.Response.WriteAsync(message, context.RequestAborted);
	}

	/// <summary>
	/// 拼出某个窗口的入口地址
	/// </summary>
	public string WindowUrl(string label) => $"{AppUrl}?window={Uri.EscapeDataString(label)}";

	public async ValueTask DisposeAsync()
	{
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
