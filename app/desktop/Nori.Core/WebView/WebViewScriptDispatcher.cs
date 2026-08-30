using System.Runtime.InteropServices;
using Nori.Core.Telemetry;

namespace Nori.Core.WebView;

/// <summary>
/// WebView 脚本派发器: 统一观察 InvokeScript 任务并管理 ready/pending/关闭状态。
///
/// - 未 ready 时脚本进入 pending 队列, MarkReady 后按序派发
/// - 每个派发任务都被完整观察: 预期的 WebView 失效态 (窗口或引擎已销毁的
///   COMException) 静默丢弃, 不产生 UnobservedTaskException; 其余异常进入遥测
/// - Close 之后停止派发并清空队列, 后续脚本直接丢弃
/// </summary>
public sealed class WebViewScriptDispatcher(Func<string, Task> invokeScript, ITelemetry? telemetry = null)
{
	/// <summary>窗口/引擎销毁后再调用的预期失败码, 0x8007139F = ERROR_INVALID_STATE。</summary>
	private const int ExpectedInvalidState = unchecked((int)0x8007139F);

	private readonly object _gate = new();
	private readonly Queue<string> _pending = [];
	private readonly Func<string, Task> _invokeScript = invokeScript;
	private readonly ITelemetry? _telemetry = telemetry;
	private bool _ready;
	private bool _closed;

	/// <summary>页面脚本通道是否已就绪。</summary>
	public bool IsReady
	{
		get { lock (_gate) return _ready && !_closed; }
	}

	/// <summary>窗口是否已关闭 (关闭后一切派发丢弃)。</summary>
	public bool IsClosed
	{
		get { lock (_gate) return _closed; }
	}

	/// <summary>页面导航完成, 开始派发积压脚本。</summary>
	public void MarkReady()
	{
		string[] scripts;
		lock (_gate)
		{
			if (_closed || _ready) return;
			_ready = true;
			scripts = [.. _pending];
			_pending.Clear();
		}
		foreach (string script in scripts) Dispatch(script);
	}

	/// <summary>
	/// 派发一段脚本; 未 ready 时排队, 已关闭时丢弃。
	///
	/// 派发任务在内部完整观察, 返回的包装任务永远不会 fault, 丢弃它是安全的。
	/// </summary>
	public void Dispatch(string script)
	{
		lock (_gate)
		{
			if (_closed) return;
			if (!_ready)
			{
				_pending.Enqueue(script);
				return;
			}
		}
		_ = InvokeObservedAsync(script);
	}

	/// <summary>窗口销毁: 停止派发并清空积压脚本。</summary>
	public void Close()
	{
		lock (_gate)
		{
			_closed = true;
			_pending.Clear();
		}
	}

	private async Task InvokeObservedAsync(string script)
	{
		try
		{
			await _invokeScript(script).ConfigureAwait(false);
		}
		catch (Exception exception) when (IsExpectedWebViewFailure(exception))
		{
			// WebView 已销毁/失效, 这类失败是窗口生命周期的正常结果, 丢弃即可。
		}
		catch (Exception exception)
		{
			_telemetry?.CaptureException(exception, "webview.dispatch");
		}
	}

	private static bool IsExpectedWebViewFailure(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			if (current is ObjectDisposedException) return true;
			if (current is COMException comException && comException.HResult == ExpectedInvalidState) return true;
		}
		return false;
	}
}
