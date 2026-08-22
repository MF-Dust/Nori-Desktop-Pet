using Nori.Core.Configuration;

namespace Nori.Core.Voice;

/// <summary>
/// 全局语音服务
///
/// 职责:
/// - 按配置选择 TTS 提供商合成并经原生播放后端播放 (串行队列)
/// - 全局音量读写与持久化 (audio_volume)
/// - Whisper 录音识别入口
/// - 旧浏览器语音配置的停用检测
///
/// 后端化后不再提供 Web Speech / 浏览器 Edge-TTS 路径;
/// 命中旧配置时抛出可读错误并由 UI 引导迁移。
/// </summary>
public sealed class VoiceService(HttpClient httpClient, ConfigStore config, IAudioPlayback? playback, Func<IMicrophoneRecorder?> recorderFactory) : IDisposable
{
	/// <summary>已停用的浏览器语音提供商集合</summary>
	public static readonly IReadOnlySet<string> RetiredProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "web_speech", "edge_tts" };

	private readonly SemaphoreSlim _queue = new(1, 1);

	/// <summary>音量变化通知</summary>
	public event Action<double>? VolumeChanged;

	// ---- 音量 ----

	/// <summary>读取全局音量 (0.0 ~ 1.0)</summary>
	public double GetVolume()
	{
		string raw = config.GetStringOr("audio_volume", "1");
		if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
		{
			return 1.0;
		}
		return Math.Clamp(value, 0, 1);
	}

	/// <summary>设置全局音量并持久化</summary>
	public void SetVolume(double volume)
	{
		double clamped = Math.Clamp(volume, 0, 1);
		config.Set("audio_volume", new ConfigValue.Text(clamped.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture)));
		VolumeChanged?.Invoke(clamped);
	}

	// ---- 播放状态 ----

	/// <summary>是否正在播放</summary>
	public bool IsSpeaking => playback?.IsPlaying ?? false;

	/// <summary>停止朗读并清空队列</summary>
	public void Stop() => playback?.Stop();

	// ---- 合成与播放 ----

	/// <summary>解析当前配置的 TTS 提供商名</summary>
	public string ResolveProviderName() =>
		config.GetStringOr("tts_provider", "openai") is {Length: > 0} saved ? saved : "openai";

	/// <summary>
	/// 朗读文本: 合成后推入播放队列 (串行)
	/// </summary>
	public async Task SpeakAsync(string text, TtsSynthesizeOptions? options = null, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		IAudioPlayback player = playback ?? throw new InvalidOperationException("音频播放后端不可用");

		byte[] audio = await SynthesizeAsync(text, options, cancellationToken);
		await _queue.WaitAsync(cancellationToken);
		try
		{
			await player.PlayAsync(audio, null, cancellationToken);
		}
		finally
		{
			_queue.Release();
		}
	}

	/// <summary>仅合成不播放 (测试/预检用)</summary>
	public async Task<byte[]> SynthesizeAsync(string text, TtsSynthesizeOptions? options = null, CancellationToken cancellationToken = default)
	{
		string providerName = ResolveProviderName();
		ITtsProvider provider = CreateProvider(providerName);
		TtsSynthesizeOptions merged = new()
		{
			Voice = options?.Voice is {Length: > 0} voice ? voice : config.GetStringOr("tts_voice", "") is {Length: > 0} saved ? saved : null,
			Speed = options?.Speed is > 0 ? options.Speed : ReadDoubleConfig("tts_speed", 1.0),
		};
		return await provider.SynthesizeAsync(text.Trim(), merged, cancellationToken);
	}

	/// <summary>按名称构造 TTS 提供商; 已停用的浏览器路径给出明确错误</summary>
	public ITtsProvider CreateProvider(string name)
	{
		if (RetiredProviders.Contains(name))
		{
			throw new InvalidOperationException($"语音提供商 {name} 依赖浏览器能力, 已在纯后端版本中停用, 请改用 OpenAI / 自定义 HTTP / GPT-SoVITS");
		}
		return name switch
		{
			"gpt_sovits" => new GptSoVitsTtsProvider(httpClient, config),
			"custom" => new CustomHttpTtsProvider(httpClient, config),
			_ => new OpenAiTtsProvider(httpClient, config),
		};
	}

	// ---- 录音识别 ----

	/// <summary>开始录音</summary>
	public void StartListening()
	{
		IMicrophoneRecorder? recorder = recorderFactory() ?? throw new InvalidOperationException("麦克风录音后端不可用");
		recorder.Start();
	}

	/// <summary>结束录音并经 Whisper 识别返回文本</summary>
	public async Task<string> StopListeningAndTranscribeAsync(CancellationToken cancellationToken = default)
	{
		IMicrophoneRecorder? recorder = recorderFactory() ?? throw new InvalidOperationException("麦克风录音后端不可用");
		if (!recorder.IsRecording) return "";
		byte[] wav = recorder.Stop();
		if (wav.Length == 0) return "";

		// stt_provider=whisper 或未显式配置时都走 Whisper (唯一的云端 STT 路径)
		return await new WhisperSttProvider(httpClient, config).TranscribeAsync(wav, cancellationToken);
	}

	// ---- 迁移检测 ----

	/// <summary>
	/// 检测旧版浏览器语音配置是否需要一次性提示。
	/// 不删除原配置: 用户后续切回云端提供商时其余字段仍然可用。
	/// </summary>
	public bool HasRetiredVoiceConfig() =>
		RetiredProviders.Contains(ResolveProviderName())
		|| RetiredProviders.Contains(config.GetStringOr("stt_provider", ""));

	/// <summary>读取数值配置, 非法时回退</summary>
	private double ReadDoubleConfig(string key, double fallback) =>
		double.TryParse(
			config.GetStringOr(key, ""),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out double value) && value > 0 ? value : fallback;

	public void Dispose()
	{
		playback?.Dispose();
		_queue.Dispose();
	}
}
