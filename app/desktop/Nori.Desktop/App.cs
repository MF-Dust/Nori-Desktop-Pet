using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Nori.Desktop.Diagnostics;

namespace Nori.Desktop;

/// <summary>桌面应用生命周期适配器；启动装配委托给 DesktopBootstrapper。</summary>
public sealed class App : Application
{
	private DesktopBootstrapper? _bootstrapper;

	public override void Initialize() => Styles.Add(new FluentTheme());

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			CrashReporter.Register(desktop);
			DesktopBootstrapper bootstrapper = new(this);
			_bootstrapper = bootstrapper;
			desktop.Exit += (_, _) => bootstrapper.RequestShutdown();
			_ = StartAsync(bootstrapper, desktop);
		}
		base.OnFrameworkInitializationCompleted();
	}

	private static async Task StartAsync(DesktopBootstrapper bootstrapper, IClassicDesktopStyleApplicationLifetime desktop)
	{
		try { await bootstrapper.StartAsync(desktop); }
		catch (OperationCanceledException) { }
		catch (Exception exception) { CrashReporter.Report(exception, critical: true); }
	}

	internal void ActivateMainWindow() => _bootstrapper?.ActivateMainWindow();
}
