using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>语音格式、句子切分与合成缓存的纯逻辑测试。</summary>
public class VoiceAudioTests
{
	[Theory]
	[InlineData("audio/webm;codecs=opus")]
	[InlineData("audio/mpeg")]
	[InlineData("audio/wav")]
	public void MIME校验保留实际格式参数(string mime)
	{
		Assert.Equal(mime, AudioMime.Validate(mime));
		EncodedAudio audio = AudioMime.ValidateEncoded([1, 2, 3], mime);
		Assert.Equal(mime, audio.Mime);
	}

	[Theory]
	[InlineData("")]
	[InlineData("application/octet-stream")]
	[InlineData("text/plain")]
	public void 不支持的MIME被拒绝(string mime)
	{
		Assert.Throws<InvalidOperationException>(() => AudioMime.Validate(mime));
	}

	[Fact]
	public void 空音频和超大音频被拒绝()
	{
		Assert.Throws<InvalidOperationException>(() => AudioMime.ValidateEncoded([], "audio/wav"));
		Assert.Throws<InvalidOperationException>(() => AudioMime.ValidateEncoded(new byte[VoiceAudioLimits.MaxBytes + 1], "audio/wav"));
	}

	[Fact]
	public void 句子按标点拆分并限制长度()
	{
		Assert.Equal(["你好呀！", "今天想聊什么？"], SentenceChunker.Split("你好呀！今天想聊什么？"));
		IReadOnlyList<string> chunks = SentenceChunker.Split("abcdefghij", 3);
		Assert.Equal(["abc", "def", "ghi", "j"], chunks);
		Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 3));
	}

	[Fact]
	public void 合成缓存按16项和32MiB淘汰最旧项()
	{
		AudioSynthesisCache cache = new();
		for (int index = 0; index < 16; index++)
		{
			string key = $"key-{index}";
			cache.Put(key, new EncodedAudio([1], "audio/wav"));
		}
		Assert.True(cache.TryGet("key-0", out _));
		cache.Put("key-new", new EncodedAudio([2], "audio/wav"));

		Assert.Equal(16, cache.Count);
		Assert.True(cache.TryGet("key-0", out _));
		Assert.False(cache.TryGet("key-1", out _));
		Assert.True(cache.TryGet("key-new", out _));

		cache.Put("too-large", new EncodedAudio(new byte[VoiceAudioLimits.MaxBytes + 1], "audio/wav"));
		Assert.Equal(16, cache.Count);
		Assert.False(cache.TryGet("too-large", out _));
	}
}
