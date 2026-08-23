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
using Nori.Core.Network;
using Nori.Core.Resources;
using Nori.Core.Telemetry;
using Nori.Desktop.Bridge;
using Nori.Desktop.Diagnostics;
using Nori.Desktop.Telemetry;
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
	private NoriDatabase? _startupDatabase;
	private SentryTelemetry? _startupTelemetry;
	private NoriHttpClients? _startupHttpClients;
	private AssetServer? _startupAssets;
	private Nori.Core.Mcp.McpManager? _startupMcp;
	private Task? _shutdownTask;
	private readonly CancellationTokenSource _shutdownCts = new();
	private int _shutdownStarted;
	private int _secondInstanceActivationPending;

	public override void Initialize() => Styles.Add(new FluentTheme());

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// 关掉最后一个窗口不退应用: 托盘常驻
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			CrashReporter.Register(desktop); // UI 线程与任务级异常兜底
			desktop.Exit += (_, _) =>
			{
				_shutdownCts.Cancel();
				if (Interlocked.CompareExchange(ref _shutdownStarted, 1, 0) == 0)
					_shutdownTask = ShutdownAsync();
				try { _shutdownTask?.GetAwaiter().GetResult(); }
				catch { /* exit must continue even when cleanup fails */ }
			};
			_ = StartAsync(desktop);
		}
		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>
	/// 启动流程: 目录 → 日志 → 数据库 → 资源服务 → 窗口 → 托盘 → 窗口调度
	///
	/// 整个流程包在 try/catch 里: 这里是 fire-and-forget 调用, 异常不兜底的话
	/// 只会留下一个无窗口无提示的僵尸进程。
	/// </summary>
	private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
	{
		try
		{
			await StartAsyncCore(desktop, _shutdownCts.Token);
		}
		catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
		{
			// 应用在启动完成前退出时, 由 Exit 事件负责清理已创建的资源。
		}
		catch (Exception exception)
		{
			// 记日志与崩溃窗展示都在 Report 内部完成 (critical: 关窗即退出码 1)
			CrashReporter.Report(exception, critical: true);
		}
	}

	private async Task StartAsyncCore(IClassicDesktopStyleApplicationLifetime desktop, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		bool devMode = Environment.GetEnvironmentVariable("NORI_DEV") == "1";

		AppPaths.EnsureCreated();

		FileLogger logger = new();
		logger.Initialize();
		CrashReporter.AttachLogger(logger); // 兜底日志与应用共用同一个写入器
		logger.Write(LogSource.Backend, "info", "日志系统初始化完成");

		// 先挂接但保持关闭。数据库中的明确同意状态读取完成前, Native Sentry 不得初始化,
		// 这样 WebView/数据库探测等启动失败始终只留在本机。
		SentryTelemetry telemetry = new(SentryBuildConfig.NativeDsn, SentryBuildConfig.Release, SentryBuildConfig.Environment);
		_startupTelemetry = telemetry;
		CrashReporter.AttachTelemetry(telemetry);

		// WebView 运行时缺失时给个能看懂的提示, 而不是弹几个空白窗口。
		// 三平台各自的原生引擎: Windows→WebView2, macOS→WKWebView, Linux→WebKitGTK
		(WebViewAdapterType adapterType, string engineName, string installHint) = OperatingSystem.IsWindows()
			? (WebViewAdapterType.WebView2, "WebView2", "请安装 Microsoft Edge WebView2 Evergreen Runtime 后重试。")
			: OperatingSystem.IsMacOS()
				? (WebViewAdapterType.WkWebView, "WKWebView", "系统 WebKit 组件异常, 请确认 macOS 版本受支持。")
				: (WebViewAdapterType.WebKitGtk, "WebKitGTK", "请安装 WebKitGTK 运行时 (Debian/Ubuntu: libwebkit2gtk-4.1-0; Fedora: webkit2gtk4.1)。");

		DetailedWebViewAdapterInfo adapter = WebViewAdapterInfo.GetAdapterInfo(adapterType);
		logger.Write(LogSource.Backend, "info", $"{engineName}: installed={adapter.IsInstalled} version={adapter.Version}");
		if (!adapter.IsInstalled)
		{
			logger.Write(LogSource.Backend, "error", $"{engineName} 运行时不可用: {adapter.UnavailableReason}");
			CrashReporter.ReportStartupFatal($"缺少 {engineName} 运行时", $"Nori 需要 {engineName} 才能显示界面。{Environment.NewLine}{installHint}");
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();
		NoriDatabase database = NoriDatabase.Open();
		_startupDatabase = database;
		ConfigStore config = new(database);
		try
		{
			config.InitDefaults(AppVersion());
			config.EnsureSchemaVersion();
		}
		catch (InvalidOperationException exception)
		{
			logger.Write(LogSource.Backend, "error", exception.Message);
			CrashReporter.ReportStartupFatal("配置数据库版本过高", exception.Message);
			return;
		}
		// 只有完成配置迁移并确认 consent=granted 后才允许初始化 Native Sentry。
		telemetry.Configure(config.GetTelemetryConsent() == TelemetryConsent.Granted);
		using ITelemetryTransaction startupTransaction = telemetry.StartTransaction("app.startup");
		logger.Write(LogSource.Backend, "info", "数据库已打开");

		// 默认校验服务器证书。自签名/私有部署的大模型端点可通过 allow_insecure_tls 显式放开。
		bool insecureTls = ParseBoolFlag(config.GetStringOr("allow_insecure_tls", "")) ?? false;
		NoriHttpClients httpClients = NoriHttpClients.Create(
			insecureTls,
			TimeSpan.FromSeconds(ChatService.TimeoutSeconds + 10));
		_startupHttpClients = httpClients;
		HttpClient http = httpClients.Local;
		HttpClient publicHttp = httpClients.Public;
		if (insecureTls)
		{
			logger.Write(LogSource.Backend, "warn", "已启用 allow_insecure_tls: 出站 HTTPS 不再校验服务器证书, 仅建议对本地/自签名端点使用");
		}
		cancellationToken.ThrowIfCancellationRequested();
		AssetServer assets = await AssetServer.StartAsync(new AssetServerOptions
		{
			AppRoot = AppRoot(),
			ResourcesRoot = AppPaths.ResourcesDir,
			DevMode = devMode,
		});
		_startupAssets = assets;
		logger.Write(LogSource.Backend, "info", $"资源服务已启动: {assets.Origin} (dev={devMode})");

		Nori.Core.Mcp.McpManager mcp = new(http, config);
		_startupMcp = mcp;

		AppServices services = new()
		{
			Database = database,
			Config = config,
			Logger = logger,
			Telemetry = telemetry,
			Resources = new ResourceManager(),
			Chat = new ChatService(http, database, config),
			Memory = new Nori.Core.Memory.MemoryStore(database),
			Embedding = new Nori.Core.Embedding.OpenAiEmbeddingAdapter(http),
			Llm = new LlmClient(http),
			Mcp = mcp,
			Assets = assets,
			Http = http,
			PublicHttp = publicHttp,
			AgentOperations = new Bridge.AgentOperationRegistry(),
			ShutdownToken = _shutdownCts.Token,
		};
		_services = services;
		_startupDatabase = null;
		_startupTelemetry = null;
		_startupHttpClients = null;
		_startupAssets = null;
		_startupMcp = null;

		// 异步自动连接已启用的 MCP 服务; 后台任务失败只记日志, 不崩进程
		CrashReporter.Forget(mcp.AutoConnectEnabledAsync(), "MCP 自动连接");

		cancellationToken.ThrowIfCancellationRequested();
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			services.Windows = new WindowManager(assets, desktop);
			services.Commands = new BridgeCommands(services);
			NoriBridge bridge = new(services);
			services.Bridge = bridge;
			services.Windows.CreateAll(bridge, services);

			// 业务运行时 (Agent/技能/情绪/提醒/语音): 桌宠窗口就绪后启动,
			// 桥接命令通过 services.Runtime 访问
			Runtime.AppRuntime runtime = new(services);
			services.Runtime = runtime;
			runtime.Start();

			// 托盘失败 (常见于部分 Linux 桌面) 时前端要显示内建入口
			runtime.TrayAvailable = TrayMenu.Install(this, services);

			// 首次启动显示向导, 否则直接进初始化窗口
			bool firstRun = config.IsFirstRun();
			logger.Write(LogSource.Backend, "info", firstRun ? "首次启动应用" : "应用启动完成");
			if (Interlocked.Exchange(ref _secondInstanceActivationPending, 0) == 1)
				services.Windows.Show(WindowLabels.Main);
			else
				services.Windows.Show(firstRun ? WindowLabels.FirstRun : WindowLabels.Init);
		});
	}

	/// <summary>
	/// 响应第二个实例的激活请求。
	///
	/// 单实例监听线程不碰 Avalonia 对象, 这里只把请求切回 UI 线程；窗口尚未装配时
	/// 先记住请求, 等启动流程创建窗口后再显示 main。
	/// </summary>
	internal void ActivateMainWindow()
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(ActivateMainWindow);
			return;
		}

		if (_services?.Windows is { } windows)
		{
			windows.Show(WindowLabels.Main);
			return;
		}
		Interlocked.Exchange(ref _secondInstanceActivationPending, 1);
	}

	private async Task ShutdownAsync()
	{
		Task cleanup = ShutdownCoreAsync();
		await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(8))).ConfigureAwait(false);
	}

	private async Task ShutdownCoreAsync()
	{
		try
		{
			if (_services is not null) await _services.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			if (_startupMcp is not null)
			{
				try { await _startupMcp.DisposeAsync().ConfigureAwait(false); } catch { }
				_startupMcp = null;
			}
			if (_startupAssets is not null)
			{
				try { await _startupAssets.DisposeAsync().ConfigureAwait(false); } catch { }
				_startupAssets = null;
			}
			try { _startupHttpClients?.Dispose(); } catch { }
			_startupHttpClients = null;
			try { _startupDatabase?.Dispose(); } catch { }
			_startupDatabase = null;
			if (_startupTelemetry is not null)
			{
				try { await _startupTelemetry.FlushAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
				try { _startupTelemetry.Dispose(); } catch { }
				_startupTelemetry = null;
			}
		}
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

	/// <summary>
	/// 解析布尔配置文本, 与 PetRuntime.ParseBool 同口径
	/// </summary>
	private static bool? ParseBoolFlag(string raw) => raw switch
	{
		"1" => true,
		"0" => false,
		_ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
		_ when raw.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
		_ => null,
	};
}
