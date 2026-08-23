using Nori.Core.Telemetry;

namespace Nori.Core.Tests;

/// <summary>遥测操作名与凭据脱敏纯函数测试。</summary>
public sealed class TelemetrySanitizerTests
{
	[Fact]
	public void 操作名只保留稳定字符并限制长度()
	{
		string normalized = TelemetrySanitizer.NormalizeOperation("bridge.chat_start/用户消息");

		Assert.Equal("bridge.chat_start", normalized);
		Assert.True(TelemetrySanitizer.NormalizeOperation(new string('x', 200)).Length <= 80);
		Assert.Equal("operation", TelemetrySanitizer.NormalizeOperation("中文操作"));
	}

	[Fact]
	public void 普通诊断文本会移除密钥凭据和路径()
	{
		string raw = "api_key=sk-secret https://user:password@example.com/v1?token=token-secret /home/user/nori.db";
		string scrubbed = TelemetrySanitizer.ScrubText(raw);

		Assert.DoesNotContain("sk-secret", scrubbed, StringComparison.Ordinal);
		Assert.DoesNotContain("password@example", scrubbed, StringComparison.Ordinal);
		Assert.DoesNotContain("token-secret", scrubbed, StringComparison.Ordinal);
		Assert.DoesNotContain("/home/user/nori.db", scrubbed, StringComparison.Ordinal);
		Assert.Contains("[redacted]", scrubbed, StringComparison.Ordinal);
		Assert.Contains("[path]", scrubbed, StringComparison.Ordinal);
	}

	[Fact]
	public void 异常正文只保留异常类型不保留聊天内容()
	{
		InvalidOperationException exception = new("用户的聊天内容和提示词");

		string value = TelemetrySanitizer.SanitizeExceptionValue(exception);

		Assert.DoesNotContain("聊天内容", value, StringComparison.Ordinal);
		Assert.Equal(typeof(InvalidOperationException).FullName, value);
	}
}
