using Avalonia.Threading;

namespace Nori.Desktop.Bridge;

/// <summary>
/// 窗口相关操作的 UI 线程调度入口。
///
/// 生产环境始终委托 Avalonia Dispatcher；测试可注入同步实现，避免没有 UI 消息循环的
/// 测试宿主等待永远不会被处理的调度任务。
/// </summary>
public interface IUiDispatcher
{
	void Post(Action action);

	Task<T> InvokeAsync<T>(Func<T> action);

	Task InvokeTaskAsync(Func<Task> action);

	Task<T> InvokeTaskAsync<T>(Func<Task<T>> action);
}

/// <summary>Avalonia UI 线程调度器实现。</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
	public static AvaloniaUiDispatcher Instance { get; } = new();

	private AvaloniaUiDispatcher()
	{
	}

	public void Post(Action action) => Dispatcher.UIThread.Post(action);

	public Task<T> InvokeAsync<T>(Func<T> action) =>
		Dispatcher.UIThread.CheckAccess() ? Task.FromResult(action()) : Dispatcher.UIThread.InvokeAsync(action).GetTask();

	public Task InvokeTaskAsync(Func<Task> action) =>
		Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);

	public Task<T> InvokeTaskAsync<T>(Func<Task<T>> action) =>
		Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
}
