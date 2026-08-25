using Nori.Desktop.Automation.Browser;

namespace Nori.Desktop.Tests;

public sealed class BrowserAutomationPolicyTests
{
	[Theory]
	[InlineData("https://example.com")]
	[InlineData("http://localhost:8080/test")]
	public void 只允许http地址(string value)
	{
		Uri uri = BrowserAutomationPolicy.ValidateNavigation(value);
		Assert.Equal(new Uri(value).Host, uri.Host);
		Assert.StartsWith($"{new Uri(value).Scheme}://", uri.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("file:///C:/secret.txt")]
	[InlineData("javascript:alert(1)")]
	[InlineData("https://user:password@example.com")]
	[InlineData("")]
	public void 拒绝危险或无效地址(string value)
	{
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateNavigation(value));
	}

	[Fact]
	public void 文本输入必须确认且限长()
	{
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateInput("secret", false));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateInput(new string('x', BrowserAutomationPolicy.MaxInputCharacters + 1), true));
		Assert.Equal("hello", BrowserAutomationPolicy.ValidateInput("hello", true));
	}

	[Fact]
	public void 操作参数有边界()
	{
		Assert.Equal(200, BrowserAutomationPolicy.ValidateScroll(200));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateScroll(0));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateScroll(BrowserAutomationPolicy.MaxScrollPixels + 1));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateWait(BrowserAutomationPolicy.MaxWaitMilliseconds + 1));
	}

	[Fact]
	public void 截图只接受限制内存数据()
	{
		byte[] data = [1, 2, 3];
		Assert.Same(data, BrowserAutomationPolicy.ValidateScreenshot(data));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateScreenshot([]));
		Assert.Throws<InvalidOperationException>(() => BrowserAutomationPolicy.ValidateScreenshot(new byte[BrowserAutomationPolicy.MaxScreenshotBytes + 1]));
	}
}
