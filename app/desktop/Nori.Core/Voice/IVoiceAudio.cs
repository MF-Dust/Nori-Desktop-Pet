namespace Nori.Core.Voice;

/// <summary>
/// TTS 合成选项
/// </summary>
public sealed record TtsSynthesizeOptions
{
	/// <summary>朗读音色</summary>
	public string? Voice { get; init; }

	/// <summary>语速 (1.0 为常速)</summary>
	public double Speed { get; init; } = 1.0;
}

/// <summary>
/// TTS 提供商接口: 云端合成返回音频字节 (mp3/wav 由端点决定)
///
/// 后端化后仅保留云端/HTTP 路径; 浏览器 Web Speech 与浏览器 Edge-TTS 已移除。
/// </summary>
public interface ITtsProvider
{
	/// <summary>提供商名 (对应配置键 tts_provider 的取值)</summary>
	string Name { get; }

	/// <summary>合成文本并返回音频字节</summary>
	Task<byte[]> SynthesizeAsync(string text, TtsSynthesizeOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// 原生音频播放接口 (Desktop 侧以 NAudio 实现)
///
/// 播放期间通过 VolumeSampled 输出 0~1 音量采样驱动桌宠口型,
/// SpeakingChanged 通知说话状态变化。
/// </summary>
public interface IAudioPlayback : IDisposable
{
	/// <summary>是否正在播放</summary>
	bool IsPlaying { get; }

	/// <summary>播放状态变化</summary>
	event Action<bool>? PlayingChanged;

	/// <summary>音量采样 (0.0 ~ 1.0)</summary>
	event Action<double>? VolumeSampled;

	/// <summary>阻塞式播放一段音频 (mp3/wav)</summary>
	Task PlayAsync(byte[] data, string? mime, CancellationToken cancellationToken);

	/// <summary>停止当前播放并清空队列</summary>
	void Stop();
}

/// <summary>
/// 麦克风录音接口 (Desktop 侧以 NAudio 实现)
/// </summary>
public interface IMicrophoneRecorder : IDisposable
{
	/// <summary>开始录制 (16kHz 单声道 WAV)</summary>
	void Start();

	/// <summary>停止录制并返回完整 WAV 字节</summary>
	byte[] Stop();

	/// <summary>是否正在录制</summary>
	bool IsRecording { get; }
}
