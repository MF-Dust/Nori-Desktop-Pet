using System.Runtime.InteropServices;
using Nori.Core.Telemetry;
using Nori.Core.WebView;

namespace Nori.Core.Tests;

/// <summary>WebView 脚本派发器: 排队/关闭/异常观察语义测试。</summary>
public sealed class WebViewScriptDispatcherTests
{
	[Fact]
	public void 未就绪时排队就绪后按序派发()
	{
		List<string> invoked = [];
		WebViewScriptDispatcher dispatcher = new(script =>
		{
			invoked.Add(script);
			return Task.CompletedTask;
		});

		dispatcher.Dispatch("a");
		dispatcher.Dispatch("b");
		Assert.Empty(invoked);
		Assert.False(dispatcher.IsReady);

		dispatcher.MarkReady();
		Assert.Equal(["a", "b"], invoked);
		Assert.True(dispatcher.IsReady);

		dispatcher.Dispatch("c");
		Assert.Equal(["a", "b", "c"], invoked);
	}

	[Fact]
	public void MarkReady幂等不重复派发()
	{
		List<string> invoked = [];
		WebViewScriptDispatcher dispatcher = new(script =>
		{
			invoked.Add(script);
			return Task.CompletedTask;
		});

		dispatcher.Dispatch("a");
		dispatcher.MarkReady();
		dispatcher.MarkReady();

		Assert.Equal(["a"], invoked);
	}

	[Fact]
	public void 关闭后丢弃后续脚本()
	{
		List<string> invoked = [];
		WebViewScriptDispatcher dispatcher = new(script =>
		{
			invoked.Add(script);
			return Task.CompletedTask;
		});

		dispatcher.MarkReady();
		dispatcher.Close();
		dispatcher.Dispatch("after-close");

		Assert.Empty(invoked);
		Assert.True(dispatcher.IsClosed);
	}

	[Fact]
	public void 关闭会清空未派发的积压脚本()
	{
		List<string> invoked = [];
		WebViewScriptDispatcher dispatcher = new(script =>
		{
			invoked.Add(script);
			return Task.CompletedTask;
		});

		dispatcher.Dispatch("queued");
		dispatcher.Close();

		Assert.Empty(invoked);
	}

	[Fact]
	public void WebView失效态的预期异常静默丢弃不上报()
	{
		FakeTelemetry telemetry = new();
		WebViewScriptDispatcher dispatcher = new(_ => throw new COMException("已失效", unchecked((int)0x8007139F)), telemetry);
		WebViewScriptDispatcher disposedDispatcher = new(_ => throw new ObjectDisposedException("webview"), telemetry);

		dispatcher.MarkReady();
		disposedDispatcher.MarkReady();
		dispatcher.Dispatch("x");
		disposedDispatcher.Dispatch("y");

		Assert.Empty(telemetry.Captured);
	}

	[Fact]
	public void 其他异常进入遥测且不逃逸()
	{
		FakeTelemetry telemetry = new();
		WebViewScriptDispatcher dispatcher = new(_ => throw new InvalidOperationException("内部错误"), telemetry);
		dispatcher.MarkReady();

		dispatcher.Dispatch("boom");

		var (exception, operation, _) = Assert.Single(telemetry.Captured);
		Assert.IsType<InvalidOperationException>(exception);
		Assert.Equal("webview.dispatch", operation);
	}

	private sealed class FakeTelemetry : ITelemetry
	{
		public List<(Exception Exception, string Operation, IReadOnlyDictionary<string, string>? Tags)> Captured { get; } = [];

		public bool IsAvailable => true;

		public bool IsEnabled => true;

		public void Configure(bool enabled)
		{
		}

		public void CaptureException(Exception exception, string operation, bool handled = true, bool terminal = false, IReadOnlyDictionary<string, string>? tags = null) =>
			Captured.Add((exception, operation, tags));

		public ITelemetryTransaction StartTransaction(string operation) => new NoopTransaction();

		public Task FlushAsync(TimeSpan timeout) => Task.CompletedTask;

		public void Dispose()
		{
		}
	}

	private sealed class NoopTransaction : ITelemetryTransaction
	{
		public void Dispose()
		{
		}
	}
}
