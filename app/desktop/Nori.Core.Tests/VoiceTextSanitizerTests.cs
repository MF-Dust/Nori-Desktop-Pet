using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>TTS 文本清洗（颜文字/装饰符号移除）测试，行为对齐 IndexTTS 技能 tts_cli。</summary>
public class VoiceTextSanitizerTests
{
	[Theory]
	[InlineData("小卡～新版TTS升级完成啦(๑•̀ㅂ•́)و✧", "小卡～新版TTS升级完成啦")]
	[InlineData("今天心情不错(づ￣3￣)づ╭❤～", "今天心情不错")]
	[InlineData("好耶(^_^)", "好耶")]
	[InlineData("别这样(T_T)", "别这样")]
	[InlineData("啊咧(>_<)", "啊咧")]
	[InlineData("随便(・_・)", "随便")]
	[InlineData("嗯嗯(｡･ω･｡)", "嗯嗯")]
	public void 括号颜文字被移除(string input, string expected)
	{
		Assert.Equal(expected, VoiceTextSanitizer.StripKaomoji(input));
	}

	[Theory]
	[InlineData("（开心）", "（开心）")]
	[InlineData("（重要）", "（重要）")]
	[InlineData("(メニュー)", "(メニュー)")]
	[InlineData("(x86)", "(x86)")]
	[InlineData("（银河）", "（银河）")]
	public void 正常括号不被误删(string input, string expected)
	{
		Assert.Equal(expected, VoiceTextSanitizer.StripKaomoji(input));
	}

	[Theory]
	[InlineData("好耶✧", "好耶")]
	[InlineData("谢谢啦❤", "谢谢啦")]
	[InlineData("晚安～♪", "晚安～")]
	[InlineData("开心✨", "开心")]
	public void 尾部硬装饰被移除但语气波浪保留(string input, string expected)
	{
		Assert.Equal(expected, VoiceTextSanitizer.StripKaomoji(input));
	}

	[Fact]
	public void 多个颜文字连排全部移除()
	{
		Assert.Equal("今天天气不错", VoiceTextSanitizer.StripKaomoji("今天(^_^)天气(≧▽≦)不错(๑˃̵ᴗ˂̵)"));
	}

	[Fact]
	public void 全颜文字文本清洗后为空()
	{
		Assert.Equal("", VoiceTextSanitizer.StripKaomoji("(๑•̀ㅂ•́)و✧"));
	}

	[Fact]
	public void 无颜文字文本原样返回()
	{
		const string input = "今天天气真好呀，我们一起去散步吧？";
		Assert.Equal(input, VoiceTextSanitizer.StripKaomoji(input));
	}

	[Fact]
	public void 空串与null安全()
	{
		Assert.Equal("", VoiceTextSanitizer.StripKaomoji(""));
		Assert.Equal("", VoiceTextSanitizer.StripKaomoji(null));
	}
}
