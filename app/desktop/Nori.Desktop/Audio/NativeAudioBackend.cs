using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Nori.Desktop.Audio;

/// <summary>
/// Windows 原生音频后端 (NAudio)
///
/// 实现 Core 侧 IAudioPlayback / IMicrophoneRecorder:
/// - 播放: MediaFoundation 解码 (mp3/wav) → 音量 → RMS 计量 → WaveOut
///   计量采样经节流后抛出 VolumeSampled, 驱动桌宠口型同步
/// - 录音: WaveIn 16kHz/16bit/单声道, 停止时封装为完整 WAV 字节
/// 非 Windows 平台构造返回 null, 由调用方降级。
/// </summary>
public sealed class NativeAudioPlayback : Nori.Core.Voice.IAudioPlayback
{
	/// <summary>口型采样节流间隔</summary>
	private const int MouthSampleIntervalMs = 60;

	private readonly object _gate = new();
	private WaveOutEvent? _output;
	private CancellationTokenSource? _playCts;
	private bool _playing;
	private long _lastMouthSampleAt;

	/// <inheritdoc />
	public bool IsPlaying => Volatile.Read(ref _playing);

	/// <inheritdoc />
	public event Action<bool>? PlayingChanged;

	/// <inheritdoc />
	public event Action<double>? VolumeSampled;

	/// <inheritdoc />
	public Task PlayAsync(byte[] data, string? mime, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("原生音频仅支持 Windows");
		ObjectDisposedException.ThrowIf(_disposed, this);

		Stop();
		CancellationTokenSource playCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_playCts = playCts;
		Volatile.Write(ref _playing, true);
		PlayingChanged?.Invoke(true);

		return Task.Run(async () =>
		{
			string tempPath = Path.Combine(Path.GetTempPath(), $"nori-tts-{Guid.NewGuid():N}.audio");
			try
			{
				await File.WriteAllBytesAsync(tempPath, data, CancellationToken.None);
				using WaveStream reader = new MediaFoundationReader(tempPath);
				ISampleProvider samples = reader.ToSampleProvider();
				CustomMetering meter = new(samples, OnLevel);
				using WaveOutEvent output = new();
				_output = output;
				output.Init(meter.ToWaveProvider());
				output.Play();
				while (output.PlaybackState == PlaybackState.Playing && !playCts.Token.IsCancellationRequested)
				{
					Thread.Sleep(40);
				}
				output.Stop();
			}
			finally
			{
				try { File.Delete(tempPath); } catch { /* 临时文件清理失败忽略 */ }
				lock (_gate) _output = null;
				Volatile.Write(ref _playing, false);
				VolumeSampled?.Invoke(0);
				PlayingChanged?.Invoke(false);
			}
		}, CancellationToken.None);
	}

	private void OnLevel(float level)
	{
		long now = Environment.TickCount64;
		if (now - Interlocked.Read(ref _lastMouthSampleAt) < MouthSampleIntervalMs) return;
		Interlocked.Exchange(ref _lastMouthSampleAt, now);

		double value = Math.Clamp(Math.Abs(level), 0, 1);
		if (!_playing) return;
		VolumeSampled?.Invoke(value);
	}

	/// <summary>
	/// 自带 RMS/峰值计量的采样提供器
	/// (不依赖 MeteringSampleProvider 的事件签名, 版本兼容性更好)
	/// </summary>
	private sealed class CustomMetering(ISampleProvider source, Action<float> onLevel) : ISampleProvider
	{
		public WaveFormat WaveFormat => source.WaveFormat;

		public int Read(float[] buffer, int offset, int count)
		{
			int read = source.Read(buffer, offset, count);
			float max = 0;
			for (int index = offset; index < offset + read; index++)
			{
				float abs = MathF.Abs(buffer[index]);
				if (abs > max) max = abs;
			}
			if (read > 0) onLevel(max);
			return read;
		}
	}

	/// <inheritdoc />
	public void Stop()
	{
		try
		{
			_playCts?.Cancel();
		}
		catch
		{
			// 已释放时忽略
		}
	}

	/// <summary>
	/// 设置输出设备音量 (0.0 ~ 1.0), 由运行时在全局音量变化时调用
	/// </summary>
	public void SetDeviceVolume(double volume)
	{
		WaveOutEvent? output;
		lock (_gate) output = _output;
		if (output is not null)
		{
			try
			{
				output.Volume = Math.Clamp((float)volume, 0f, 1f);
			}
			catch
			{
				// 部分设备不支持软件音量时忽略
			}
		}
	}

	private bool _disposed;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Stop();
		WaveOutEvent? output;
		lock (_gate) output = _output;
		try
		{
			output?.Stop();
			output?.Dispose();
		}
		catch
		{
			// 设备已被系统回收时忽略
		}
		_playCts?.Dispose();
	}
}

/// <summary>
/// 麦克风录音器 (每次 Start/Stop 创建一个实例)
/// </summary>
public sealed class NativeMicrophoneRecorder : Nori.Core.Voice.IMicrophoneRecorder
{
	private WaveFileWriter? _writer;
	private WaveInEvent? _waveIn;
	private MemoryStream? _stream;

	public bool IsRecording { get; private set; }

	public void Start()
	{
		if (IsRecording) return;
		if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("麦克风录音仅支持 Windows");

		_stream = new MemoryStream();
		_waveIn = new WaveInEvent
		{
			WaveFormat = new WaveFormat(16000, 16, 1),
			BufferMilliseconds = 100,
		};
		_writer = new WaveFileWriter(_stream, _waveIn.WaveFormat);
		_waveIn.DataAvailable += (_, args) =>
		{
			try
			{
				_writer?.Write(args.Buffer, 0, args.BytesRecorded);
			}
			catch
			{
				// 写入失败不影响采集
			}
		};
		_waveIn.RecordingStopped += (_, _) => IsRecording = false;
		_waveIn.StartRecording();
		IsRecording = true;
	}

	public byte[] Stop()
	{
		if (!IsRecording && _writer is null) return [];
		try
		{
			_waveIn?.StopRecording();
			_writer?.Flush();
		}
		catch
		{
			// 设备拔出等异常时尽力返回已录内容
		}
		byte[] bytes = _stream?.ToArray() ?? [];
		Cleanup();
		return bytes;
	}

	private void Cleanup()
	{
		try
		{
			_writer?.Dispose();
			_waveIn?.Dispose();
			_stream?.Dispose();
		}
		catch
		{
			// 忽略释放异常
		}
		_writer = null;
		_waveIn = null;
		_stream = null;
	}

	public void Dispose() => Cleanup();
}
