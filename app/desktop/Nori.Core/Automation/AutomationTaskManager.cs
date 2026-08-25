namespace Nori.Core.Automation;

/// <summary>供桌面端或 Edge runner 注入的自动化执行器。</summary>
public interface IAutomationTaskRunner
{
	/// <summary>执行任务；执行器必须及时观察取消令牌。</summary>
	Task RunAsync(AutomationTaskContext context, CancellationToken cancellationToken);
}

/// <summary>单活动自动化任务管理器；队列和状态转换线程安全，执行器在锁外运行。</summary>
public sealed class AutomationTaskManager : IAsyncDisposable
{
	private readonly object _gate = new();
	private readonly LinkedList<WorkItem> _queue = [];
	private readonly SemaphoreSlim _signal = new(0);
	private readonly CancellationTokenSource _shutdown = new();
	private readonly int _maxQueueLength;
	private readonly Task _worker;
	private WorkItem? _active;
	private bool _disposed;

	/// <summary>创建任务管理器。</summary>
	public AutomationTaskManager(int maxQueueLength = 32)
	{
		if (maxQueueLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxQueueLength));
		_maxQueueLength = maxQueueLength;
		_worker = ProcessAsync();
	}

	/// <summary>当前活动任务。</summary>
	public AutomationTaskSnapshot? ActiveTask { get { lock (_gate) return _active?.Task.Snapshot; } }
	/// <summary>当前等待任务数量。</summary>
	public int QueuedCount { get { lock (_gate) return _queue.Count; } }

	/// <summary>放入执行委托；任务状态不保存委托输入、提示词或动作正文。</summary>
	public AutomationTask Enqueue(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		return EnqueueCore((_, token) => operation(token), cancellationToken);
	}

	/// <summary>放入桌面端或 Edge runner。</summary>
	public AutomationTask Enqueue(IAutomationTaskRunner runner, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(runner);
		return EnqueueCore((context, token) => runner.RunAsync(context, token), cancellationToken);
	}

	/// <summary>取消排队或活动任务；终态任务返回 false。</summary>
	public bool Cancel(Guid taskId)
	{
		WorkItem? item = null;
		bool changed;
		lock (_gate)
		{
			if (_active?.Task.Id == taskId)
			{
				item = _active;
				changed = item.Task.TryCancel(DateTimeOffset.UtcNow);
			}
			else
			{
				LinkedListNode<WorkItem>? node = _queue.First;
				while (node is not null && node.Value.Task.Id != taskId) node = node.Next;
				if (node is null) return false;
				item = node.Value;
				_queue.Remove(node);
				changed = item.Task.TryCancel(DateTimeOffset.UtcNow);
			}
		}
		if (!changed || item is null) return false;
		item.Cancellation.Cancel();
		if (!ReferenceEquals(item, _active)) item.Dispose();
		return true;
	}

	/// <summary>取消指定任务。</summary>
	public bool Cancel(AutomationTask task)
	{
		ArgumentNullException.ThrowIfNull(task);
		return Cancel(task.Id);
	}

	/// <summary>取消所有未终态任务并等待活动执行器收尾。</summary>
	public async ValueTask DisposeAsync()
	{
		WorkItem[] pending;
		WorkItem? active;
		lock (_gate)
		{
			if (_disposed) return;
			_disposed = true;
			pending = _queue.ToArray();
			_queue.Clear();
			active = _active;
			foreach (WorkItem item in pending) item.Task.TryCancel(DateTimeOffset.UtcNow);
			active?.Task.TryCancel(DateTimeOffset.UtcNow);
		}
		foreach (WorkItem item in pending) { item.Cancellation.Cancel(); item.Dispose(); }
		active?.Cancellation.Cancel();
		_shutdown.Cancel();
		try { _signal.Release(); } catch (ObjectDisposedException) { }
		await _worker.ConfigureAwait(false);
		active?.Dispose();
		_signal.Dispose();
		_shutdown.Dispose();
	}

	private AutomationTask EnqueueCore(Func<AutomationTaskContext, CancellationToken, Task> operation, CancellationToken cancellationToken)
	{
		AutomationTask task = new();
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_queue.Count >= _maxQueueLength) throw new InvalidOperationException("自动化任务队列已满");
			WorkItem item = new(task, operation, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
			_queue.AddLast(item);
			item.Registration = cancellationToken.Register(() => { task.TryCancel(DateTimeOffset.UtcNow); SignalSafely(); });
		}
		_signal.Release();
		return task;
	}

	private async Task ProcessAsync()
	{
		try
		{
			while (true)
			{
				await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
				WorkItem? item = TakeNext();
				if (item is null)
				{
					lock (_gate) if (_disposed && _queue.Count == 0 && _active is null) return;
					continue;
				}
				if (!item.Task.TryMarkRunning(DateTimeOffset.UtcNow)) { item.Dispose(); continue; }
				try
				{
					await item.Operation(new AutomationTaskContext(item.Task.Id), item.Cancellation.Token).ConfigureAwait(false);
					item.Task.TryComplete(DateTimeOffset.UtcNow);
				}
				catch (OperationCanceledException) { item.Task.TryCancel(DateTimeOffset.UtcNow); }
				catch (Exception) { item.Task.TryFail("execution_failed", DateTimeOffset.UtcNow); }
				finally
				{
					lock (_gate) if (ReferenceEquals(_active, item)) _active = null;
					item.Dispose();
				}
			}
		}
		catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
	}

	private WorkItem? TakeNext()
	{
		lock (_gate)
		{
			while (_queue.First is { } node)
			{
				_queue.RemoveFirst();
				WorkItem item = node.Value;
				if (item.Task.State == AutomationTaskState.Cancelled || item.Cancellation.IsCancellationRequested)
				{
					item.Task.TryCancel(DateTimeOffset.UtcNow);
					item.Dispose();
					continue;
				}
				_active = item;
				return item;
			}
			return null;
		}
	}

	private void SignalSafely()
	{
		try { _signal.Release(); } catch (ObjectDisposedException) { }
	}

	private sealed class WorkItem : IDisposable
	{
		public WorkItem(AutomationTask task, Func<AutomationTaskContext, CancellationToken, Task> operation, CancellationTokenSource cancellation)
		{
			Task = task; Operation = operation; Cancellation = cancellation;
		}
		public AutomationTask Task { get; }
		public Func<AutomationTaskContext, CancellationToken, Task> Operation { get; }
		public CancellationTokenSource Cancellation { get; }
		public CancellationTokenRegistration Registration { get; set; }
		public void Dispose() { Registration.Dispose(); Cancellation.Dispose(); }
	}
}
