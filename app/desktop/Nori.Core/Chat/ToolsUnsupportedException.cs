using System.Net;

namespace Nori.Core.Chat;

/// <summary>
/// LLM 端点明确拒绝了 tools/function calling 能力。
/// 只有这个类型允许 Agent 回退到 Nori JSON 协议；普通网络、鉴权、限流或模型错误绝不重试。
/// </summary>
public sealed class ToolsUnsupportedException : Exception
{
	public int? StatusCode { get; }

	public ToolsUnsupportedException(string message, int? statusCode = null, Exception? inner = null)
		: base(message, inner)
	{
		StatusCode = statusCode;
	}

	/// <summary>从供应商异常中识别“明确不支持工具”的错误。</summary>
	public static bool TryCreate(Exception exception, out ToolsUnsupportedException? result)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is ToolsUnsupportedException typed)
			{
				result = typed;
				return true;
			}

			int? status = current switch
			{
				HttpRequestException http when http.StatusCode is { } code => (int)code,
				_ => ReadStatusCode(current),
			};
			string message = current.Message.ToLowerInvariant();
			bool mentionsTool = message.Contains("tool", StringComparison.Ordinal)
				|| message.Contains("function call", StringComparison.Ordinal)
				|| message.Contains("function_call", StringComparison.Ordinal)
				|| message.Contains("tools", StringComparison.Ordinal);
			bool explicitlyUnsupported = message.Contains("not support", StringComparison.Ordinal)
				|| message.Contains("unsupported", StringComparison.Ordinal)
				|| message.Contains("unknown field", StringComparison.Ordinal)
				|| message.Contains("unrecognized field", StringComparison.Ordinal)
				|| message.Contains("invalid field", StringComparison.Ordinal)
				|| message.Contains("not allowed", StringComparison.Ordinal);
			bool statusAllowsCapabilityRejection = status is 400 or 404 or 405 or 422 or 501;
			if (mentionsTool && explicitlyUnsupported && (statusAllowsCapabilityRejection || current is NotSupportedException))
			{
				result = new ToolsUnsupportedException("当前 LLM 端点明确不支持原生工具调用", status, exception);
				return true;
			}
		}

		result = null;
		return false;
	}

	private static int? ReadStatusCode(Exception exception)
	{
		// 部分 SDK 的结果异常不继承 HttpRequestException；只读取公开的 StatusCode/Status 属性，
		// 不依据任意异常文本猜测 HTTP 状态。
		System.Reflection.PropertyInfo? property = exception.GetType().GetProperty("StatusCode")
			?? exception.GetType().GetProperty("Status");
		if (property?.GetValue(exception) is HttpStatusCode code) return (int)code;
		if (property?.GetValue(exception) is int number) return number;
		return null;
	}
}
