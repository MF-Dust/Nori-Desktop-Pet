using System.Globalization;
using Nori.Core.Data;

namespace Nori.Core.Logging;

/// <summary>
/// 日志来源类型
/// </summary>
public enum LogSource
{
	/// <summary>前端调用 write_log 写入</summary>
	Frontend,

	/// <summary>宿主后端直接调用</summary>
	Backend,
}

/// <summary>
/// 文件日志
///
/// 对应 Rust 版 log.rs: 按来源与日期分文件, 启动时清理过期日志.
/// Rust 每次写入都重新 open+append, 这里加锁避免多线程并发写坏同一个文件.
/// </summary>
public sealed class FileLogger
{
	/// <summary>日志保留天数</summary>
	private const int RetentionDays = 7;

	private readonly string _directory;
	private readonly Lock _gate = new();

	public FileLogger(string? directory = null) => _directory = directory ?? AppPaths.LogDir;

	/// <summary>
	/// 初始化: 创建日志目录并清理过期日志. 单个文件删除失败不影响启动.
	/// </summary>
	public void Initialize()
	{
		Directory.CreateDirectory(_directory);
		DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
		foreach (string path in Directory.EnumerateFiles(_directory, "*.log"))
		{
			try
			{
				// 修改时间在未来的文件不删除, 与 Rust 版一致
				if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	/// <summary>
	/// 写入一行日志
	/// </summary>
	public void Write(LogSource source, string level, string message)
	{
		string line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] [{level}] {message}";
		try
		{
			lock (_gate)
			{
				Directory.CreateDirectory(_directory);
				File.AppendAllText(TodayLogFile(source), line + Environment.NewLine);
			}
		}
		catch (IOException)
		{
			// 日志写入失败不能拖垮应用
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	/// <summary>
	/// 今天的日志文件路径, 形如 backend_2026-01-01.log
	/// </summary>
	private string TodayLogFile(LogSource source)
	{
		string prefix = source == LogSource.Frontend ? "frontend" : "backend";
		string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return Path.Combine(_directory, $"{prefix}_{date}.log");
	}
}
