using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>
/// 一次性媒体交换所: token 只能用一次、过期失效、上传等待
/// </summary>
public class MediaExchangeTests
{
	[Fact]
	public void 音频token只能取一次()
	{
		MediaExchange exchange = new();
		byte[] payload = [1, 2, 3, 4];
		string token = exchange.PublishAudio(payload, "audio/mpeg");

		Assert.True(exchange.TryTakeAudio(token, out byte[] first, out string mime));
		Assert.Equal(payload, first);
		Assert.Equal("audio/mpeg", mime);

		// 第二次拿不到 (取走即删)
		Assert.False(exchange.TryTakeAudio(token, out _, out _));
	}

	[Fact]
	public void 未知token取不到音频()
	{
		MediaExchange exchange = new();
		Assert.False(exchange.TryTakeAudio("deadbeef", out _, out _));
	}

	[Fact]
	public void 每次发布的token都不同()
	{
		MediaExchange exchange = new();
		string first = exchange.PublishAudio([1], "audio/mpeg");
		string second = exchange.PublishAudio([1], "audio/mpeg");
		Assert.NotEqual(first, second);
	}

	[Fact]
	public async Task 上传票据可被等待并取回数据()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		byte[] recorded = [9, 8, 7];

		Task<byte[]> waiting = exchange.WaitForUploadAsync(token, TimeSpan.FromSeconds(5));
		Assert.True(exchange.TryCompleteUpload(token, recorded));

		Assert.Equal(recorded, await waiting);
	}

	[Fact]
	public async Task 上传票据只能兑付一次()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		Task<byte[]> waiting = exchange.WaitForUploadAsync(token, TimeSpan.FromSeconds(5));

		Assert.True(exchange.TryCompleteUpload(token, [1]));
		await waiting;

		// 票据已在等待结束时移除
		Assert.False(exchange.TryCompleteUpload(token, [2]));
	}

	[Fact]
	public async Task 上传超时抛TimeoutException()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		await Assert.ThrowsAsync<TimeoutException>(() =>
			exchange.WaitForUploadAsync(token, TimeSpan.FromMilliseconds(30)));
	}

	[Fact]
	public async Task 取消票据让等待方立即结束()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		Task<byte[]> waiting = exchange.WaitForUploadAsync(token, TimeSpan.FromSeconds(5));

		exchange.CancelUpload(token);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
	}

	[Fact]
	public async Task 带MIME的录音上传保留格式和文件名()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		Task<RecordedAudio> waiting = exchange.WaitForRecordedUploadAsync(token, TimeSpan.FromSeconds(5));

		Assert.True(exchange.TryCompleteUpload(token, new RecordedAudio([1, 2], "audio/webm;codecs=opus", "speech.webm")));
		RecordedAudio audio = await waiting;
		Assert.Equal("audio/webm;codecs=opus", audio.Mime);
		Assert.Equal("speech.webm", audio.FileName);
	}

	[Fact]
	public async Task 录音失败立即结束等待()
	{
		MediaExchange exchange = new();
		string token = exchange.CreateUploadTicket();
		Task<RecordedAudio> waiting = exchange.WaitForRecordedUploadAsync(token, TimeSpan.FromMinutes(1));

		Assert.True(exchange.TryFailUpload(token, "权限被拒绝"));
		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => waiting);
		Assert.Contains("权限被拒绝", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 过期媒体和上传票据不可用()
	{
		MediaExchange exchange = new(TimeSpan.FromMilliseconds(1));
		string download = exchange.PublishAudio([1], "audio/wav");
		string upload = exchange.CreateUploadTicket();
		await Task.Delay(20);

		Assert.False(exchange.TryTakeAudio(download, out _, out _));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			exchange.WaitForRecordedUploadAsync(upload, TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public async Task 未知票据无法等待()
	{
		MediaExchange exchange = new();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			exchange.WaitForUploadAsync("missing", TimeSpan.FromMilliseconds(10)));
	}
}
