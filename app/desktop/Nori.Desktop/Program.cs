using System;
using Avalonia;

namespace Nori.Desktop;

/// <summary>
/// 进程入口
/// </summary>
internal static class Program
{
	[STAThread]
	public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

	/// <summary>
	/// Avalonia 配置 (设计器也会用到, 不要删)
	/// </summary>
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
		.LogToTrace();
}
