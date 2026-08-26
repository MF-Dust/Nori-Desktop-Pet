using Nori.Desktop.Automation.Browser;

namespace Nori.Desktop.Tests;

public sealed class PlaywrightRuntimeAvailabilityTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"nori-playwright-runtime-{Guid.NewGuid():N}");

	[Fact]
	public void 缺少driver目录时报告未安装()
	{
		Directory.CreateDirectory(_root);
		Assert.False(PlaywrightRuntimeAvailability.IsAvailable(_root));
	}

	[Fact]
	public void package和node同时存在时报告可用()
	{
		Directory.CreateDirectory(Path.Combine(_root, ".playwright", "package"));
		Directory.CreateDirectory(Path.Combine(_root, ".playwright", "node"));
		Assert.True(PlaywrightRuntimeAvailability.IsAvailable(_root));
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}
