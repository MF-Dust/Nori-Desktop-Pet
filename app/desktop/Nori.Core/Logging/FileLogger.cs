using System.Globalization;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;
using Nori.Core.Data;

namespace Nori.Core.Logging;

/// <summary>日志来源类型。</summary>
public enum LogSource
{
	/// <summary>前端调用 write_log 写入</summary>
	Frontend,

	/// <summary>宿主后端直接调用</summary>
	Backend,
}

/// <summary>
/// 文件日志。
/// NLog 负责按来源/日期滚动写盘，结构化 Queue 保留给调试页读取的最近 500 条。
/// </summary>
public sealed class FileLogger
{
	private const int RetentionDays = 7;
	private const int MaxMemoryEntries = 500;

	private readonly string _directory;
	private readonly LogLevel _minimumLevel;
	private readonly Lock _gate = new();
	private readonly Queue<LogEntry> _memory = new();
	private readonly LogFactory _factory;
	private readonly Logger _backend;
	private readonly Logger _frontend;
	private readonly MemoryTarget _recentTarget;

	public FileLogger(string? directory = null)
		: this(directory, "trace")
	{
	}

	/// <summary>创建带最低级别过滤的日志写入器。</summary>
	public FileLogger(string? directory, string minimumLevel)
	{
		_directory = directory ?? AppPaths.LogDir;
		_minimumLevel = ParseLevel(minimumLevel, LogLevel.Trace);
		Directory.CreateDirectory(_directory);

		_factory = new LogFactory();
		LoggingConfiguration configuration = new(_factory);
		FileTarget backendTarget = CreateTarget("backend");
		FileTarget frontendTarget = CreateTarget("frontend");
		_recentTarget = new MemoryTarget
		{
			Layout = "${message}",
			MaxLogsCount = MaxMemoryEntries,
		};
		configuration.AddTarget("backend", backendTarget);
		configuration.AddTarget("frontend", frontendTarget);
		configuration.AddTarget("recent", _recentTarget);
		configuration.AddRule(new LoggingRule("backend", _minimumLevel, LogLevel.Fatal, backendTarget));
		configuration.AddRule(new LoggingRule("frontend", _minimumLevel, LogLevel.Fatal, frontendTarget));
		configuration.AddRule(new LoggingRule("backend", _minimumLevel, LogLevel.Fatal, _recentTarget));
		configuration.AddRule(new LoggingRule("frontend", _minimumLevel, LogLevel.Fatal, _recentTarget));
		_factory.Configuration = configuration;
		_backend = _factory.GetLogger("backend");
		_frontend = _factory.GetLogger("frontend");
	}

	/// <summary>初始化日志目录并清理超过 7 天的日志。</summary>
	public void Initialize()
	{
		Directory.CreateDirectory(_directory);
		DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
		foreach (string path in Directory.EnumerateFiles(_directory, "*.log"))
		{
			try
			{
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

	/// <summary>写入一行日志。</summary>
	public void Write(LogSource source, string level, string message)
	{
		// Unknown levels from the bridge are deliberately normalized to Info;
		// an invalid threshold still defaults to Trace so logging is not silently
		// disabled by a malformed setting.
		LogLevel normalized = ParseLevel(level, LogLevel.Info);
		if (normalized.Ordinal < _minimumLevel.Ordinal) return;

		LogEntry entry = LogEntry.Create(source, normalized.Name.ToLowerInvariant(), message);
		lock (_gate)
		{
			_memory.Enqueue(entry);
			while (_memory.Count > MaxMemoryEntries) _memory.Dequeue();

			LogEventInfo eventInfo = new(normalized, source == LogSource.Backend ? _backend.Name : _frontend.Name, message);
			eventInfo.Properties["nori-time"] = entry.Time;
			eventInfo.Properties["nori-level"] = entry.Level;
			try
			{
				(source == LogSource.Backend ? _backend : _frontend).Log(eventInfo);
			}
			catch (IOException)
			{
				// 日志写入失败不能拖垮应用
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	/// <summary>读取内存缓冲快照。</summary>
	public IReadOnlyList<LogEntry> RecentLogs()
	{
		lock (_gate) return _memory.ToArray();
	}

	/// <summary>清空内存缓冲 (不动磁盘文件)。</summary>
	public void ClearRecentLogs()
	{
		lock (_gate)
		{
			_memory.Clear();
			_recentTarget.Logs.Clear();
		}
	}

	private FileTarget CreateTarget(string source) => new()
	{
		FileName = Path.Combine(_directory, $"{source}_${{shortdate}}.log"),
		Layout = "[${event-properties:item=nori-time}] [${event-properties:item=nori-level}] ${message}",
		KeepFileOpen = true,
		CreateDirs = true,
		AutoFlush = true,
		MaxArchiveDays = RetentionDays,
		Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
	};

	private static LogLevel ParseLevel(string? raw, LogLevel unknownLevel) => raw?.Trim().ToLowerInvariant() switch
	{
		"trace" => LogLevel.Trace,
		"debug" => LogLevel.Debug,
		"info" or "information" => LogLevel.Info,
		"warn" or "warning" => LogLevel.Warn,
		"error" => LogLevel.Error,
		"fatal" => LogLevel.Fatal,
		_ => unknownLevel,
	};
}
