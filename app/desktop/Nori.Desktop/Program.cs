using System;
using Avalonia;
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
		// 在 Avalonia 启动前就挂上域级兑底, 尽早覆盖启动期异常 (参考 ClassIsland Program.cs)
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
