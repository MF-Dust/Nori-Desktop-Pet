using System.Net;
using Nori.Core.Network;

namespace Nori.Core.Tests;

public class UrlAccessPolicyTests
{
	[Theory]
	[InlineData("https://example.com/page")]
	[InlineData("http://api.anysearch.com/v1/search")]
	public void 公网地址允许(string url) =>
		UrlAccessPolicy.EnsurePublicHttp(new Uri(url));

	[Theory]
	[InlineData("ftp://example.com/file", "仅支持 http/https")]
	[InlineData("file:///etc/passwd", "仅支持 http/https")]
	[InlineData("http://127.0.0.1:8080/api", "私网或保留地址")]
	[InlineData("http://localhost/x", "私网或保留地址")]
	[InlineData("http://169.254.1.1/metadata", "私网或保留地址")]
	[InlineData("http://192.168.1.10/router", "私网或保留地址")]
	[InlineData("http://10.0.0.5/internal", "私网或保留地址")]
	[InlineData("http://172.16.0.9/internal", "私网或保留地址")]
	[InlineData("http://[::1]/v6", "私网或保留地址")]
	[InlineData("http://224.0.0.1/group", "私网或保留地址")]
	public void 危险地址被拒绝(string url, string reason)
	{
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(
			() => UrlAccessPolicy.EnsurePublicHttp(new Uri(url)));
		Assert.Contains(reason, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 允许私网时本地端点放行()
	{
		// 本地 LLM / GPT-SoVITS 端点走 allowPrivate
		UrlAccessPolicy.EnsureAllowed(new Uri("http://127.0.0.1:9880/tts"), allowPrivate: true);
		UrlAccessPolicy.EnsureAllowed(new Uri("http://localhost:11434/api"), allowPrivate: true);
	}

	[Fact]
	public void IPv4映射的IPv6回环按IPv4规则拒绝() =>
		Assert.Throws<InvalidOperationException>(() =>
			UrlAccessPolicy.EnsurePublicHttp(new Uri("http://[::ffff:127.0.0.1]/x")));

	[Fact]
	public void 内部判定_回环与未指定地址受限()
	{
		Assert.True(UrlAccessPolicy.IsRestricted(IPAddress.Loopback));
		Assert.True(UrlAccessPolicy.IsRestricted(IPAddress.IPv6Loopback));
		Assert.True(UrlAccessPolicy.IsRestricted(IPAddress.Any));
		Assert.False(UrlAccessPolicy.IsRestricted(IPAddress.Parse("8.8.8.8")));
		Assert.False(UrlAccessPolicy.IsRestricted(IPAddress.Parse("172.32.0.1")));
	}

	[Fact]
	public void 系统代理接管的公网请求默认被拒绝()
	{
		try
		{
			UrlAccessPolicy.PublicSystemProxyAllowed = false;
			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
				UrlAccessPolicy.EnsureDirectRoute(new Uri("https://api.anysearch.com/v1/search"), AlwaysProxy()));
			Assert.Contains("系统代理", exception.Message);
		}
		finally
		{
			UrlAccessPolicy.PublicSystemProxyAllowed = false;
		}
	}

	[Fact]
	public void 放行公网系统代理后不再拒绝()
	{
		try
		{
			UrlAccessPolicy.PublicSystemProxyAllowed = true;
			UrlAccessPolicy.EnsureDirectRoute(new Uri("https://api.anysearch.com/v1/search"), AlwaysProxy());
		}
		finally
		{
			UrlAccessPolicy.PublicSystemProxyAllowed = false;
		}
	}

	private static IWebProxy AlwaysProxy() => new AlwaysProxyStub();

	private sealed class AlwaysProxyStub : IWebProxy
	{
		public Uri? GetProxy(Uri destination) => new("http://127.0.0.1:7890");
		public bool IsBypassed(Uri host) => false;
		public ICredentials? Credentials { get; set; }
	}
}
