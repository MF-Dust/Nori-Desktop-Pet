using System.Collections.Concurrent;

namespace Nori.Core.Voice;

/// <summary>
/// 一次性媒体交换所
///
/// 音频不走 JSON 桥: TTS 字节在这里登记一个一次性 token, 前端拿着
/// `/{prefix}/media/{token}` 直接下载播放; 麦克风录音反向走同一套 token 上传。
///
/// 纪律:
/// - token 只能用一次 (取走即删), 且有短过期时间
/// - 只在本机回环上暴露, 与 AssetServer 共用 Host 头与前缀校验
/// - 全部驻留内存, 不落盘 (录音是隐私数据)
/// </summary>
public sealed class MediaExchange
{
	/// <summary>token 有效期</summary>
	public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

	private sealed record Entry(byte[] Data, string Mime, DateTimeOffset ExpiresAt);

	private sealed class Upload
	{
		public TaskCompletionSource<byte[]> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public DateTimeOffset ExpiresAt { get; init; }
	}

	private readonly ConcurrentDictionary<string, Entry> _downloads = new();
	private readonly ConcurrentDictionary<string, Upload> _uploads = new();

	private static string NewToken() =>
		Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

	/// <summary>
	/// 登记一段待播放音频, 返回一次性 token
	/// </summary>
	public string PublishAudio(byte[] data, string mime)
	{
		Prune();
		string token = NewToken();
		_downloads[token] = new Entry(data, mime, DateTimeOffset.UtcNow + Ttl);
		return token;
	}

	/// <summary>
	/// 取走音频 (取走即删); token 无效或已过期返回 false
	/// </summary>
	public bool TryTakeAudio(string token, out byte[] data, out string mime)
	{
		data = [];
		mime = "application/octet-stream";
		if (!_downloads.TryRemove(token, out Entry? entry)) return false;
		if (entry.ExpiresAt < DateTimeOffset.UtcNow) return false;
		data = entry.Data;
		mime = entry.Mime;
		return true;
	}

	/// <summary>
	/// 开一张上传票据 (给前端录音用), 返回 token
	/// </summary>
	public string CreateUploadTicket()
	{
		Prune();
		string token = NewToken();
		_uploads[token] = new Upload {ExpiresAt = DateTimeOffset.UtcNow + Ttl};
		return token;
	}

	/// <summary>
	/// 完成一次上传; token 无效返回 false
	/// </summary>
	public bool TryCompleteUpload(string token, byte[] data)
	{
		if (!_uploads.TryGetValue(token, out Upload? upload)) return false;
		return upload.Completion.TrySetResult(data);
	}

	/// <summary>
	/// 放弃一张票据 (录音失败/取消)
	/// </summary>
	public void CancelUpload(string token)
	{
		if (_uploads.TryRemove(token, out Upload? upload)) upload.Completion.TrySetCanceled();
	}

	/// <summary>
	/// 等待某张票据的上传结果; 超时抛 TimeoutException
	/// </summary>
	public async Task<byte[]> WaitForUploadAsync(string token, TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		if (!_uploads.TryGetValue(token, out Upload? upload)) throw new InvalidOperationException("上传票据不存在或已失效");
		try
		{
			return await upload.Completion.Task.WaitAsync(timeout, cancellationToken);
		}
		catch (TimeoutException)
		{
			throw new TimeoutException("等待前端上传录音超时");
		}
		finally
		{
			_uploads.TryRemove(token, out _);
		}
	}

	/// <summary>清理过期条目</summary>
	private void Prune()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (KeyValuePair<string, Entry> pair in _downloads)
		{
			if (pair.Value.ExpiresAt < now) _downloads.TryRemove(pair.Key, out _);
		}
		foreach (KeyValuePair<string, Upload> pair in _uploads)
		{
			if (pair.Value.ExpiresAt >= now) continue;
			if (_uploads.TryRemove(pair.Key, out Upload? stale)) stale.Completion.TrySetCanceled();
		}
	}
}
