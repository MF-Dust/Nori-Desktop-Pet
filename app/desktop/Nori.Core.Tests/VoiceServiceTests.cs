using System.Net;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>语音流水线、取消和可观察状态测试。</summary>
public class VoiceServiceTests : IDisposable
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nori-voice-service-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	public VoiceServiceTests()
	{
		_database = NoriDatabase.Open(_dbPath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_config.Set("tts_provider", new ConfigValue.Text("openai"));
		_config.Set("tts_base_url", new ConfigValue.Text("http://127.0.0.1:9880/v1"));
	}

	[Fact]
	public async Task 句子流水线先播放已完成段并最终结束()
	{
		FakePlayback playback = new();
		using HttpClient client = new(new AudioHandler());
		using VoiceService voice = new(client, _config, playback, () => null);

		await voice.SpeakAsync("第一句。第二句！");

		Assert.Equal(2, playback.Played.Count);
		Assert.All(playback.Played, audio => Assert.Equal("audio/wav", audio.Mime));
		Assert.False(voice.IsSpeaking);
	}

	[Fact]
	public async Task Stop取消合成播放并发出状态变化()
	{
		FakePlayback playback = new() {WaitForCancellation = true};
		using HttpClient client = new(new AudioHandler());
		using VoiceService voice = new(client, _config, playback, () => null);
		List<bool> states = [];
		voice.SpeakingChanged += states.Add;

		Task speak = voice.SpeakAsync("请停止这段朗读。", cancellationToken: CancellationToken.None);
		await playback.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
		voice.Stop();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speak);
		Assert.False(voice.IsSpeaking);
		Assert.Equal([true, false], states);
	}

	public void Dispose()
	{
		_database.Dispose();
		try { File.Delete(_dbPath); } catch (IOException) { }
	}

	private sealed class AudioHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = AudioContent([1, 2, 3]),
			});

		private static ByteArrayContent AudioContent(byte[] bytes)
		{
			ByteArrayContent content = new(bytes);
			content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
			return content;
		}
	}

	private sealed class FakePlayback : IAudioPlayback
	{
		public bool WaitForCancellation { get; init; }
		public List<EncodedAudio> Played { get; } = [];
		public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool IsPlaying { get; private set; }
		public event Action<bool>? PlayingChanged;
		event Action<double>? IAudioPlayback.VolumeSampled
		{
			add { }
			remove { }
		}

		public async Task PlayAsync(EncodedAudio audio, CancellationToken cancellationToken)
		{
			Played.Add(audio);
			IsPlaying = true;
			PlayingChanged?.Invoke(true);
			Started.TrySetResult(true);
			if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			IsPlaying = false;
			PlayingChanged?.Invoke(false);
		}

		public void Stop()
		{
			IsPlaying = false;
			PlayingChanged?.Invoke(false);
		}

		public void Dispose()
		{
		}
	}
}
