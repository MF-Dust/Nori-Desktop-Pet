namespace Nori.Core.Automation;

/// <summary>自动化高风险动作审批回调；请求与结论均不携带正文。</summary>
public delegate Task<AutomationApprovalDecision> AutomationApprovalCallback(
	AutomationApprovalRequest request,
	CancellationToken cancellationToken);

/// <summary>审批结论，不携带自由文本。</summary>
public enum AutomationApprovalOutcome
{
	/// <summary>用户批准。</summary>
	Approved,
	/// <summary>用户拒绝。</summary>
	Denied,
	/// <summary>审批已过期。</summary>
	Expired,
}

/// <summary>脱敏审批请求：不包含提示词、截图或输入正文。</summary>
public sealed class AutomationApprovalRequest
{
	public Guid RequestId { get; }
	public Guid TaskId { get; }
	public IReadOnlyList<AutomationActionKind> ActionKinds { get; }
	public DateTimeOffset RequestedAt { get; }

	/// <summary>创建脱敏审批请求。</summary>
	public AutomationApprovalRequest(Guid requestId, Guid taskId, IEnumerable<AutomationActionKind> actionKinds, DateTimeOffset requestedAt)
	{
		if (requestId == Guid.Empty) throw new ArgumentException("审批请求标识不能为空", nameof(requestId));
		if (taskId == Guid.Empty) throw new ArgumentException("任务标识不能为空", nameof(taskId));
		ArgumentNullException.ThrowIfNull(actionKinds);
		AutomationActionKind[] kinds = actionKinds.Distinct().ToArray();
		if (kinds.Length == 0) throw new ArgumentException("审批请求必须包含动作种类", nameof(actionKinds));
		RequestId = requestId;
		TaskId = taskId;
		ActionKinds = Array.AsReadOnly(kinds);
		RequestedAt = requestedAt;
	}
}

/// <summary>脱敏审批决定。</summary>
public sealed record AutomationApprovalDecision(Guid RequestId, AutomationApprovalOutcome Outcome, DateTimeOffset DecidedAt)
{
	/// <summary>从请求创建审批决定。</summary>
	public static AutomationApprovalDecision Create(AutomationApprovalRequest request, AutomationApprovalOutcome outcome, DateTimeOffset decidedAt)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new AutomationApprovalDecision(request.RequestId, outcome, decidedAt);
	}
}
