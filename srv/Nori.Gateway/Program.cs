using Serilog;
using Serilog.Events;
using Nori.Gateway.Api;
using Nori.Gateway.Configuration;
using Nori.Gateway.Middleware;
using Nori.Gateway.Services;

// ---- 配置 ----
// 与 Go 版一致: 读工作目录下的 configs/config.yaml
GatewayConfig config;
try
{
	config = GatewayConfig.Load();
}
catch (InvalidOperationException exception)
{
	Console.Error.WriteLine($"启动失败: {exception.Message}");
	return 1;
}

// ---- 日志 ----
// Serilog 取代 zap + lumberjack, 保留系统日志与请求日志双输出
LoggerConfiguration logging = new LoggerConfiguration()
	.MinimumLevel.Is(config.Logger.Level.ToLowerInvariant() switch
	{
		"debug" => LogEventLevel.Debug,
		"warn" => LogEventLevel.Warning,
		"error" => LogEventLevel.Error,
		_ => LogEventLevel.Information,
	});
if (config.Logger.Output is "file" or "both")
{
	logging = logging.WriteTo.File(
		config.Logger.LogPath,
		rollingInterval: RollingInterval.Day,
		retainedFileCountLimit: config.Logger.MaxBackups,
		fileSizeLimitBytes: config.Logger.MaxSize * 1024L * 1024L,
		rollOnFileSizeLimit: true);
}
if (config.Logger.Output is "console" or "both") logging = logging.WriteTo.Console();
Log.Logger = logging.CreateLogger();

try
{
	WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
	builder.Host.UseSerilog();
	builder.WebHost.UseUrls($"http://0.0.0.0:{config.Gateway.Port}");
	builder.Services.AddSingleton(config);
	builder.Services.AddSingleton<OssService>();
	builder.Services.AddSingleton<AssetManifestStore>();

	WebApplication app = builder.Build();
	// 顺序与 Go 版 setupRouter 一致: CORS → RequestID → 请求日志
	app.UseGatewayCors();
	app.UseRequestId();
	app.UseRequestLogging();

	// GET /ping?timestamp=<客户端毫秒或秒时间戳> → {latency: "123ms"}
	app.MapGet("/ping", (HttpContext context) =>
	{
		string raw = context.Request.Query["timestamp"].ToString();
		if (raw.Length == 0) return ApiResponse.BadRequest("timestamp 不能为空");
		if (!long.TryParse(raw, out long clientTs)) return ApiResponse.BadRequest("无效的时间戳格式");
		// 兼容秒级时间戳
		if (clientTs < 10_000_000_000L) clientTs *= 1000;
		long latency = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clientTs);
		return ApiResponse.Success(new {latency = $"{latency}ms"});
	});

	// GET /resource/download_url?type=live2d&name=arg-nori
	app.MapGet("/resource/download_url", (HttpContext context, OssService oss) =>
	{
		string type = context.Request.Query["type"].ToString();
		string name = context.Request.Query["name"].ToString();
		if (type.Length == 0) return ApiResponse.BadRequest("type 不能为空");
		if (name.Length == 0) return ApiResponse.BadRequest("name 不能为空");

		string? url;
		try
		{
			url = oss.GetSignedUrl(type, name);
		}
		catch (InvalidOperationException exception)
		{
			return ApiResponse.InternalServerError(exception.Message);
		}
		return url is null
			? ApiResponse.NotFound($"资源不存在: {type}/{name}.zip")
			: ApiResponse.Success(new {url});
	});

	// GET /resource/manifest?type=live2d&name=arg-nori
	app.MapGet("/resource/manifest", (HttpContext context, AssetManifestStore manifests) =>
	{
		string type = context.Request.Query["type"].ToString();
		string name = context.Request.Query["name"].ToString();
		if (type.Length == 0) return ApiResponse.BadRequest("type 不能为空");
		if (name.Length == 0) return ApiResponse.BadRequest("name 不能为空");
		try
		{
			AssetManifestItem? manifest = manifests.Find(type, name);
			return manifest is null
				? ApiResponse.NotFound($"资源 Manifest 不存在: {type}/{name}")
				: ApiResponse.Success(manifest);
		}
		catch (InvalidOperationException exception)
		{
			return ApiResponse.InternalServerError(exception.Message);
		}
	});

	// 未命中的路由: 与 Go 版一样返回统一信封而不是空 404
	app.MapFallback(() => ApiResponse.NotFound("资源未找到"));

	Log.Information("服务启动完成 port={Port}", config.Gateway.Port);
	app.Run();
	return 0;
}
catch (Exception exception)
{
	Log.Fatal(exception, "服务启动失败");
	return 1;
}
finally
{
	Log.CloseAndFlush();
}
