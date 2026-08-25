using Nori.Core.Automation;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Desktop.Automation.Browser;
using Nori.Desktop.Automation.Desktop;
using Nori.Desktop.Automation.Windows;

namespace Nori.Desktop.Automation;

/// <summary>可注入的浏览器自动化运行器；测试实现不得把真实浏览器拉起。</summary>
public interface IAutomationBrowserRunner : IAsyncDisposable
{
	/// <summary>启动一个隔离的浏览器会话。</summary>
	Task StartAsync(CancellationToken cancellationToken = default);
}

/// <summary>浏览器自动化宿主状态。</summary>
public enum AutomationBrowserState
{
	/// <summary>没有浏览器会话。</summary>
	Stopped,
	/// <summary>正在启动浏览器。</summary>
	Starting,
	/// <summary>浏览器会话已启动。</summary>
	Running,
	/// <summary>最近一次启动失败。</summary>
	Failed,
}

/// <summary>自动化设置的脱敏快照。</summary>
public sealed record AutomationSettingsSnapshot(
	bool Enabled,
	bool AllowPointer,
	bool AllowKeyboard,
	bool AllowScroll)
{
	/// <summary>浏览器自动化显式开关。</summary>
	public bool BrowserEnabled { get; init; }
}

/// <summary>宿主当前显式授权的自动化能力。</summary>
public sealed record AutomationCapabilitiesSnapshot(
	bool IsWindows,
	bool VisionAvailable,
	bool Pointer,
	bool Keyboard,
	bool Scroll,
	string? UnavailableReason)
{
	/// <summary>桌面视觉自动化能力是否已满足平台、配置和显式开关。</summary>
	public bool Desktop { get; init; }

	/// <summary>当前聊天 Provider 是否具备可调用的多模态规划接线。</summary>
	public bool VisionReady { get; init; }

	/// <summary>浏览器自动化能力是否已满足平台和显式开关。</summary>
	public bool Browser { get; init; }
}

/// <summary>浏览器自动化的脱敏生命周期状态；不包含 Cookie、页面内容、地址或截图。</summary>
public sealed record AutomationBrowserStatusSnapshot(
	AutomationBrowserState State,
	bool Enabled,
	bool Available,
	string? UnavailableReason)
{
	/// <summary>当前是否持有浏览器会话。</summary>
	public bool Running => State == AutomationBrowserState.Running;
}

/// <summary>自动化视觉探测结果；不会包含截图或请求正文。</summary>
public sealed record AutomationVisionProbeSnapshot(bool Available, string? Reason);

/// <summary>自动化任务的脱敏状态；只包含生命周期和稳定进度分类。</summary>
public sealed record AutomationTaskStatusSnapshot(
	Guid Id,
	AutomationTaskState State,
	int Step,
	string ProgressCategory,
	string? ErrorCategory);

/// <summary>自动化运行时汇总；不包含截图、提示词、URL 或工具参数。</summary>
public sealed record AutomationSnapshot(
	bool Enabled,
	bool Available,
	string? UnavailableReason,
	AutomationSettingsSnapshot Settings,
	AutomationCapabilitiesSnapshot Capabilities,
	AutomationTaskStatusSnapshot? ActiveTask,
	int QueuedCount)
{
	/// <summary>最近的桌面视觉任务脱敏状态。</summary>
	public IReadOnlyList<AutomationTaskStatusSnapshot> Tasks { get; init; } = [];

	/// <summary>当前等待用户决定的桌面高风险动作；不包含输入正文。</summary>
	public IReadOnlyList<AutomationDesktopApprovalSnapshot> PendingApprovals { get; init; } = [];

	/// <summary>浏览器自动化脱敏状态。</summary>
	public AutomationBrowserStatusSnapshot Browser { get; init; } = new(
		AutomationBrowserState.Stopped,
		false,
		false,
		"浏览器自动化默认关闭，请先显式启用");
}

/// <summary>把 Playwright Edge 运行器适配到自动化生命周期抽象。</summary>
public sealed class PlaywrightEdgeBrowserAutomationRunner : IAutomationBrowserRunner
{
	private readonly PlaywrightEdgeBrowserRunner _runner = new();
	private PlaywrightEdgeBrowserSession? _session;
	private int _disposed;

	/// <summary>启动可见的隔离 Edge 会话。</summary>
	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		_session = await _runner.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>关闭页面、Edge 上下文及临时 profile。</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		PlaywrightEdgeBrowserSession? session = Interlocked.Exchange(ref _session, null);
		try
		{
			if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			await _runner.DisposeAsync().ConfigureAwait(false);
		}
	}
}

