using Nori.Core.Voice;

namespace Nori.Desktop.Audio;

/// <summary>
/// 把音频事件推给某个 WebView 窗口的通道
///
/// 由 AppRuntime 装配, 指向 main 窗口 (关闭只隐藏, 生命周期内始终存在)。
/// </summary>
public interface IAudioHostChannel
{
	/// <summary>宿主窗口是否可用</summary>
	bool IsAvailable { get; }

	/// <summary>向音频宿主窗口推事件</summary>
	void Post(string name, object? payload);
}

/// <summary>
/// WebView 音频播放后端
///
/// 平台无关: 音频字节经 AssetServer 的一次性媒体端点交给前端, 由 WebAudio 播放,
/// 前端每 ~60ms 回传一次 RMS 音量驱动桌宠口型, 播放结束再回报一次终态。
/// 这样 Windows / macOS / Linux 共用一套代码, 不再依赖 NAudio。
/// </summary>
public sealed class WebViewAudioPlayback(MediaExchange media, Func<string, string> mediaUrl, IAudioHostChannel channel) : IAudioPlayback
{
	/// <summary>等待前端回报播放结束的上限 (兜底, 防止事件丢失把队列卡死)</summary>
	private static readonly TimeSpan PlaybackTimeout = TimeSpan.FromMinutes(5);

	private readonly object _gate = new();
	private TaskCompletionSource<bool>? _current;
	private string _currentToken = "";
	private bool _playing;
	private double _deviceVolume = 1.0;

	/// <inheritdoc />
	public bool IsPlaying => Volatile.Read(ref _playing);

	/// <inheritdoc />
	public event Action<bool>? PlayingChanged;

	/// <inheritdoc />
	public event Action<double>? VolumeSampled;

	/// <summary>设置输出音量 (0~1), 随下一段播放生效</summary>
	public void SetDeviceVolume(double volume) => Volatile.Write(ref _deviceVolume, Math.Clamp(volume, 0, 1));

	/// <inheritdoc />
	public async Task PlayAsync(byte[] data, string? mime, CancellationToken cancellationToken)
	{
		if (data.Length == 0) return;
		if (!channel.IsAvailable) throw new InvalidOperationException("音频宿主窗口不可用, 无法播放");

		Stop();

		string token = media.PublishAudio(data, mime is {Length: > 0} value ? value : "audio/mpeg");
		TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_gate)
		{
			_current = completion;
			_currentToken = token;
		}
		SetPlaying(true);

		channel.Post("nori:audio-play", new
		{
			token,
			url = mediaUrl(token),
			mime = mime ?? "audio/mpeg",
			volume = Volatile.Read(ref _deviceVolume),
		});

		try
		{
			await completion.Task.WaitAsync(PlaybackTimeout, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			channel.Post("nori:audio-stop", null);
			throw;
		}
		catch (TimeoutException)
		{
			channel.Post("nori:audio-stop", null);
		}
		finally
		{
			lock (_gate)
			{
				if (ReferenceEquals(_current, completion))
				{
					_current = null;
					_currentToken = "";
					SetPlaying(false);
				}
			}
		}
	}

	/// <inheritdoc />
	public void Stop()
	{
		TaskCompletionSource<bool>? completion;
		lock (_gate)
		{
			completion = _current;
			_current = null;
			_currentToken = "";
		}
		if (completion is null) return;
		channel.Post("nori:audio-stop", null);
		completion.TrySetResult(false);
		SetPlaying(false);
	}

	/// <summary>前端回报: 某段音频播完 / 播放失败</summary>
	public void ReportPlaybackFinished(string token, string? error)
	{
		TaskCompletionSource<bool>? completion = null;
		lock (_gate)
		{
			if (_currentToken.Length > 0 && _currentToken == token)
			{
				completion = _current;
				_current = null;
				_currentToken = "";
			}
		}
		if (completion is null) return;
		SetPlaying(false);
		if (error is {Length: > 0})
		{
			completion.TrySetException(new InvalidOperationException($"音频播放失败: {error}"));
			return;
		}
		completion.TrySetResult(true);
	}

	/// <summary>前端回报的实时音量 (0~1), 直接驱动桌宠口型</summary>
	public void ReportLevel(double level) => VolumeSampled?.Invoke(Math.Clamp(level, 0, 1));

	private void SetPlaying(bool value)
	{
		if (Volatile.Read(ref _playing) == value) return;
		Volatile.Write(ref _playing, value);
		PlayingChanged?.Invoke(value);
	}

	public void Dispose() => Stop();
}

/// <summary>
/// WebView 麦克风录音后端
///
/// 前端用 MediaRecorder 采集, 结束后把音频 POST 回一次性媒体端点;
/// 全流程异步, 绝不在 UI 线程上等待。
/// </summary>
public sealed class WebViewMicrophoneRecorder(MediaExchange media, Func<string, string> mediaUrl, IAudioHostChannel channel) : IMicrophoneRecorder
{
	/// <summary>等待前端上传录音的上限</summary>
	private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(20);

	private string _token = "";

	/// <inheritdoc />
	public bool IsRecording => _token.Length > 0;

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (!channel.IsAvailable) throw new InvalidOperationException("音频宿主窗口不可用, 无法录音");
		if (IsRecording) return Task.CompletedTask;

		string token = media.CreateUploadTicket();
		_token = token;
		channel.Post("nori:audio-record-start", new {token, uploadUrl = mediaUrl(token)});
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<byte[]> StopAsync(CancellationToken cancellationToken = default)
	{
		string token = _token;
		_token = "";
		if (token.Length == 0) return [];

		channel.Post("nori:audio-record-stop", new {token});
		try
		{
			return await media.WaitForUploadAsync(token, UploadTimeout, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			media.CancelUpload(token);
			return [];
		}
	}

	public void Dispose()
	{
		if (_token.Length == 0) return;
		media.CancelUpload(_token);
		_token = "";
	}
}

/// <summary>
/// 指向某个 WebView 窗口的音频事件通道
///
/// 音频宿主固定为 main 窗口: 它关闭只隐藏、进程内始终存在, 因此隐藏时依然能放声。
/// </summary>
public sealed class AudioHostChannel(Func<Nori.Desktop.Windows.NoriWindow?> resolve) : IAudioHostChannel
{
	/// <inheritdoc />
	public bool IsAvailable => resolve() is not null;

	/// <inheritdoc />
	public void Post(string name, object? payload)
	{
		Nori.Desktop.Windows.NoriWindow? window = resolve();
		window?.PostEvent(name, payload);
	}
}
