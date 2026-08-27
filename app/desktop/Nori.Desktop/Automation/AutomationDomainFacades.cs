using Nori.Core.Automation;
using Nori.Desktop.Automation.Desktop;

namespace Nori.Desktop.Automation;

/// <summary>
/// Browser Automation 的窄接口。调用方不需要持有整个 AutomationRuntime。
/// </summary>
public sealed class BrowserAutomationHost(AutomationRuntime runtime)
{
	private readonly AutomationRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

	public AutomationBrowserStatusSnapshot GetStatus() => _runtime.GetBrowserStatus();

	public Task<AutomationBrowserStatusSnapshot> StartAsync(CancellationToken cancellationToken = default) =>
		_runtime.StartBrowserAsync(cancellationToken);

	public Task<AutomationBrowserTaskStartSnapshot> StartTaskAsync(
		BrowserAutomationTaskPlan plan,
		CancellationToken cancellationToken = default) =>
		_runtime.StartBrowserTaskAsync(plan, cancellationToken);

	public BrowserAutomationTaskResult? GetTaskResult(Guid taskId) => _runtime.GetBrowserTaskResult(taskId);

	public bool StopTask(Guid taskId) => _runtime.StopBrowserTask(taskId);

	public Task<AutomationBrowserStatusSnapshot> StopAsync(CancellationToken cancellationToken = default) =>
		_runtime.StopBrowserAsync(cancellationToken);
}

/// <summary>
/// Desktop Vision Automation 的窄接口。窗口枚举和桌面任务不再要求调用方依赖浏览器生命周期方法。
/// </summary>
public sealed class DesktopAutomationHost(AutomationRuntime runtime)
{
	private readonly AutomationRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

	public AutomationCapabilitiesSnapshot GetCapabilities() => _runtime.GetCapabilities();

	public AutomationVisionProbeSnapshot ProbeVision() => _runtime.ProbeVision();

	public IReadOnlyList<AutomationDesktopWindowSnapshot> ListWindows() => _runtime.ListDesktopWindows();

	public AutomationDesktopTaskStartSnapshot StartTask(string task, string targetToken) =>
		_runtime.StartDesktopTask(task, targetToken);

	public bool StopTask(Guid taskId) => _runtime.StopDesktopTask(taskId);
}

/// <summary>
/// 自动化审批与审计结果登记的窄接口。
/// </summary>
public sealed class AutomationApprovalHost(AutomationRuntime runtime)
{
	private readonly AutomationRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

	public void Set(AutomationApprovalRequest request) => _runtime.SetAutomationApproval(request);

	public void Clear(Guid requestId) => _runtime.ClearAutomationApproval(requestId);

	public void RecordOutcome(AutomationApprovalRequest request, AutomationApprovalOutcome outcome) =>
		_runtime.RecordApprovalOutcome(request, outcome);

	public void RecordCancellation(AutomationApprovalRequest request) =>
		_runtime.RecordApprovalCancellation(request);
}
