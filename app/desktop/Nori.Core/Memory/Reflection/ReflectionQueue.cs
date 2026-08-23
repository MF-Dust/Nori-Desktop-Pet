using System.Threading.Channels;

namespace Nori.Core.Memory;

/// <summary>容量为 1 的 Reflection 队列，重复触发只保留一次后台工作。</summary>
public sealed class ReflectionQueue : IAsyncDisposable
{
	private readonly Channel<ReflectionJob> _channel = Channel.CreateBounded<ReflectionJob>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true,
		SingleWriter = false,
	});

	public bool TryEnqueue(ReflectionJob job) => _channel.Writer.TryWrite(job);

	public IAsyncEnumerable<ReflectionJob> ReadAllAsync(CancellationToken cancellationToken) =>
		_channel.Reader.ReadAllAsync(cancellationToken);

	public ValueTask DisposeAsync()
	{
		_channel.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}
}