/// <summary>
/// 自动化宿主接线层。
///
/// 装配任务生命周期、Windows adapter 能力探测和隔离 Edge 生命周期。所有执行入口
/// 都经过安全模式、平台、总开关和显式能力授权校验；浏览器状态只返回脱敏摘要。
/// </summary>
public sealed class AutomationRuntime : IAsyncDisposable
{
	private readonly ConfigStore _config;
	private readonly AiSettingsStore _aiSettings;
	private readonly AutomationTaskManager _tasks;
	private readonly bool _safeMode;
	private readonly bool _isWindows;
	private readonly bool _visionAvailable;
	private readonly Func<IAutomationBrowserRunner> _browserRunnerFactory;
	private readonly Func<DesktopVisionRunnerRequest, IAutomationTaskRunner>? _desktopVisionRunnerFactory;
	private readonly Func<IDesktopVisionPlanner>? _desktopVisionPlannerFactory;
	private readonly Func<IDesktopVisionActionExecutor>? _desktopVisionActionFactory;
	private readonly Func<IDesktopVisionScreenshotSource>? _desktopVisionScreenshotFactory;
	private readonly Func<IDesktopVisionWindowCatalog>? _desktopVisionWindowCatalogFactory;
	private readonly object _desktopStateGate = new();
	private readonly Dictionary<Guid, DesktopTaskState> _desktopTasks = [];
	private readonly Dictionary<string, DesktopWindowTarget> _desktopWindowTargets = new(StringComparer.Ordinal);
	private readonly Dictionary<Guid, AutomationDesktopApprovalSnapshot> _desktopApprovals = [];
	private DesktopVisionApprovalCallback? _desktopVisionApprovalCallback;
	private readonly SemaphoreSlim _browserGate = new(1, 1);
	private readonly object _browserStateGate = new();
	private IAutomationBrowserRunner? _browserRunner;
	private AutomationBrowserState _browserState = AutomationBrowserState.Stopped;
	private string? _browserFailureCode;
	private int _disposed;

	private const int MaxPublicDesktopTasks = 32;

	/// <summary>自动化状态发生变化时触发；订阅者只能读取脱敏快照。</summary>
	public event Action? Changed;

	/// <summary>按宿主实际平台装配自动化运行时。</summary>
	public AutomationRuntime(ConfigStore config, bool safeMode, AutomationTaskManager? taskManager = null)
		: this(config, safeMode, OperatingSystem.IsWindows(), visionAvailable: false, taskManager, null)
	{
	}

	/// <summary>可注入平台、视觉能力和浏览器运行器的构造函数，供宿主边界测试使用。</summary>
	public AutomationRuntime(
		ConfigStore config,
		bool safeMode,
		bool isWindows,
		bool visionAvailable,
		AutomationTaskManager? taskManager = null,
		Func<IAutomationBrowserRunner>? browserRunnerFactory = null,
		ChatService? chatService = null,
		Func<DesktopVisionRunnerRequest, IAutomationTaskRunner>? desktopVisionRunnerFactory = null,
		Func<IDesktopVisionPlanner>? desktopVisionPlannerFactory = null,
		Func<IDesktopVisionActionExecutor>? desktopVisionActionFactory = null,
		Func<IDesktopVisionScreenshotSource>? desktopVisionScreenshotFactory = null,
		Func<IDesktopVisionWindowCatalog>? desktopVisionWindowCatalogFactory = null,
		DesktopVisionApprovalCallback? desktopVisionApprovalCallback = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		_config = config;
		_aiSettings = new AiSettingsStore(config);
		_safeMode = safeMode;
		_isWindows = isWindows;
		_visionAvailable = visionAvailable;
		_tasks = taskManager ?? new AutomationTaskManager();
		_browserRunnerFactory = browserRunnerFactory ?? (() => new PlaywrightEdgeBrowserAutomationRunner());

		// 安全模式装配时不保留任何桌面视觉外部依赖工厂；Bridge 和执行入口仍会再次拒绝。
		if (safeMode)
		{
			_desktopVisionRunnerFactory = null;
			_desktopVisionPlannerFactory = null;
			_desktopVisionActionFactory = null;
			_desktopVisionScreenshotFactory = null;
			_desktopVisionWindowCatalogFactory = null;
			_desktopVisionApprovalCallback = null;
		}
		else
		{
			_desktopVisionRunnerFactory = desktopVisionRunnerFactory ?? DefaultDesktopVisionRunnerFactory;
			_desktopVisionPlannerFactory = desktopVisionPlannerFactory
				?? (chatService is null ? null : () => new ChatServiceDesktopVisionPlanner(chatService, _aiSettings));
			_desktopVisionActionFactory = desktopVisionActionFactory ?? (() => new WindowsDesktopVisionActionExecutor());
			_desktopVisionScreenshotFactory = desktopVisionScreenshotFactory ?? (() => new WindowsDesktopVisionScreenshotSource());
			_desktopVisionWindowCatalogFactory = desktopVisionWindowCatalogFactory ?? (() => new WindowsDesktopVisionWindowCatalog());
			_desktopVisionApprovalCallback = desktopVisionApprovalCallback;
		}

		_tasks.TaskChanged += OnTaskChanged;
	}

