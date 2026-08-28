using System;
using System.Runtime.InteropServices;
using Avalonia;
using Nori.Core;
using Nori.Core.Data;
using Nori.Desktop.Diagnostics;
using Nori.Desktop.Startup;

namespace Nori.Desktop;

/// <summary>
/// 进程入口
/// </summary>
internal static class Program
{
	private static int _activationPending;

	internal static StartupOptions? Options { get; private set; }
	internal static AppStoragePaths? StoragePaths { get; private set; }
	internal static StorageBootstrapResult? StorageMigration { get; private set; }

	internal static bool ConsumePendingActivation() => Interlocked.Exchange(ref _activationPending, 0) == 1;

	private static string RuntimeRid()
	{
		string rid = RuntimeInformation.RuntimeIdentifier;
		if (rid.StartsWith("win-", StringComparison.Ordinal) || rid.StartsWith("linux-", StringComparison.Ordinal) || rid.StartsWith("osx-", StringComparison.Ordinal)) return rid;
		string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
		string architecture = RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => throw new InvalidOperationException("不支持的 CPU 架构"),
		};
		return $"{os}-{architecture}";
	}

	private static void ShowStartupError(string title, string message)
	{
		string safe = Nori.Core.Security.SensitiveDataRedactor.Redact(message);
		if (OperatingSystem.IsWindows())
		{
			MessageBox(nint.Zero, safe, title, 0x10);
			return;
		}
		if (OperatingSystem.IsMacOS())
		{
			try
			{
				System.Diagnostics.ProcessStartInfo alert = new("osascript") { UseShellExecute = false };
				alert.ArgumentList.Add("-e");
				alert.ArgumentList.Add($"display alert {AppleScriptString(title)} message {AppleScriptString(safe)}");
				using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(alert);
				if (process is null) throw new InvalidOperationException("无法显示启动错误");
				process.WaitForExit(5000);
				return;
			}
			catch { }
		}
		Console.Error.WriteLine($"{title}: {safe}");
	}

	private static string AppleScriptString(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + "\"";

	[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int MessageBox(nint hWnd, string text, string caption, uint type);

	private static bool IsDevelopmentProcess() =>
		Environment.GetEnvironmentVariable("NORI_DEV") == "1"
		|| !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NORI_DEV_PACKAGE_ROOT"))
		|| Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase);

	private static void ActivateFirstInstance()
	{
		if (Application.Current is App app) app.ActivateMainWindow();
		else Interlocked.Exchange(ref _activationPending, 1);
	}

	[STAThread]
	public static void Main(string[] args)
	{
		if (!StartupOptions.TryParse(args, out StartupOptions? startup, out string parseError))
		{
			ShowStartupError("启动参数错误", parseError);
			Environment.ExitCode = 2;
			return;
		}

		Options = startup;
		bool safeMode = startup?.SafeMode == true;
		SmokeTestOptions? smokeTest = startup?.SmokeTest;
		try
		{
			StoragePaths = smokeTest is null
				? AppStoragePathResolver.Resolve()
				: new AppStoragePaths(smokeTest.Profile);
			if (smokeTest is not null)
			{
				SmokeTestRuntime.Configure(smokeTest);
			}
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			ShowStartupError("存储包根不可用", exception.Message);
			Environment.ExitCode = 2;
			return;
		}

		// 在 Avalonia 启动前就挂上域级兜底, 尽早覆盖启动期异常 (参考 ClassIsland Program.cs)
		CrashReporter.RegisterDomainHandler();
		// 冒烟使用隔离 profile，不能被用户正在运行的正式实例互斥量拦截。
		using SingleInstanceGuard? singleInstance = smokeTest is null
			? SingleInstanceGuard.TryAcquire(ActivateFirstInstance, signalExisting: !safeMode)
			: null;
		if (smokeTest is null && OperatingSystem.IsWindows() && singleInstance is null)
		{
			if (safeMode)
			{
				Console.Error.WriteLine("安全模式无法启动: Nori 已有一个实例正在运行");
				Environment.ExitCode = 3;
			}
			return;
		}
		try
		{
			AppStoragePaths paths = StoragePaths ?? throw new InvalidOperationException("存储路径尚未初始化");
			bool development = IsDevelopmentProcess();
			string? legacy = development || smokeTest is not null ? null : LegacyDataPathResolver.Resolve();
			StorageMigration = StorageBootstrapper.Bootstrap(
				paths,
				ProductVersion.Current,
				RuntimeRid(),
				legacy,
				allowLegacyMigration: !development && smokeTest is null);
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
		{
			ShowStartupError("存储初始化失败", exception.Message);
			Environment.ExitCode = 1;
		}
	}

	/// <summary>
	/// Avalonia 配置 (设计器也会用到, 不要删)
	/// </summary>
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.LogToTrace();
}
