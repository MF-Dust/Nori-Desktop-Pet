using Nori.Core.Automation;

namespace Nori.Core.Tests;

/// <summary>截图边界的跨平台纯函数测试。</summary>
public sealed class AutomationCaptureLimitsTests
{
	[Fact]
	public void 接受普通尺寸并计算原始大小()
	{
		Assert.True(AutomationCaptureLimits.TryGetRawByteCount(1920, 1080, out int bytes, out string? error));
		Assert.Null(error);
		Assert.Equal(1920 * 1080 * 4, bytes);
	}

	[Theory]
	[InlineData(0, 100)]
	[InlineData(100, 0)]
	[InlineData(4097, 100)]
	[InlineData(4096, 4096)]
	public void 拒绝非法或过大尺寸(int width, int height)
	{
		Assert.False(AutomationCaptureLimits.TryValidate(width, height, out string? error));
		Assert.False(string.IsNullOrWhiteSpace(error));
	}
}
