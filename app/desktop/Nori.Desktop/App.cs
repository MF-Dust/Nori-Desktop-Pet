using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Nori.Core.Assets;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Resources;
using Nori.Desktop.Bridge;
using Nori.Desktop.Tray;
using Nori.Desktop.Windows;

namespace Nori.Desktop;

/// <summary>
/// 应用装配
///
/// 只做装配: 起服务、建窗口、挂托盘、决定首启走向. 业务逻辑都在 Nori.Core 与 Bridge 里.
/// </summary>
public sealed class App : Application
{
	private AppServices? _services;

	public override void Initialize() => Styles.Add(new FluentTheme());

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// 关掉最后一个窗口不退应用: 托盘常驻
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			desktop.Exit += async (_, _) =>
			{
				if (_services is not null) await _services.DisposeAsync();
			};
			_ = StartAsync(desktop);
		}
		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>
	/// 启动流程: 目录 → 日志 → 数据库 → 资源服务 → 窗口 → 托盘 → 窗口调度
	/// </summary>
	private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
	{
		bool devMode = Environment.GetEnvironmentVariable("NORI_DEV") == "1";

		AppPaths.EnsureCreated();

		FileLogger logger = new();
		logger.Initialize();
		logger.Write(LogSource.Backend, "info", "日志系统初始化完成");

		// WebView2 运行时缺失时给个能看懂的提示, 而不是弹四个空白窗口
		DetailedWebViewAdapterInfo adapter = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2);
		logger.Write(LogSource.Backend, "info", $"WebView2: installed={adapter.IsInstalled} version={adapter.Version}");
		if (!adapter.IsInstalled)
		{
			logger.Write(LogSource.Backend, "error", $"WebView2 运行时不可用: {adapter.UnavailableReason}");
			await Dispatcher.UIThread.InvokeAsync(() => ShowFatal("缺少 Microsoft Edge WebView2 运行时", "Nori 需要 WebView2 运行时才能显示界面。\n请安装 Microsoft Edge WebView2 Evergreen Runtime 后重试。", desktop));
			return;
		}

		NoriDatabase database = NoriDatabase.Open();
		ConfigStore config = new(database);
		config.InitDefaults(AppVersion());
		try
		{
			config.EnsureSchemaVersion();
		}
		catch (InvalidOperationException exception)
		{
			logger.Write(LogSource.Backend, "error", exception.Message);
			await Dispatcher.UIThread.InvokeAsync(() => ShowFatal("配置数据库版本过高", exception.Message, desktop));
			return;
		}
		logger.Write(LogSource.Backend, "info", $"数据库已打开: {AppPaths.DatabasePath}");

		HttpClientHandler httpHandler = new()
		{
			ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
		};
		HttpClient http = new(httpHandler);
		AssetServer assets = await AssetServer.StartAsync(new AssetServerOptions
		{
			AppRoot = AppRoot(),
			ResourcesRoot = AppPaths.ResourcesDir,
			DevMode = devMode,
		});
		logger.Write(LogSource.Backend, "info", $"资源服务已启动: {assets.Origin} (dev={devMode})");

		AppServices services = new()
		{
			Database = database,
			Config = config,
			Logger = logger,
			Resources = new ResourceManager(http),
			Chat = new ChatService(http, database, config),
			Memory = new Nori.Core.Memory.MemoryStore(database),
			Llm = new LlmClient(http),
			Assets = assets,
			Http = http,
		};
		_services = services;

		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			services.Windows = new WindowManager(assets, desktop);
			services.Commands = new BridgeCommands(services);
			NoriBridge bridge = new(services);
			services.Windows.CreateAll(bridge);

			TrayMenu.Install(this, services);

			// 首次启动显示向导, 否则直接进初始化窗口
			bool firstRun = config.IsFirstRun();
			logger.Write(LogSource.Backend, "info", firstRun ? "首次启动应用" : "应用启动完成");
			services.Windows.Show(firstRun ? WindowLabels.FirstRun : WindowLabels.Init);
		});
	}

	/// <summary>
	/// 致命错误提示窗: 说明原因并退出
	/// </summary>
	private static void ShowFatal(string title, string message, IClassicDesktopStyleApplicationLifetime desktop)
	{
		Window window = new()
		{
			Title = title,
			Width = 460,
			Height = 200,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			Content = new TextBlock
			{
				Text = message,
				Margin = new Thickness(24),
				TextWrapping = Avalonia.Media.TextWrapping.Wrap,
			},
		};
		window.Closed += (_, _) => desktop.Shutdown(1);
		window.Show();
	}

	/// <summary>
	/// 前端 bundle 目录
	///
	/// 生产: 与可执行文件同目录的 wwwroot; 开发模式下不使用 (页面由 vite 提供)
	/// </summary>
	private static string AppRoot() => Path.Combine(AppContext.BaseDirectory, "wwwroot");

	/// <summary>
	/// 应用版本, 写入 app_version 配置
	/// </summary>
	private static string AppVersion() =>
		Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}