	/// <summary>运行时装配完成后可接入宿主现有审批协调器；空值始终拒绝高风险动作。</summary>
	public DesktopVisionApprovalCallback? DesktopVisionApprovalCallback
	{
		get => _desktopVisionApprovalCallback;
		set
		{
			if (_safeMode) return;
			_desktopVisionApprovalCallback = value;
		}
	}

	/// <summary>读取当前设置。</summary>
	public AutomationSettingsSnapshot GetSettings() => new(
		_config.GetBoolOr(ConfigStore.KeyAutomationEnabled, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowPointer, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowKeyboard, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowScroll, false))
	{
		BrowserEnabled = _config.GetBoolOr(ConfigStore.KeyAutomationBrowserEnabled, false),
	};

	/// <summary>读取当前可用能力；能力必须由配置显式授权。</summary>
	public AutomationCapabilitiesSnapshot GetCapabilities()
	{
		AutomationSettingsSnapshot settings = GetSettings();
		bool visionReady = IsVisionConfigurationValid();
		return new(
			_isWindows,
			visionReady,
			_isWindows && settings.Enabled && settings.AllowPointer,
			_isWindows && settings.Enabled && settings.AllowKeyboard,
			_isWindows && settings.Enabled && settings.AllowScroll,
			GetUnavailableReason(settings))
		{
			Desktop = IsExecutionAvailable(settings),
			VisionReady = visionReady,
			Browser = IsBrowserExecutionAvailable(settings),
		};
	}

	/// <summary>读取当前浏览器的脱敏生命周期状态。</summary>
	public AutomationBrowserStatusSnapshot GetBrowserStatus() => GetBrowserStatus(GetSettings());

	/// <summary>读取自动化脱敏汇总。</summary>
	public AutomationSnapshot GetSnapshot()
	{
		AutomationSettingsSnapshot settings = GetSettings();
		AutomationCapabilitiesSnapshot capabilities = GetCapabilities();
		return new(
			settings.Enabled,
			IsExecutionAvailable(settings),
			GetUnavailableReason(settings),
			settings,
			capabilities,
			ToStatus(_tasks.ActiveTask),
			_tasks.QueuedCount)
		{
			Browser = GetBrowserStatus(settings),
			Tasks = GetDesktopTaskSnapshots(),
			PendingApprovals = GetDesktopApprovalSnapshots(),
		};
	}

	/// <summary>更新显式授权设置；未提供的字段保持原值。</summary>
	public AutomationSettingsSnapshot UpdateSettings(
		bool? enabled,
		bool? allowPointer,
		bool? allowKeyboard,
		bool? allowScroll,
		bool? browserEnabled = null)
	{
		ThrowIfControlBlocked();
		if (enabled is { } enabledValue) SetBool(ConfigStore.KeyAutomationEnabled, enabledValue);
		if (allowPointer is { } pointerValue) SetBool(ConfigStore.KeyAutomationAllowPointer, pointerValue);
		if (allowKeyboard is { } keyboardValue) SetBool(ConfigStore.KeyAutomationAllowKeyboard, keyboardValue);
		if (allowScroll is { } scrollValue) SetBool(ConfigStore.KeyAutomationAllowScroll, scrollValue);
		if (browserEnabled is { } browserValue) SetBool(ConfigStore.KeyAutomationBrowserEnabled, browserValue);

		// 任一桌面能力或总开关被收回后，已有任务也必须立即取消，不能只阻止下一次启动。
		if (enabled == false || allowPointer == false || allowKeyboard == false || allowScroll == false)
			_tasks.CancelAll();
		// 总开关或浏览器子开关关闭后，已经存在的 Edge 也必须立即收回，不能只阻止下一次启动。
		if (enabled == false || browserEnabled == false) StopBrowserSynchronously();
		NotifyChanged();
		return GetSettings();
	}

	/// <summary>探测视觉能力；不发起网络请求，也不返回 Provider 密钥或原文。</summary>
	public AutomationVisionProbeSnapshot ProbeVision()
	{
		if (_safeMode) return new(false, "安全模式已禁用自动化视觉能力");
		if (!_isWindows) return new(false, "Windows 桌面自动化仅支持 Windows");
		if (!_visionAvailable || _desktopVisionRunnerFactory is null || _desktopVisionPlannerFactory is null
			|| _desktopVisionActionFactory is null || _desktopVisionScreenshotFactory is null)
			return new(false, "当前版本未接入桌面多模态视觉能力");
		if (!IsChatVisionConfigurationValid()) return new(false, "当前聊天 Provider 未配置有效的视觉模型");
		return new(true, null);
	}

