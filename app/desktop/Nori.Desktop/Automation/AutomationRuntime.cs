using Nori.Core.Automation;
using Nori.Core.Configuration;

namespace Nori.Desktop.Automation;

/// <summary>自动化设置的脱敏快照。</summary>
public sealed record AutomationSettingsSnapshot(
	bool Enabled,
	bool AllowPointer,
	bool AllowKeyboard,
	bool AllowScroll);

/// <summary>宿主当前显式授权的自动化能力。</summary>
public sealed record AutomationCapabilitiesSnapshot(
	bool IsWindows,
	bool VisionAvailable,
	bool Pointer,
	bool Keyboard,
	bool Scroll,
	string? UnavailableReason);

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
	int QueuedCount);

/// <summary>
/// 自动化宿主接线层。
///
/// 当前只装配任务生命周期和 Windows adapter 能力探测，不启动视觉循环或 Playwright runner。
/// 所有执行入口都经过安全模式、平台、总开关和显式能力授权校验。
/// </summary>
public sealed class AutomationRuntime : IAsyncDisposable
{
	private readonly ConfigStore _config;
	private readonly AutomationTaskManager _tasks;
	private readonly bool _safeMode;
	private readonly bool _isWindows;
	private readonly bool _visionAvailable;
	private int _disposed;

	/// <summary>自动化状态发生变化时触发；订阅者只能读取脱敏快照。</summary>
	public event Action? Changed;

	/// <summary>按宿主实际平台装配自动化运行时。</summary>
	public AutomationRuntime(ConfigStore config, bool safeMode, AutomationTaskManager? taskManager = null)
		: this(config, safeMode, OperatingSystem.IsWindows(), visionAvailable: false, taskManager)
	{
	}

	/// <summary>可注入平台和视觉能力的构造函数，供宿主边界测试使用。</summary>
	public AutomationRuntime(
		ConfigStore config,
		bool safeMode,
		bool isWindows,
		bool visionAvailable,
		AutomationTaskManager? taskManager = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		_config = config;
		_safeMode = safeMode;
		_isWindows = isWindows;
		_visionAvailable = visionAvailable;
		_tasks = taskManager ?? new AutomationTaskManager();
		_tasks.TaskChanged += OnTaskChanged;
	}

	/// <summary>读取当前设置。</summary>
	public AutomationSettingsSnapshot GetSettings() => new(
		_config.GetBoolOr(ConfigStore.KeyAutomationEnabled, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowPointer, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowKeyboard, false),
		_config.GetBoolOr(ConfigStore.KeyAutomationAllowScroll, false));

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
			GetUnavailableReason(settings));
	}

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
			_tasks.QueuedCount);
	}

	/// <summary>更新显式授权设置；未提供的字段保持原值。</summary>
	public AutomationSettingsSnapshot UpdateSettings(
		bool? enabled,
		bool? allowPointer,
		bool? allowKeyboard,
		bool? allowScroll)
	{
		ThrowIfControlBlocked();
		if (enabled is { } enabledValue) SetBool(ConfigStore.KeyAutomationEnabled, enabledValue);
		if (allowPointer is { } pointerValue) SetBool(ConfigStore.KeyAutomationAllowPointer, pointerValue);
		if (allowKeyboard is { } keyboardValue) SetBool(ConfigStore.KeyAutomationAllowKeyboard, keyboardValue);
		if (allowScroll is { } scrollValue) SetBool(ConfigStore.KeyAutomationAllowScroll, scrollValue);
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

	/// <summary>取消一个任务；任务不存在或已终态时返回 false。</summary>
	public bool StopTask(Guid taskId)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		bool stopped = _tasks.Cancel(taskId);
		if (stopped) NotifyChanged();
		return stopped;
	}

	/// <summary>取消全部未终态任务；重复调用返回零。</summary>
	public int StopAll()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		int stopped = _tasks.CancelAll();
		if (stopped > 0) NotifyChanged();
		return stopped;
	}

	/// <summary>释放自动化任务执行器。</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_tasks.TaskChanged -= OnTaskChanged;
		await _tasks.DisposeAsync().ConfigureAwait(false);
	}

	private bool IsExecutionAvailable(AutomationSettingsSnapshot settings) =>
		!_safeMode && _isWindows && _visionAvailable && settings.Enabled;

	private string? GetUnavailableReason(AutomationSettingsSnapshot settings)
	{
		if (_safeMode) return "安全模式已禁用自动化";
		if (!_isWindows) return "Windows 桌面自动化仅支持 Windows";
		if (!_visionAvailable) return "当前版本未接入多模态视觉能力";
		if (!settings.Enabled) return "自动化默认关闭，请在设置中显式启用";
		if (!settings.AllowPointer && !settings.AllowKeyboard && !settings.AllowScroll)
			return "未显式授权任何自动化输入能力";
		return null;
	}

	private void ThrowIfControlBlocked()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (_safeMode) throw new InvalidOperationException("安全模式已禁用自动化设置");
		if (!_isWindows) throw new InvalidOperationException("Windows 桌面自动化仅支持 Windows");
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
