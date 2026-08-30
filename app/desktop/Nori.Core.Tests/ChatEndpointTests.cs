using Nori.Core.Chat;

namespace Nori.Core.Tests;

/// <summary>聊天端点地址严格校验测试 (NORI-14: UriFormatException 必须转成领域错误)。</summary>
public sealed class ChatEndpointTests
{
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-a-url")]
	[InlineData("api.example.com/v1")]
	[InlineData("ftp://api.example.com")]
	[InlineData("file:///C:/Windows")]
	[InlineData("http://")]
	public void 非法地址抛ChatException而不是UriFormatException(string endpoint)
	{
		ChatException error = Assert.Throws<ChatException>(() => ChatEndpoint.CreateHttpUri(endpoint));
		Assert.Contains("Base URL 格式无效", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 本地路径形式的地址被拒绝且错误消息脱敏()
	{
		ChatException error = Assert.Throws<ChatException>(() =>
			ChatEndpoint.CreateHttpUri(@"C:\Users\me\secret\config.json"));

		Assert.Contains("Base URL 格式无效", error.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("https://api.openai.com/v1", "https", "api.openai.com")]
	[InlineData("http://127.0.0.1:9880", "http", "127.0.0.1")]
	public void 合法的http与https地址被接受(string endpoint, string scheme, string host)
	{
		Uri uri = ChatEndpoint.CreateHttpUri(endpoint);

		Assert.Equal(scheme, uri.Scheme);
		Assert.Equal(host, uri.Host);
	}

	[Fact]
	public void 绝对路径与非http协议返回False()
	{
		Assert.False(ChatEndpoint.TryCreateHttpUri(null, out _));
		Assert.False(ChatEndpoint.TryCreateHttpUri("/relative/path", out _));
		Assert.False(ChatEndpoint.TryCreateHttpUri("wss://example.com", out _));
	}
}
