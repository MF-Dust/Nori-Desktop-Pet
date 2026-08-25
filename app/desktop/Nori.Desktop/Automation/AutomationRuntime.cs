using Nori.Core.Automation;
using Nori.Core.Configuration;
using Nori.Desktop.Automation.Browser;

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

/// <summary>自动化任务的脱敏状态。</summary>
public sealed record AutomationTaskStatusSnapshot(
	Guid Id,
	AutomationTaskState State,
	DateTimeOffset CreatedAt,
	DateTimeOffset? StartedAt,
	DateTimeOffset? FinishedAt,
	string? FailureCode);

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
	private readonly AutomationTaskManager _tasks;
	private readonly bool _safeMode;
	private readonly bool _isWindows;
	private readonly bool _visionAvailable;
	private readonly Func<IAutomationBrowserRunner> _browserRunnerFactory;
	private readonly SemaphoreSlim _browserGate = new(1, 1);
	private readonly object _browserStateGate = new();
	private IAutomationBrowserRunner? _browserRunner;
	private AutomationBrowserState _browserState = AutomationBrowserState.Stopped;
	private string? _browserFailureCode;
	private int _disposed;

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
		Func<IAutomationBrowserRunner>? browserRunnerFactory = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		_config = config;
		_safeMode = safeMode;
		_isWindows = isWindows;
		_visionAvailable = visionAvailable;
		_tasks = taskManager ?? new AutomationTaskManager();
		_browserRunnerFactory = browserRunnerFactory ?? (() => new PlaywrightEdgeBrowserAutomationRunner());
		_tasks.TaskChanged += OnTaskChanged;
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
		return new(
			_isWindows,
			_visionAvailable,
			_isWindows && settings.Enabled && settings.AllowPointer,
			_isWindows && settings.Enabled && settings.AllowKeyboard,
			_isWindows && settings.Enabled && settings.AllowScroll,
			GetUnavailableReason(settings))
		{
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

		// 总开关或浏览器子开关关闭后，已经存在的 Edge 也必须立即收回，不能只阻止下一次启动。
		if (enabled == false || browserEnabled == false) StopBrowserSynchronously();
		NotifyChanged();
		return GetSettings();
	}

	/// <summary>探测视觉能力；当前没有多模态视觉实现时明确返回不可用。</summary>
	public AutomationVisionProbeSnapshot ProbeVision()
	{
		if (_safeMode) return new(false, "安全模式已禁用自动化视觉能力");
		if (!_isWindows) return new(false, "Windows 桌面自动化仅支持 Windows");
		if (!_visionAvailable) return new(false, "当前版本未接入多模态视觉能力");
		return new(true, null);
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
		!_safeMode && _isWindows && _visionAvailable && settings.Enabled;

	private bool IsBrowserExecutionAvailable(AutomationSettingsSnapshot settings) =>
		!_safeMode && _isWindows && settings.Enabled && settings.BrowserEnabled;

	private string? GetUnavailableReason(AutomationSettingsSnapshot settings)
	{
		if (_safeMode) return "安全模式已禁用自动化";
		if (!_isWindows) return "Windows 桌面自动化仅支持 Windows";
		if (!settings.Enabled) return "自动化默认关闭，请在设置中显式启用";
		if (!_visionAvailable) return "当前版本未接入多模态视觉能力";
		if (!settings.AllowPointer && !settings.AllowKeyboard && !settings.AllowScroll)
			return "未显式授权任何自动化输入能力";
		return null;
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

	private void OnTaskChanged(AutomationTaskSnapshot _) => NotifyChanged();

	private void NotifyChanged()
	{
		try { Changed?.Invoke(); }
		catch { /* 状态通知不能影响自动化生命周期 */ }
	}

	private static AutomationTaskStatusSnapshot? ToStatus(AutomationTaskSnapshot? task) => task is null
		? null
		: new(task.Id, task.State, task.CreatedAt, task.StartedAt, task.FinishedAt, task.FailureCode);
}