	/// <summary>列出当前可选窗口；返回值只包含一次性 token、尺寸和前台标记。</summary>
	public IReadOnlyList<AutomationDesktopWindowSnapshot> ListDesktopWindows()
	{
		ThrowIfDesktopExecutionBlocked();
		IDesktopVisionWindowCatalog catalog = CreateWindowCatalog();
		IReadOnlyList<WindowsTopLevelWindow> windows;
		try { windows = catalog.Enumerate(); }
		catch { throw new InvalidOperationException("桌面窗口列表不可用"); }

		Dictionary<string, DesktopWindowTarget> targets = new(StringComparer.Ordinal);
		List<AutomationDesktopWindowSnapshot> result = [];
		foreach (WindowsTopLevelWindow window in windows)
		{
			if (window.Handle == 0 || window.Bounds.Width <= 0 || window.Bounds.Height <= 0) continue;
			string token = $"desktop-{Guid.NewGuid():N}";
			targets[token] = new(window.Handle, window.Bounds.Width, window.Bounds.Height, window.IsForeground);
			result.Add(new(token, window.Bounds.Width, window.Bounds.Height, window.IsForeground));
		}

		lock (_desktopStateGate)
		{
			_desktopWindowTargets.Clear();
			foreach ((string token, DesktopWindowTarget target) in targets) _desktopWindowTargets[token] = target;
		}
		return result;
	}

	/// <summary>创建一个桌面视觉任务；任务正文只在内存执行链中传递。</summary>
	public AutomationDesktopTaskStartSnapshot StartDesktopTask(string task, string targetToken)
	{
		ThrowIfDesktopExecutionBlocked();
		if (string.IsNullOrWhiteSpace(task) || task.Trim().Length > 4096)
			throw new InvalidOperationException("桌面视觉任务内容无效");
		if (string.IsNullOrWhiteSpace(targetToken)) throw new InvalidOperationException("目标窗口 token 无效");

		DesktopWindowTarget? target;
		lock (_desktopStateGate)
		{
			if (!_desktopWindowTargets.TryGetValue(targetToken, out target) || target is null)
				throw new InvalidOperationException("目标窗口 token 无效或已过期");
		}

		IDesktopVisionScreenshotSource screenshotSource = CreateScreenshotSource();
		IDesktopVisionActionExecutor actionExecutor = CreateActionExecutor();
		IDesktopVisionPlanner planner = CreatePlanner();
		AutomationCapability granted = GetGrantedCapabilities(GetSettings());
		AutomationPolicy policy = new(granted, AutomationPolicy.Default.ScreenBounds);
		DesktopTaskState state = new();
		DesktopVisionRunnerRequest request = new(
			"桌面视觉任务",
			task.Trim(),
			target.Handle,
			screenshotSource,
			actionExecutor,
			planner,
			_desktopVisionApprovalCallback,
			policy,
			progress => OnDesktopProgress(state, progress));

		IAutomationTaskRunner runner;
		try
		{
			runner = (_desktopVisionRunnerFactory ?? throw new InvalidOperationException("桌面视觉执行器未装配"))(request)
				?? throw new InvalidOperationException("桌面视觉执行器未装配");
		}
		catch (InvalidOperationException exception) when (exception.Message == "桌面视觉执行器未装配")
		{
			throw;
		}
		catch
		{
			throw new InvalidOperationException("桌面视觉执行器装配失败");
		}

		AutomationTask taskModel = _tasks.Enqueue(new DesktopVisionRunnerAdapter(runner, state));
		state.Attach(taskModel.Id);
		lock (_desktopStateGate)
		{
			_desktopTasks[taskModel.Id] = state;
			TrimDesktopTasks();
		}
		ApplyTaskSnapshot(taskModel.Snapshot);
		NotifyChanged();
		return new(taskModel.Id, ToStatus(taskModel.Snapshot) ?? throw new InvalidOperationException("自动化任务状态不可用"));
	}

	/// <summary>登记或清除当前桌面高风险动作审批，供宿主现有协调器复用。</summary>
	public void SetDesktopApproval(AutomationApprovalRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (_safeMode) return;
		lock (_desktopStateGate)
		{
			_desktopApprovals[request.RequestId] = new(request.RequestId, request.TaskId, request.ActionKinds);
		}
		NotifyChanged();
	}

	/// <summary>清除桌面高风险动作审批。</summary>
	public void ClearDesktopApproval(Guid requestId)
	{
		lock (_desktopStateGate) _desktopApprovals.Remove(requestId);
		NotifyChanged();
	}

