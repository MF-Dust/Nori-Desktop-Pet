using System.Runtime.Versioning;

namespace Nori.Desktop.Startup;

/// <summary>
/// Windows 单实例与第二实例激活信号。
///
/// 只在 Windows 启用命名内核对象；其他平台不改变现有启动方式。第二个实例不创建
/// Avalonia 应用，而是唤醒第一个实例的 main 窗口后立即退出。
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
	private const string MutexName = @"Local\NoriDesktopPet.SingleInstance";
	private const string ActivationEventName = @"Local\NoriDesktopPet.Activate";

	private readonly Mutex? _mutex;
	private readonly EventWaitHandle? _activationEvent;
	private readonly CancellationTokenSource _listenerCts = new();
	private readonly Action _onActivate;
	private readonly Thread? _listener;
	private bool _ownsMutex;
	private int _disposed;

	private SingleInstanceGuard(
		Mutex? mutex,
		EventWaitHandle? activationEvent,
		bool ownsMutex,
		Action onActivate)
	{
		_mutex = mutex;
		_activationEvent = activationEvent;
		_ownsMutex = ownsMutex;
		_onActivate = onActivate;

		if (_activationEvent is not null)
		{
			_listener = new Thread(Listen)
			{
				IsBackground = true,
				Name = "Nori second-instance listener",
			};
			_listener.Start();
		}
	}

	/// <summary>
	/// 尝试成为第一个实例。返回 null 表示已经向现有实例发送激活信号。
	/// </summary>
	public static SingleInstanceGuard? TryAcquire(Action onActivate)
	{
		ArgumentNullException.ThrowIfNull(onActivate);
		if (!OperatingSystem.IsWindows()) return new SingleInstanceGuard(null, null, false, onActivate);

		Mutex mutex = new(true, MutexName, out bool createdNew);
		bool ownsMutex = createdNew;
		if (!createdNew)
		{
			try
			{
				ownsMutex = mutex.WaitOne(0);
			}
			catch (AbandonedMutexException)
			{
				ownsMutex = true;
			}

			if (!ownsMutex)
			{
				SignalExistingInstance();
				mutex.Dispose();
				return null;
			}
		}

		EventWaitHandle? activationEvent = null;
		try
		{
			activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
			return new SingleInstanceGuard(mutex, activationEvent, ownsMutex, onActivate);
		}
		catch
		{
			activationEvent?.Dispose();
			if (ownsMutex)
			{
				try { mutex.ReleaseMutex(); } catch { }
			}
			mutex.Dispose();
			throw;
		}
	}

	[SupportedOSPlatform("windows")]
	private static void SignalExistingInstance()
	{
		// Mutex 已经建立而事件监听线程可能尚未创建，短暂重试覆盖这个启动竞态。
		for (int attempt = 0; attempt < 10; attempt++)
		{
			try
			{
				using EventWaitHandle activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
				activationEvent.Set();
				return;
			}
			catch (WaitHandleCannotBeOpenedException) when (attempt < 9)
			{
				Thread.Sleep(50);
			}
			catch (UnauthorizedAccessException) when (attempt < 9)
			{
				Thread.Sleep(50);
			}
			catch
			{
				return;
			}
		}
	}

	private void Listen()
	{
		if (_activationEvent is null) return;
		try
		{
			while (!_listenerCts.IsCancellationRequested)
			{
				if (!_activationEvent.WaitOne(250)) continue;
				if (_listenerCts.IsCancellationRequested) break;
				try { _onActivate(); } catch { }
			}
		}
		catch (ObjectDisposedException) when (_listenerCts.IsCancellationRequested)
		{
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_listenerCts.Cancel();
		try { _activationEvent?.Set(); } catch { }
		if (_listener is not null && _listener != Thread.CurrentThread) _listener.Join(1000);
		_activationEvent?.Dispose();
		_listenerCts.Dispose();
		if (_ownsMutex)
		{
			try { _mutex?.ReleaseMutex(); } catch { }
		}
		_mutex?.Dispose();
		_ownsMutex = false;
	}
}
