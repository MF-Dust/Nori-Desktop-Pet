using System.Text.Json;
using Nori.Core.Data;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Diagnostics;

/// <summary>
/// 发布产物启动冒烟模式的参数与检查点。
///
/// 该模式必须显式带独立 profile, 只用于 CI/维护者验证, 不会读取或修改真实用户目录。
/// </summary>
public sealed record SmokeTestOptions(SmokeTestMode Mode, string Profile)
{
	/// <summary>启动冒烟模式命令行解析。</summary>
	public static bool TryParse(IReadOnlyList<string> args, out SmokeTestOptions? options, out string error)
	{
		options = null;
		error = "";
		int smokeIndex = -1;
		for (int index = 0; index < args.Count; index++)
		{
			if (args[index].Equals("--smoke-test", StringComparison.Ordinal))
			{
				if (smokeIndex >= 0)
				{
					error = "--smoke-test 只能指定一次";
					return false;
				}
				smokeIndex = index;
			}
		}

		if (smokeIndex < 0) return true;
		if (smokeIndex + 1 >= args.Count || !TryParseMode(args[smokeIndex + 1], out SmokeTestMode mode))
		{
			error = "--smoke-test 必须是 first-run 或 initialized";
			return false;
		}

		string? profile = null;
		for (int index = smokeIndex + 2; index < args.Count; index++)
		{
			if (!args[index].Equals("--profile", StringComparison.Ordinal)) continue;
			if (profile is not null || index + 1 >= args.Count)
			{
				error = "--smoke-test 必须带且只能带一个 --profile <temp>";
				return false;
			}
			profile = args[++index];
		}

		if (string.IsNullOrWhiteSpace(profile))
		{
			error = "--smoke-test 必须带 --profile <temp>";
			return false;
		}

		try
		{
			string fullProfile = Path.GetFullPath(profile);
			if (Directory.Exists(fullProfile)
				&& (File.GetAttributes(fullProfile) & FileAttributes.ReparsePoint) != 0)
			{
				error = "--profile 不能是符号链接、junction 或 reparse point";
				return false;
			}
			if (Path.GetPathRoot(fullProfile)?.Equals(fullProfile, StringComparison.OrdinalIgnoreCase) == true)
			{
				error = "--profile 不能指向文件系统根目录";
				return false;
			}

			string databasePath = Path.Combine(fullProfile, "data", AppPaths.DatabaseFileName);
			if (File.Exists(databasePath))
			{
				error = "--profile 必须是隔离的临时目录, 不能包含已有 nori.db";
				return false;
			}
			Directory.CreateDirectory(fullProfile);
			string readinessPath = Path.Combine(fullProfile, "readiness.json");
			if (File.Exists(readinessPath)) File.Delete(readinessPath);
			options = new SmokeTestOptions(mode, fullProfile);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
		{
			error = $"--profile 不可用: {exception.Message}";
			return false;
		}
	}

	/// <summary>冒烟检查点文件路径。</summary>
	public string ReadinessPath => Path.Combine(Profile, "readiness.json");

	private static bool TryParseMode(string value, out SmokeTestMode mode)
	{
		mode = value switch
		{
			"first-run" => SmokeTestMode.FirstRun,
			"initialized" => SmokeTestMode.Initialized,
			_ => SmokeTestMode.FirstRun,
		};
		return value is "first-run" or "initialized";
	}
}

/// <summary>冒烟模式的运行时检查点写入与有界退出。</summary>
public static class SmokeTestRuntime
{
	private static readonly TimeSpan GracefulShutdownDelay = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan HardExitDelay = TimeSpan.FromSeconds(5);
	private static SmokeTestOptions? _current;

	/// <summary>当前冒烟配置; 普通启动时为 null。</summary>
	public static SmokeTestOptions? Current => _current;

	/// <summary>安装冒烟配置, 只能在进程启动阶段调用一次。</summary>
	public static void Configure(SmokeTestOptions options)
	{
		if (_current is not null) throw new InvalidOperationException("冒烟模式只能配置一次");
		_current = options;
	}

	/// <summary>
	/// 写入原子 JSON readiness checkpoint。
	///
	/// 只有资源服务、窗口和配置数据库都已经装配后才会调用, 因此它代表宿主已就绪而非仅仅进程已启动。
	/// </summary>
	public static void WriteReady(SmokeTestOptions options, bool firstRun, bool safeMode = false)
	{
		var checkpoint = new
		{
			schema_version = 2,
			status = "ready",
			product_version = Nori.Core.ProductVersion.Current,
			database_schema_version = NoriDatabase.DatabaseSchemaVersion,
			config_schema_version = Nori.Core.Configuration.ConfigStore.ConfigSchemaVersion,
			mode = options.Mode == SmokeTestMode.FirstRun ? "first-run" : "initialized",
			safe_mode = safeMode,
			first_run = firstRun,
			initial_window = firstRun ? "first-run" : "init",
			data_dir = AppPaths.DataDir,
			asset_server = "ready",
		};
		string temporaryPath = options.ReadinessPath + ".tmp";
		string json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions {WriteIndented = true}) + Environment.NewLine;
		File.WriteAllText(temporaryPath, json);
		File.Move(temporaryPath, options.ReadinessPath, true);
	}

	/// <summary>检查点写入后自动退出, 防止 CI 遗留 GUI 进程。</summary>
	public static void ScheduleBoundedExit(IWindowManager windowManager)
	{
		_ = ExitAfterCheckpointAsync(windowManager);
	}

	private static async Task ExitAfterCheckpointAsync(IWindowManager windowManager)
	{
		await Task.Delay(GracefulShutdownDelay).ConfigureAwait(false);
		try { windowManager.Shutdown(); } catch { }

		// CI 的无头桌面环境可能卡住原生窗口退出; 冒烟 profile 是隔离的一次性目录,
		// 因此在等待正常清理后保留进程内硬退出兜底, 外部脚本仍有更长的 watchdog。
		await Task.Delay(HardExitDelay).ConfigureAwait(false);
		Environment.Exit(0);
	}
}

/// <summary>允许冒烟模式验证两个启动分支。</summary>
public enum SmokeTestMode
{
	/// <summary>空 profile 的首次运行分支。</summary>
	FirstRun,

	/// <summary>由 harness 在隔离 profile 中预置完成标记的初始化分支。</summary>
	Initialized,
}
