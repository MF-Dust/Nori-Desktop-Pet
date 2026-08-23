using Nori.Desktop.Telemetry;

namespace Nori.Desktop.Tests;

/// <summary>无 DSN 构建下 Native Sentry 的空降级测试。</summary>
public sealed class SentryTelemetryTests
{
	[Fact]
	public async Task 无DSN时启停和事务都不会出网()
	{
		using SentryTelemetry telemetry = new("", "nori@test", "test");

		Assert.False(telemetry.IsAvailable);
		telemetry.Configure(true);
		Assert.False(telemetry.IsEnabled);
		using (telemetry.StartTransaction("bridge.chat_start"))
		{
			telemetry.CaptureException(new InvalidOperationException("聊天内容"), "bridge.chat_start");
		}
		await telemetry.FlushAsync(TimeSpan.FromMilliseconds(10));
	}
}
