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

	// 常驻写入器: 每条日志都重新开关一次文件在高频路径 (Cubism 告警 / 前端 write_log)
	// 上是纯开销, 改为按来源各持一个追加写入器, 跨天时滚动重建
	private StreamWriter? _backendWriter;
	private StreamWriter? _frontendWriter;
	private string _backendFileDate = "";
	private string _frontendFileDate = "";

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
				StreamWriter writer = GetWriter(source);
				writer.WriteLine(line);
				writer.Flush();
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
	/// 取指定来源的常驻写入器, 跨天时滚动到新文件
	/// </summary>
	private StreamWriter GetWriter(LogSource source)
	{
		string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		if (source == LogSource.Frontend)
		{
			if (_frontendWriter is null || _frontendFileDate != today)
			{
				_frontendWriter?.Dispose();
				_frontendWriter = CreateWriter("frontend", today);
				_frontendFileDate = today;
			}
			return _frontendWriter;
		}

		if (_backendWriter is null || _backendFileDate != today)
		{
			_backendWriter?.Dispose();
			_backendWriter = CreateWriter("backend", today);
			_backendFileDate = today;
		}
		return _backendWriter;
	}

	/// <summary>
	/// 创建今天的日志写入器, 形如 backend_2026-01-01.log
	/// </summary>
	private StreamWriter CreateWriter(string prefix, string today)
	{
		Directory.CreateDirectory(_directory);
		return new StreamWriter(Path.Combine(_directory, $"{prefix}_{today}.log"), append: true);
	}
}
