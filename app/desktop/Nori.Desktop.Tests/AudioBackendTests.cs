using System.Text.Json;
using Nori.Core.Voice;
using Nori.Desktop.Audio;

namespace Nori.Desktop.Tests;

/// <summary>隐藏 main WebView 时音频通道仍可用的可注入后端测试。</summary>
public class AudioBackendTests
{
	[Fact]
	public async Task 播放完成回报只结束当前段并通知状态()
	{
		FakeChannel channel = new();
		WebViewAudioPlayback playback = null!;
		List<bool> states = [];
		channel.Posted = (name, payload) =>
		{
			if (name != "nori:audio-play") return;
			string token = JsonSerializer.SerializeToElement(payload!).GetProperty("token").GetString()!;
			playback.ReportPlaybackFinished(token, null);
		};
		playback = new WebViewAudioPlayback(new MediaExchange(), token => token, channel);
		playback.PlayingChanged += states.Add;

		await playback.PlayAsync(new EncodedAudio([1, 2], "audio/wav"), CancellationToken.None);

		Assert.False(playback.IsPlaying);
		Assert.Equal([true, false], states);
	}

	[Fact]
	public async Task 麦克风权限失败立即结束而非等待上传超时()
	{
		FakeChannel channel = new();
		MediaExchange media = new();
		WebViewMicrophoneRecorder recorder = new(media, token => token, channel);
		TaskCompletionSource<string> startPosted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		channel.Posted = (name, payload) =>
		{
			if (name == "nori:audio-record-start")
			{
				startPosted.TrySetResult(JsonSerializer.SerializeToElement(payload!).GetProperty("token").GetString()!);
			}
		};

		Task start = recorder.StartAsync();
		string token = await startPosted.Task.WaitAsync(TimeSpan.FromSeconds(1));
		recorder.ReportRecordingFailed(token, "用户拒绝麦克风权限");

		InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => start);
		Assert.Contains("用户拒绝", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 录音上传保留实际MIME和文件名()
	{
		FakeChannel channel = new();
		MediaExchange media = new();
		WebViewMicrophoneRecorder recorder = new(media, token => token, channel);
		TaskCompletionSource<string> startPosted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<string> stopPosted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		channel.Posted = (name, payload) =>
		{
			string token = JsonSerializer.SerializeToElement(payload!).GetProperty("token").GetString()!;
			if (name == "nori:audio-record-start") startPosted.TrySetResult(token);
			if (name == "nori:audio-record-stop") stopPosted.TrySetResult(token);
		};

		Task start = recorder.StartAsync();
		string token = await startPosted.Task.WaitAsync(TimeSpan.FromSeconds(1));
		recorder.ReportRecordingReady(token);
		await start;

		Task<RecordedAudio> stop = recorder.StopAsync();
		Assert.Equal(token, await stopPosted.Task.WaitAsync(TimeSpan.FromSeconds(1)));
		Assert.True(media.TryCompleteUpload(token, new RecordedAudio([3, 4], "audio/webm;codecs=opus", "speech.webm")));
		RecordedAudio audio = await stop;
		Assert.Equal("audio/webm;codecs=opus", audio.Mime);
		Assert.Equal("speech.webm", audio.FileName);
	}

	private sealed class FakeChannel : IAudioHostChannel
	{
		public bool IsAvailable => true;
		public Action<string, object?>? Posted { get; set; }
		public void Post(string name, object? payload) => Posted?.Invoke(name, payload);
	}
}
