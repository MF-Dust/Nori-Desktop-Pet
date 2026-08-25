namespace Nori.Core.Automation;

/// <summary>自动化任务生命周期状态。</summary>
public enum AutomationTaskState
{
	/// <summary>已排队。</summary>
	Queued,
	/// <summary>正在执行。</summary>
	Running,
	/// <summary>已成功完成。</summary>
	Completed,
	/// <summary>已取消，终态。</summary>
	Cancelled,
	/// <summary>执行失败，终态。</summary>
	Failed,
}

/// <summary>自动化任务只读状态快照，不包含动作正文。</summary>
public sealed record AutomationTaskSnapshot(Guid Id, AutomationTaskState State, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? FailureCode);

/// <summary>供桌面端或 Edge runner 注入的最小执行上下文。</summary>
public sealed record AutomationTaskContext(Guid TaskId);

/// <summary>线程安全的自动化任务状态模型，由 AutomationTaskManager 驱动。</summary>
public sealed class AutomationTask
{
	private readonly object _gate = new();
	private readonly TaskCompletionSource<AutomationTaskSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private AutomationTaskState _state = AutomationTaskState.Queued;
	private DateTimeOffset? _startedAt;
	private DateTimeOffset? _finishedAt;
	private string? _failureCode;

	/// <summary>创建任务。</summary>
	public AutomationTask(Guid? id = null, DateTimeOffset? createdAt = null)
	{
		Id = id.GetValueOrDefault(Guid.NewGuid());
		if (Id == Guid.Empty) throw new ArgumentException("任务标识不能为空", nameof(id));
		CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
	}

	public Guid Id { get; }
	public DateTimeOffset CreatedAt { get; }
	public AutomationTaskState State { get { lock (_gate) return _state; } }
	/// <summary>任务完成时产生最终快照。</summary>
	public Task<AutomationTaskSnapshot> Completion => _completion.Task;
	/// <summary>读取线程安全状态快照。</summary>
	public AutomationTaskSnapshot Snapshot { get { lock (_gate) return CreateSnapshot(); } }

	internal bool TryMarkRunning(DateTimeOffset startedAt)
	{
		lock (_gate)
		{
			if (_state != AutomationTaskState.Queued) return false;
			_state = AutomationTaskState.Running;
			_startedAt = startedAt;
			return true;
		}
	}

	internal bool TryComplete(DateTimeOffset finishedAt)
	{
		lock (_gate)
		{
			if (_state != AutomationTaskState.Running) return false;
			_state = AutomationTaskState.Completed;
			_finishedAt = finishedAt;
			Complete();
			return true;
		}
	}

	internal bool TryCancel(DateTimeOffset finishedAt)
	{
		lock (_gate)
		{
			if (_state is AutomationTaskState.Completed or AutomationTaskState.Cancelled or AutomationTaskState.Failed) return false;
			_state = AutomationTaskState.Cancelled;
			_finishedAt = finishedAt;
			Complete();
			return true;
		}
	}

	internal bool TryFail(string failureCode, DateTimeOffset finishedAt)
	{
		if (string.IsNullOrWhiteSpace(failureCode)) throw new ArgumentException("失败代码不能为空", nameof(failureCode));
		lock (_gate)
		{
			if (_state != AutomationTaskState.Running) return false;
			_state = AutomationTaskState.Failed;
			_failureCode = failureCode;
			_finishedAt = finishedAt;
			Complete();
			return true;
		}
	}

	private void Complete() => _completion.TrySetResult(CreateSnapshot());
	private AutomationTaskSnapshot CreateSnapshot() => new(Id, _state, CreatedAt, _startedAt, _finishedAt, _failureCode);
}
