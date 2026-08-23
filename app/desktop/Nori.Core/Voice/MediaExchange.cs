using System.Collections.Concurrent;

namespace Nori.Core.Voice;

/// <summary>
/// 一次性媒体交换所。
///
/// 音频不走 JSON 桥: TTS 字节在这里登记一次性 token，前端拿着
/// `/{prefix}/media/{token}` 直接下载播放；麦克风录音反向走同一套 token 上传。
/// </summary>
public sealed class MediaExchange
{
	/// <summary>token 有效期。</summary>
	public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

	private sealed record Entry(EncodedAudio Audio, DateTimeOffset ExpiresAt);

	private sealed class Upload
	{
		public TaskCompletionSource<RecordedAudio> Completion { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public DateTimeOffset ExpiresAt { get; init; }
	}

	private readonly ConcurrentDictionary<string, Entry> _downloads = new();
	private readonly ConcurrentDictionary<string, Upload> _uploads = new();
	private readonly TimeSpan _ttl;

	/// <summary>使用默认两分钟过期时间创建交换所。</summary>
	public MediaExchange(TimeSpan? ttl = null)
	{
		_ttl = ttl ?? Ttl;
		if (_ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
	}

	private static string NewToken() =>
		Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

	/// <summary>登记一段待播放音频，返回一次性 token。</summary>
	public string PublishAudio(EncodedAudio audio)
	{
		EncodedAudio validated = AudioMime.ValidateEncoded(audio.Bytes, audio.Mime);
		Prune();
		string token = NewToken();
		_downloads[token] = new Entry(validated, DateTimeOffset.UtcNow + _ttl);
		return token;
	}

	/// <summary>兼容调用方登记带 MIME 的音频。</summary>
	public string PublishAudio(byte[] data, string mime) => PublishAudio(AudioMime.ValidateEncoded(data, mime));

	/// <summary>
	/// 取走音频 (取走即删)；token 无效或已过期返回 false。
	/// </summary>
	public bool TryTakeAudio(string token, out byte[] data, out string mime)
	{
		data = [];
		mime = "application/octet-stream";
		if (!_downloads.TryRemove(token, out Entry? entry)) return false;
		if (entry.ExpiresAt < DateTimeOffset.UtcNow) return false;
		data = entry.Audio.Bytes;
		mime = entry.Audio.Mime;
		return true;
	}

	/// <summary>开一张上传票据 (给前端录音用)，返回 token。</summary>
	public string CreateUploadTicket()
	{
		Prune();
		string token = NewToken();
		_uploads[token] = new Upload {ExpiresAt = DateTimeOffset.UtcNow + _ttl};
		return token;
	}

	/// <summary>完成一次带 MIME 的录音上传；token 无效或内容无效返回 false。</summary>
	public bool TryCompleteUpload(string token, RecordedAudio audio)
	{
		if (!_uploads.TryGetValue(token, out Upload? upload)) return false;
		if (upload.ExpiresAt < DateTimeOffset.UtcNow)
		{
			if (_uploads.TryRemove(token, out Upload? expired)) expired.Completion.TrySetCanceled();
			return false;
		}

		try
		{
			RecordedAudio validated = AudioMime.ValidateRecorded(audio.Bytes, audio.Mime, audio.FileName);
			return upload.Completion.TrySetResult(validated);
		}
		catch (InvalidOperationException)
		{
			// 由 HTTP 层通过 TryFailUpload 把原因立即通知等待方。
			return false;
		}
	}

	/// <summary>
	/// 兼容旧测试与内部调用的字节上传入口；真实 MediaRecorder 路径必须传入 RecordedAudio。
	/// </summary>
	public bool TryCompleteUpload(string token, byte[] data) =>
		TryCompleteUpload(token, new RecordedAudio(data, "audio/wav", AudioMime.FileNameFor("audio/wav")));

	/// <summary>让等待方立即收到前端权限或上传失败，而不是等超时。</summary>
	public bool TryFailUpload(string token, string error)
	{
		if (!_uploads.TryGetValue(token, out Upload? upload)) return false;
		return upload.Completion.TrySetException(new InvalidOperationException(
			string.IsNullOrWhiteSpace(error) ? "前端录音上传失败" : $"前端录音上传失败: {error}"));
	}

	/// <summary>放弃一张票据 (录音失败/取消)。</summary>
	public void CancelUpload(string token)
	{
		if (_uploads.TryRemove(token, out Upload? upload)) upload.Completion.TrySetCanceled();
	}

	/// <summary>等待带 MIME 的录音上传结果。</summary>
	public async Task<RecordedAudio> WaitForRecordedUploadAsync(
		string token, TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		if (!_uploads.TryGetValue(token, out Upload? upload)) throw new InvalidOperationException("上传票据不存在或已失效");
		if (upload.ExpiresAt < DateTimeOffset.UtcNow)
		{
			if (_uploads.TryRemove(token, out Upload? expired)) expired.Completion.TrySetCanceled();
			throw new InvalidOperationException("上传票据已过期");
		}
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

	/// <summary>兼容旧调用方，仅取回录音字节。</summary>
	public async Task<byte[]> WaitForUploadAsync(string token, TimeSpan timeout, CancellationToken cancellationToken = default) =>
		(await WaitForRecordedUploadAsync(token, timeout, cancellationToken)).Bytes;

	/// <summary>清理过期条目。</summary>
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
