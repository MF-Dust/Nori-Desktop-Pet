using System.Globalization;

namespace Nori.Core.Logging;

/// <summary>
/// 单条日志 (调试页内存缓冲与文件共用同一格式)
///
/// Time 用已格式化的文本: 跨桥接序列化给前端时无需再约定时区与格式.
/// </summary>
public sealed record LogEntry(string Time, string Level, LogSource Source, string Message)
{
	/// <summary>
	/// 从写入参数构造一条日志, 时间取当前时刻
	/// </summary>
	public static LogEntry Create(LogSource source, string level, string message) =>
		new(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), level, source, message);
}
