using Nori.Desktop.Audio;

namespace Nori.Desktop.Tests;

/// <summary>AudioHostChannel 真正等待 audio_host_ready 握手的语义测试。</summary>
public sealed class AudioHostChannelTests
{
	[Fact]
	public async Task 宿主不存在时立即明确不可用()
	{
		using AudioHostChannel channel = new(() => null);

		Assert.False(channel.IsAvailable);
		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => channel.WaitUntilReadyAsync());
		Assert.Contains("不可用", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 宿主存在但未就绪时等待握手()
	{
		TestChannel channel = new();
		Task wait = channel.WaitUntilReadyAsync();
		Assert.False(wait.IsCompleted);

		channel.MarkReady();
		await wait.WaitAsync(TimeSpan.FromSeconds(1));
		Assert.True(channel.IsReady);
	}

	[Fact]
	public async Task 等待支持取消()
	{
		TestChannel channel = new();
		using CancellationTokenSource cts = new();

		Task wait = channel.WaitUntilReadyAsync(cts.Token);
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
	}

	[Fact]
	public async Task 超时后抛出TimeoutException而不是永远等待()
	{
		TestChannel channel = new(TimeSpan.FromMilliseconds(50));

		TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(
			() => channel.WaitUntilReadyAsync());
		Assert.Contains("超时", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Dispose解除等待者并拒绝后续使用()
	{
		TestChannel channel = new();
		Task wait = channel.WaitUntilReadyAsync();

		channel.Dispose();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
		Assert.False(channel.IsAvailable);

		await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.WaitUntilReadyAsync());
		Assert.Throws<ObjectDisposedException>(() => channel.Post("nori:audio-play", null));
	}

	[Fact]
	public void Dispose幂等()
	{
		TestChannel channel = new();
		channel.Dispose();
		channel.Dispose();
	}

	/// <summary>绕过窗口解析, 只测等待机器的替身。</summary>
	private sealed class TestChannel(TimeSpan? readyTimeout = null) : AudioHostChannel(() => null, readyTimeout)
	{
		private int _disposed;

		public override bool IsAvailable => Volatile.Read(ref _disposed) == 0;

		public override void Dispose()
		{
			Volatile.Write(ref _disposed, 1);
			base.Dispose();
		}
	}
}
