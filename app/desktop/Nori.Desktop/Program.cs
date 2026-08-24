using System;
using Avalonia;
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

	internal static bool ConsumePendingActivation() => Interlocked.Exchange(ref _activationPending, 0) == 1;

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
			Console.Error.WriteLine($"启动参数错误: {parseError}");
			Environment.ExitCode = 2;
			return;
		}

		Options = startup;
		bool safeMode = startup?.SafeMode == true;
		SmokeTestOptions? smokeTest = startup?.SmokeTest;
		if (smokeTest is not null)
		{
			try
			{
				AppPaths.UseDiagnosticProfile(smokeTest.Profile);
				SmokeTestRuntime.Configure(smokeTest);
			}
			catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
			{
				Console.Error.WriteLine($"冒烟 profile 不可用: {exception.Message}");
				Environment.ExitCode = 2;
				return;
			}
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
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	/// <summary>
	/// Avalonia 配置 (设计器也会用到, 不要删)
	/// </summary>
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.LogToTrace();
}
