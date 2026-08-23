namespace Nori.Core.Memory;

/// <summary>单消费者 Reflection 后台 Worker。</summary>
public sealed class ReflectionWorker : IAsyncDisposable
{
	private readonly ReflectionQueue _queue;
	private readonly ReflectionService _service;
	private readonly Action<Exception>? _onError;
	private readonly Action? _onCompleted;
	private readonly CancellationTokenSource _cts = new();
	private Task? _worker;
	private int _started;

	public ReflectionWorker(ReflectionQueue queue, ReflectionService service, Action<Exception>? onError = null, Action? onCompleted = null)
	{
		_queue = queue;
		_service = service;
		_onError = onError;
		_onCompleted = onCompleted;
	}

	public void Start()
	{
		if (Interlocked.Exchange(ref _started, 1) != 0) return;
		_worker = Task.Run(RunAsync);
	}

	public bool TryEnqueue(ReflectionJob job) => _queue.TryEnqueue(job);

	private async Task RunAsync()
	{
		try
		{
			await foreach (ReflectionJob _ in _queue.ReadAllAsync(_cts.Token).ConfigureAwait(false))
			{
				try
				{
					if (await _service.ReflectPendingAsync(_cts.Token).ConfigureAwait(false))
					{
						try { _onCompleted?.Invoke(); }
						catch { }
						// 处理期间可能又有完整轮次进入队列，下一次循环继续检查持久化游标。
					}
				}
				catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
				catch (Exception exception) { _onError?.Invoke(exception); }
			}
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
	}

	public async ValueTask DisposeAsync()
	{
		_cts.Cancel();
		await _queue.DisposeAsync().ConfigureAwait(false);
		if (_worker is not null)
		{
			try { await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
			catch (OperationCanceledException) { }
			catch (TimeoutException) { }
		}
		_cts.Dispose();
	}
}
