using System.Diagnostics;
using System.Text;

namespace Nori.Core.Chat;

/// <summary>
/// 流式文本回调的服务层节流器。
///
/// 普通增量最多每秒发出 30 次；流结束时会等待到下一个允许时刻再刷新尾部，避免最后一批又造成一次
/// 紧邻的回调。它不改变文本内容，只合并相邻增量。
/// </summary>
public sealed class TextChunkCoalescer
{
	/// <summary>最多 30Hz 的最小回调间隔。</summary>
	public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

	private readonly StringBuilder _pending = new();
	private long _lastEmissionTimestamp = long.MinValue;

	/// <summary>追加分片；满足节流间隔时返回一批，否则返回 null。</summary>
	public string? Push(string chunk)
	{
		ArgumentNullException.ThrowIfNull(chunk);
		return Push(chunk, Stopwatch.GetTimestamp());
	}

	/// <summary>使用指定时钟追加分片，便于纯函数测试。</summary>
	public string? Push(string chunk, long timestamp)
	{
		if (chunk.Length == 0) return null;
		_pending.Append(chunk);
		if (!CanEmit(timestamp)) return null;
		return TakePending(timestamp);
	}

	/// <summary>当前待发送字符数。</summary>
	public int PendingLength => _pending.Length;

	/// <summary>以真实时钟刷新尾部，必要时等待到下一个 30Hz 时间片。</summary>
	public async Task<string?> FlushAsync(CancellationToken cancellationToken = default)
	{
		if (_pending.Length == 0) return null;
		long now = Stopwatch.GetTimestamp();
		if (!CanEmit(now))
		{
			TimeSpan delay = RemainingDelay(now);
			if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			now = Stopwatch.GetTimestamp();
		}
		return TakePending(now);
	}

	/// <summary>使用指定时钟刷新；测试不需要等待，调用方可再次传入未来时间戳。</summary>
	public string? Flush(long timestamp)
	{
		if (_pending.Length == 0) return null;
		if (!CanEmit(timestamp)) return null;
		return TakePending(timestamp);
	}

	/// <summary>清空尚未发送的分片。</summary>
	public void Reset() => _pending.Clear();

	private bool CanEmit(long timestamp) =>
		_lastEmissionTimestamp == long.MinValue || Elapsed(timestamp, _lastEmissionTimestamp) >= MinimumInterval;

	private string TakePending(long timestamp)
	{
		string result = _pending.ToString();
		_pending.Clear();
		_lastEmissionTimestamp = timestamp;
		return result;
	}

	private TimeSpan RemainingDelay(long timestamp)
	{
		double elapsedSeconds = (double)(timestamp - _lastEmissionTimestamp) / Stopwatch.Frequency;
		return MinimumInterval - TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
	}

	private static TimeSpan Elapsed(long current, long previous) =>
		TimeSpan.FromSeconds(Math.Max(0, (double)(current - previous) / Stopwatch.Frequency));
}
