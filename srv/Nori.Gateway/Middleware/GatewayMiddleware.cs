using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace Nori.Gateway.Middleware;

/// <summary>
/// 上游信息
///
/// 对应 Go 版 internal/utils/upstream.go: 网关会把客户端信息以 X-/G- 头透传下来
/// </summary>
public sealed record UpstreamInfo(string RequestId, string Ip, string Country, string Region, string Origin, string UserAgent)
{
	/// <summary>
	/// 从请求头解析上游信息
	/// </summary>
	public static UpstreamInfo From(HttpRequest request) => new(
		RequestId: request.Headers["X-Request-ID"].ToString(),
		Ip: request.Headers["X-Real-IP"].ToString(),
		Country: request.Headers["G-Country-Long"].ToString(),
		Region: request.Headers["G-Region"].ToString(),
		Origin: request.Headers.Origin.ToString(),
		UserAgent: request.Headers.UserAgent.ToString());
}

/// <summary>
/// 请求中间件
/// </summary>
public static class GatewayMiddleware
{
	/// <summary>
	/// CORS: 与 Go 版同样的头白名单与 Origin 回显
	/// </summary>
	public static IApplicationBuilder UseGatewayCors(this IApplicationBuilder app) => app.Use(async (context, next) =>
	{
		StringValues origin = context.Request.Headers.Origin;
		// credentials: include 时不能用通配符 *
		context.Response.Headers.AccessControlAllowOrigin = origin.Count > 0 ? origin : "*";
		context.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE, OPTIONS";
		context.Response.Headers.AccessControlAllowHeaders = "Origin, Content-Type, Authorization, X-Timestamp, X-Nonce, X-Signature";
		context.Response.Headers.AccessControlExposeHeaders = "Content-Length";
		context.Response.Headers.AccessControlAllowCredentials = "true";
		if (HttpMethods.IsOptions(context.Request.Method))
		{
			context.Response.StatusCode = StatusCodes.Status200OK;
			return;
		}
		await next();
	});

	/// <summary>
	/// RequestID: 上游没带就生成一个, 并回写响应头
	/// </summary>
	public static IApplicationBuilder UseRequestId(this IApplicationBuilder app) => app.Use(async (context, next) =>
	{
		string id = context.Request.Headers["X-Request-ID"].ToString();
		if (id.Length == 0)
		{
			id = Guid.NewGuid().ToString("N");
			context.Request.Headers["X-Request-ID"] = id;
		}
		context.Response.Headers["X-Request-ID"] = id;
		await next();
	});

	/// <summary>
	/// 请求日志: 字段与 Go 版 middleware/logger.go 对齐
	/// </summary>
	public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) => app.Use(async (context, next) =>
	{
		long start = Stopwatch.GetTimestamp();
		await next();
		TimeSpan duration = Stopwatch.GetElapsedTime(start);
		UpstreamInfo info = UpstreamInfo.From(context.Request);
		ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HTTP");
		logger.LogInformation(
			"HTTP 请求 requestID={RequestId} ip={Ip} country={Country} region={Region} method={Method} path={Path} status={Status} origin={Origin} userAgent={UserAgent} durationNs={DurationNs} sizeBytes={SizeBytes}",
			info.RequestId, info.Ip, info.Country, info.Region,
			context.Request.Method, context.Request.Path, context.Response.StatusCode,
			info.Origin, info.UserAgent,
			(long)(duration.TotalMilliseconds * 1_000_000), context.Response.ContentLength ?? 0);
	});
}
