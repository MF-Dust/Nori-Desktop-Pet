namespace Nori.Core.Voice;

/// <summary>
/// 语音合成流水线 (生产者/消费者) 的联接器。
///
/// 先失败的一方是主错误; 另一方因流水线取消产生的 OperationCanceledException
/// 只被观察, 不与主错误聚合成 AggregateException。
/// </summary>
public static class VoicePipeline
{
	public static async Task JoinAsync(Task producer, Task consumer)
	{
		Task first = await Task.WhenAny(producer, consumer).ConfigureAwait(false);
		if (first.IsFaulted)
		{
			Task other = ReferenceEquals(first, producer) ? consumer : producer;
			await ObserveQuietlyAsync(other).ConfigureAwait(false);
			await first.ConfigureAwait(false);
			return;
		}
		await producer.ConfigureAwait(false);
		await consumer.ConfigureAwait(false);
	}

	private static async Task ObserveQuietlyAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			// 次要失败已被观察; 主错误保持为第一失败方的原始异常。
		}
	}
}
