using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Nori.Gateway.Api;

/// <summary>
/// 统一响应信封
///
/// 与 Go 版 utils.Success / utils.Error 逐字节等价:
/// {"body": any, "error": bool, "message": string, "timestamp": 毫秒}
///
/// 注意字段顺序: Go 用的是 map[string]any, encoding/json 会按键名字母序输出,
/// 所以这里的属性声明顺序必须是 body → error → message → timestamp.
/// </summary>
public sealed record ApiResponse
{
	/// <summary>业务数据, 失败时为 null</summary>
	[JsonPropertyName("body")]
	public required object? Body { get; init; }

	/// <summary>是否出错</summary>
	[JsonPropertyName("error")]
	public required bool Error { get; init; }

	/// <summary>错误信息, 成功时为空串</summary>
	[JsonPropertyName("message")]
	public required string Message { get; init; }

	/// <summary>毫秒时间戳</summary>
	[JsonPropertyName("timestamp")]
	public required long Timestamp { get; init; }

	private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>
	/// 成功响应
	/// </summary>
	public static IResult Success(object? body) => Results.Json(new ApiResponse
	{
		Body = body,
		Error = false,
		Message = "",
		Timestamp = Now(),
	}, statusCode: StatusCodes.Status200OK);

	/// <summary>
	/// 错误响应
	/// </summary>
	public static IResult Failure(int status, string message) => Results.Json(new ApiResponse
	{
		Body = null,
		Error = true,
		Message = message,
		Timestamp = Now(),
	}, statusCode: status);

	/// <summary>请求参数有误</summary>
	public static IResult BadRequest(string message = "bad request") => Failure(StatusCodes.Status400BadRequest, message);

	/// <summary>资源不存在</summary>
	public static IResult NotFound(string message = "not found route") => Failure(StatusCodes.Status404NotFound, message);

	/// <summary>内部错误</summary>
	public static IResult InternalServerError(string message = "internal server error") => Failure(StatusCodes.Status500InternalServerError, message);
}