	/// <summary>登记一个经过显式能力授权的执行任务。</summary>
	public AutomationTask Enqueue(AutomationCapability requiredCapability, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ThrowIfExecutionBlocked(requiredCapability);
		return _tasks.Enqueue(operation, cancellationToken);
	}

	/// <summary>启动隔离 Edge；未满足安全边界时 fail-closed。</summary>
	public async Task<AutomationBrowserStatusSnapshot> StartBrowserAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _browserGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
			AutomationSettingsSnapshot settings = GetSettings();
			ThrowIfBrowserExecutionBlocked(settings);
			if (_browserRunner is not null) return GetBrowserStatus(settings);

			SetBrowserState(AutomationBrowserState.Starting, null);
			NotifyChanged();
			IAutomationBrowserRunner? runner = null;
			try
			{
				runner = _browserRunnerFactory()
					?? throw new InvalidOperationException("浏览器运行器未装配");
				_browserRunner = runner;
				await runner.StartAsync(cancellationToken).ConfigureAwait(false);
				SetBrowserState(AutomationBrowserState.Running, null);
				NotifyChanged();
				return GetBrowserStatus(settings);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await DisposeBrowserRunnerAsync(runner).ConfigureAwait(false);
				_browserRunner = null;
				SetBrowserState(AutomationBrowserState.Stopped, null);
				NotifyChanged();
				throw;
			}
			catch (Exception)
			{
				await DisposeBrowserRunnerAsync(runner).ConfigureAwait(false);
				_browserRunner = null;
				SetBrowserState(AutomationBrowserState.Failed, "start_failed");
				NotifyChanged();
				// 不把 Playwright 的路径、地址或页面信息带回桥接层。
				throw new InvalidOperationException("浏览器启动失败");
			}
		}
		finally
		{
			_browserGate.Release();
		}
	}

	/// <summary>停止隔离 Edge；没有会话时重复调用保持幂等。</summary>
	public async Task<AutomationBrowserStatusSnapshot> StopBrowserAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		// 停止是清理操作，不因桥接取消而留下浏览器 profile。
		await _browserGate.WaitAsync().ConfigureAwait(false);
		try { return await StopBrowserCoreAsync(notify: true).ConfigureAwait(false); }
		finally { _browserGate.Release(); }
	}

	/// <summary>取消一个任务；任务不存在或已终态时返回 false。</summary>
	public bool StopTask(Guid taskId)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		bool stopped = _tasks.Cancel(taskId);
		if (stopped) NotifyChanged();
		return stopped;
	}

	/// <summary>取消一个桌面视觉任务；重复调用保持幂等。</summary>
	public bool StopDesktopTask(Guid taskId) => StopTask(taskId);

	/// <summary>取消全部未终态任务并关闭浏览器；重复调用返回零。</summary>
	public int StopAll() => StopAllAsync().GetAwaiter().GetResult();

	/// <summary>异步取消全部未终态任务并关闭浏览器。</summary>
	public async Task<int> StopAllAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		int stopped = _tasks.CancelAll();
		// StopAll 是清理屏障，必须尽力拿到锁并关闭临时 profile。
		await _browserGate.WaitAsync().ConfigureAwait(false);
		try
		{
			await StopBrowserCoreAsync(notify: true).ConfigureAwait(false);
			return stopped;
		}
		finally
		{
			_browserGate.Release();
		}
	}

	/// <summary>释放自动化任务执行器与隔离 Edge。</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_tasks.TaskChanged -= OnTaskChanged;
		try
		{
			await _browserGate.WaitAsync().ConfigureAwait(false);
			try { await StopBrowserCoreAsync(notify: false).ConfigureAwait(false); }
			finally { _browserGate.Release(); }
		}
		catch
		{
			// 释放阶段仍继续关闭任务执行器。
		}
		try { await _tasks.DisposeAsync().ConfigureAwait(false); }
		finally { _browserGate.Dispose(); }
	}

	private bool IsExecutionAvailable(AutomationSettingsSnapshot settings) =>
		GetUnavailableReason(settings) is null;

	private bool IsBrowserExecutionAvailable(AutomationSettingsSnapshot settings) =>
		!_safeMode && _isWindows && settings.Enabled && settings.BrowserEnabled;

	private string? GetUnavailableReason(AutomationSettingsSnapshot settings)
	{
		if (_safeMode) return "安全模式已禁用自动化";
		if (!_isWindows) return "Windows 桌面自动化仅支持 Windows";
		if (!settings.Enabled) return "自动化默认关闭，请在设置中显式启用";
		if (!settings.AllowPointer && !settings.AllowKeyboard && !settings.AllowScroll)
			return "未显式授权任何自动化输入能力";
		if (!_visionAvailable || _desktopVisionRunnerFactory is null || _desktopVisionPlannerFactory is null
			|| _desktopVisionActionFactory is null || _desktopVisionScreenshotFactory is null)
			return "当前版本未接入桌面多模态视觉能力";
		if (!IsChatVisionConfigurationValid()) return "当前聊天 Provider 未配置有效的视觉模型";
		return null;
	}

	private bool IsVisionConfigurationValid() =>
		!_safeMode && _isWindows && _visionAvailable
		&& _desktopVisionRunnerFactory is not null
		&& _desktopVisionPlannerFactory is not null
		&& _desktopVisionActionFactory is not null
		&& _desktopVisionScreenshotFactory is not null
		&& IsChatVisionConfigurationValid();

	private bool IsChatVisionConfigurationValid()
	{
		AiChatSettings chat = _aiSettings.Read().Chat;
		if (!chat.IsConfigured) return false;
		return Uri.TryCreate(chat.BaseUrl, UriKind.Absolute, out Uri? uri)
			&& uri.Scheme is "http" or "https";
	}

	private void ThrowIfDesktopExecutionBlocked()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (_safeMode) throw new InvalidOperationException("安全模式已禁用桌面视觉自动化");
		AutomationSettingsSnapshot settings = GetSettings();
		string? reason = GetUnavailableReason(settings);
		if (reason is not null) throw new InvalidOperationException(reason);
	}

	private IDesktopVisionWindowCatalog CreateWindowCatalog()
	{
		try
		{
			return (_desktopVisionWindowCatalogFactory ?? throw new InvalidOperationException("桌面窗口能力未装配"))()
				?? throw new InvalidOperationException("桌面窗口能力未装配");
		}
		catch (InvalidOperationException exception) when (exception.Message == "桌面窗口能力未装配")
		{
			throw new InvalidOperationException("桌面窗口能力未装配");
		}
		catch
		{
			throw new InvalidOperationException("桌面窗口能力装配失败");
		}
	}

	private IDesktopVisionScreenshotSource CreateScreenshotSource()
	{
		try
		{
			return (_desktopVisionScreenshotFactory ?? throw new InvalidOperationException("桌面截图能力未装配"))()
				?? throw new InvalidOperationException("桌面截图能力未装配");
		}
		catch (InvalidOperationException exception) when (exception.Message == "桌面截图能力未装配")
		{
			throw new InvalidOperationException("桌面截图能力未装配");
		}
		catch
		{
			throw new InvalidOperationException("桌面截图能力装配失败");
		}
	}

	private IDesktopVisionActionExecutor CreateActionExecutor()
	{
		try
		{
			return (_desktopVisionActionFactory ?? throw new InvalidOperationException("桌面输入能力未装配"))()
				?? throw new InvalidOperationException("桌面输入能力未装配");
		}
		catch (InvalidOperationException exception) when (exception.Message == "桌面输入能力未装配")
		{
			throw new InvalidOperationException("桌面输入能力未装配");
		}
		catch
		{
			throw new InvalidOperationException("桌面输入能力装配失败");
		}
	}

	private IDesktopVisionPlanner CreatePlanner()
	{
		try
		{
			return (_desktopVisionPlannerFactory ?? throw new InvalidOperationException("桌面视觉规划器未装配"))()
				?? throw new InvalidOperationException("桌面视觉规划器未装配");
		}
		catch (InvalidOperationException exception) when (exception.Message == "桌面视觉规划器未装配")
		{
			throw new InvalidOperationException("桌面视觉规划器未装配");
		}
		catch
		{
			throw new InvalidOperationException("桌面视觉规划器装配失败");
		}
	}

	private string? GetBrowserUnavailableReason(AutomationSettingsSnapshot settings)
	{
		if (_safeMode) return "安全模式已禁用浏览器自动化";
		if (!_isWindows) return "Windows 浏览器自动化仅支持 Windows";
		if (!settings.Enabled) return "自动化默认关闭，请先显式启用";
		if (!settings.BrowserEnabled) return "浏览器自动化默认关闭，请先显式启用";
		return null;
	}

	private void ThrowIfControlBlocked()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (_safeMode) throw new InvalidOperationException("安全模式已禁用自动化设置");
		if (!_isWindows) throw new InvalidOperationException("Windows 桌面自动化仅支持 Windows");
	}

	private void ThrowIfBrowserExecutionBlocked(AutomationSettingsSnapshot settings)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (_safeMode) throw new InvalidOperationException("安全模式已禁用浏览器自动化");
		if (!_isWindows) throw new InvalidOperationException("Windows 浏览器自动化仅支持 Windows");
		if (!settings.Enabled) throw new InvalidOperationException("自动化默认关闭，请先显式启用");
		if (!settings.BrowserEnabled) throw new InvalidOperationException("浏览器自动化默认关闭，请先显式启用");
	}

	private void ThrowIfExecutionBlocked(AutomationCapability requiredCapability)
	{
		ThrowIfControlBlocked();
		AutomationSettingsSnapshot settings = GetSettings();
		if (!_visionAvailable) throw new InvalidOperationException("当前版本未接入多模态视觉能力");
		if (!settings.Enabled) throw new InvalidOperationException("自动化默认关闭，请先显式启用");
		if (requiredCapability == AutomationCapability.None
			|| (requiredCapability & (AutomationCapability.Pointer | AutomationCapability.Keyboard | AutomationCapability.Scroll)) != requiredCapability)
			throw new InvalidOperationException("自动化执行能力不在显式白名单内");
		AutomationCapability granted = GetGrantedCapabilities(settings);
		if ((granted & requiredCapability) != requiredCapability)
			throw new InvalidOperationException("自动化能力未被显式授权");
	}

	private static AutomationCapability GetGrantedCapabilities(AutomationSettingsSnapshot settings) =>
		(settings.AllowPointer ? AutomationCapability.Pointer : AutomationCapability.None)
		| (settings.AllowKeyboard ? AutomationCapability.Keyboard : AutomationCapability.None)
		| (settings.AllowScroll ? AutomationCapability.Scroll : AutomationCapability.None);

	private AutomationBrowserStatusSnapshot GetBrowserStatus(AutomationSettingsSnapshot settings)
	{
		AutomationBrowserState state;
		string? failureCode;
		lock (_browserStateGate)
		{
			state = _browserState;
			failureCode = _browserFailureCode;
		}
		string? reason = GetBrowserUnavailableReason(settings);
		if (reason is null && failureCode is not null) reason = "浏览器启动失败";
		return new(state, settings.BrowserEnabled, IsBrowserExecutionAvailable(settings), reason);
	}

	private void SetBrowserState(AutomationBrowserState state, string? failureCode)
	{
		lock (_browserStateGate)
		{
			_browserState = state;
			_browserFailureCode = failureCode;
		}
	}

	private async Task<AutomationBrowserStatusSnapshot> StopBrowserCoreAsync(bool notify)
	{
		IAutomationBrowserRunner? runner = _browserRunner;
		_browserRunner = null;
		bool changed;
		lock (_browserStateGate)
		{
			changed = runner is not null || _browserState != AutomationBrowserState.Stopped;
			_browserState = AutomationBrowserState.Stopped;
			_browserFailureCode = null;
		}

		await DisposeBrowserRunnerAsync(runner).ConfigureAwait(false);
		if (notify && changed) NotifyChanged();
		return GetBrowserStatus();
	}

	private static async Task DisposeBrowserRunnerAsync(IAutomationBrowserRunner? runner)
	{
		if (runner is null) return;
		try { await runner.DisposeAsync().ConfigureAwait(false); }
		catch { /* 浏览器已崩溃时仍保持 fail-closed 状态 */ }
	}

	private void StopBrowserSynchronously() => StopBrowserAsync().GetAwaiter().GetResult();

	private void SetBool(string key, bool value) => _config.Set(key, new ConfigValue.Boolean(value));

	private static IAutomationTaskRunner DefaultDesktopVisionRunnerFactory(DesktopVisionRunnerRequest request) =>
		new DesktopVisionAutomationRunner(
			request.TaskTitle,
			request.Goal,
			request.TargetWindow,
			request.ScreenshotSource,
			request.ActionExecutor,
			request.Planner,
			request.ApprovalCallback,
			request.Policy,
			progress: request.Progress);

	private void OnDesktopProgress(DesktopTaskState state, DesktopVisionProgress progress)
	{
		state.Report(progress);
		NotifyChanged();
	}

	private void ApplyTaskSnapshot(AutomationTaskSnapshot snapshot)
	{
		lock (_desktopStateGate)
		{
			if (_desktopTasks.TryGetValue(snapshot.Id, out DesktopTaskState? state)) state.ApplyLifecycle(snapshot);
		}
	}

	private IReadOnlyList<AutomationTaskStatusSnapshot> GetDesktopTaskSnapshots()
	{
		lock (_desktopStateGate)
		{
			return _desktopTasks.Values
				.OrderByDescending(state => state.Sequence)
				.Select(state => state.ToSnapshot())
				.ToArray();
		}
	}

	private IReadOnlyList<AutomationDesktopApprovalSnapshot> GetDesktopApprovalSnapshots()
	{
		lock (_desktopStateGate) return _desktopApprovals.Values.ToArray();
	}

	private void TrimDesktopTasks()
	{
		while (_desktopTasks.Count > MaxPublicDesktopTasks)
		{
			(Guid id, _) = _desktopTasks.OrderBy(pair => pair.Value.Sequence).First();
			_desktopTasks.Remove(id);
		}
	}

	private void OnTaskChanged(AutomationTaskSnapshot snapshot)
	{
		ApplyTaskSnapshot(snapshot);
		NotifyChanged();
	}

	private void NotifyChanged()
	{
		try { Changed?.Invoke(); }
		catch { /* 状态通知不能影响自动化生命周期 */ }
	}

	private AutomationTaskStatusSnapshot? ToStatus(AutomationTaskSnapshot? task)
	{
		if (task is null) return null;
		lock (_desktopStateGate)
		{
			return _desktopTasks.TryGetValue(task.Id, out DesktopTaskState? state)
				? state.ToSnapshot(task)
				: new(task.Id, task.State, 0, LifecycleCategory(task.State), task.State == AutomationTaskState.Failed ? task.FailureCode : null);
		}
	}

	private static string LifecycleCategory(AutomationTaskState state) => state switch
	{
		AutomationTaskState.Queued => "queued",
		AutomationTaskState.Running => "running",
		AutomationTaskState.Completed => "completed",
		AutomationTaskState.Cancelled => "cancelled",
		AutomationTaskState.Failed => "execution_failed",
		_ => "unknown",
	};

	private sealed record DesktopWindowTarget(nint Handle, int Width, int Height, bool IsForeground);

	private sealed class DesktopTaskState
	{
		private readonly object _gate = new();
		private Guid _taskId;
		private AutomationTaskSnapshot? _lifecycle;
		private int _step;
		private string _category = "queued";
		private string? _errorCategory;
		private long _sequence;

		public long Sequence => Volatile.Read(ref _sequence);

		public void Attach(Guid taskId)
		{
			lock (_gate)
			{
				_taskId = taskId;
				Interlocked.Increment(ref _sequence);
			}
		}

		public void Report(DesktopVisionProgress progress)
		{
			lock (_gate)
			{
				_step = Math.Max(0, progress.Step);
				_category = progress.Category switch
				{
					DesktopVisionAutomationCategory.Running => "running",
					DesktopVisionAutomationCategory.StepSucceeded => "step_succeeded",
					_ => new DesktopVisionAutomationResult(progress.Category, progress.Step).StableCategory,
				};
				_errorCategory = IsError(progress.Category) ? new DesktopVisionAutomationResult(progress.Category, progress.Step).StableCategory : null;
				Interlocked.Increment(ref _sequence);
			}
		}

		public void MarkReturned()
		{
			lock (_gate)
			{
				if (_category is "queued" or "running" or "step_succeeded") _category = "completed";
				Interlocked.Increment(ref _sequence);
			}
		}

		public void ApplyLifecycle(AutomationTaskSnapshot snapshot)
		{
			lock (_gate)
			{
				_lifecycle = snapshot;
				if (snapshot.State == AutomationTaskState.Cancelled)
				{
					_category = "cancelled";
					_errorCategory = null;
				}
				else if (snapshot.State == AutomationTaskState.Failed)
				{
					_category = snapshot.FailureCode ?? "execution_failed";
					_errorCategory = _category;
				}
				else if (snapshot.State == AutomationTaskState.Running && _category == "queued")
				{
					_category = "running";
				}
				Interlocked.Increment(ref _sequence);
			}
		}

		public AutomationTaskStatusSnapshot ToSnapshot(AutomationTaskSnapshot? current = null)
		{
			lock (_gate)
			{
				AutomationTaskSnapshot lifecycle = current ?? _lifecycle ?? new AutomationTaskSnapshot(
					_taskId,
					AutomationTaskState.Queued,
					DateTimeOffset.UtcNow,
					null,
					null,
					null);
				AutomationTaskState state = lifecycle.State;
				if (state == AutomationTaskState.Completed && _errorCategory is not null) state = AutomationTaskState.Failed;
				string category = _category;
				string? error = _errorCategory;
				return new(_taskId, state, _step, category, error);
			}
		}

		private static bool IsError(DesktopVisionAutomationCategory category) => category is not (
			DesktopVisionAutomationCategory.Running
			or DesktopVisionAutomationCategory.StepSucceeded
			or DesktopVisionAutomationCategory.Completed);
	}

	private sealed class DesktopVisionRunnerAdapter(IAutomationTaskRunner inner, DesktopTaskState state) : IAutomationTaskRunner
	{
		public async Task RunAsync(AutomationTaskContext context, CancellationToken cancellationToken)
		{
			await inner.RunAsync(context, cancellationToken).ConfigureAwait(false);
			state.MarkReturned();
		}
	}
}
