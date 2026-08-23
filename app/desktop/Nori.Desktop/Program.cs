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
	[STAThread]
	public static void Main(string[] args)
	{
		if (!SmokeTestOptions.TryParse(args, out SmokeTestOptions? smokeTest, out string parseError))
		{
			Console.Error.WriteLine($"启动参数错误: {parseError}");
			Environment.ExitCode = 2;
			return;
		}

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
		using SingleInstanceGuard? singleInstance = SingleInstanceGuard.TryAcquire(() =>
		{
			if (Application.Current is App app) app.ActivateMainWindow();
		});
		if (OperatingSystem.IsWindows() && singleInstance is null) return;
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	/// <summary>
	/// Avalonia 配置 (设计器也会用到, 不要删)
	/// </summary>
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.LogToTrace();
}
