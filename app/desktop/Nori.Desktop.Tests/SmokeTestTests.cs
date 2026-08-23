using Nori.Core.Data;
using Nori.Desktop.Diagnostics;

namespace Nori.Desktop.Tests;

public sealed class SmokeTestTests
{
	[Fact]
	public void NoSmokeArgumentsLeaveModeDisabled()
	{
		bool parsed = SmokeTestOptions.TryParse([], out SmokeTestOptions? options, out string error);

		Assert.True(parsed);
		Assert.Null(options);
		Assert.Empty(error);
	}

	[Theory]
	[InlineData("first-run")]
	[InlineData("initialized")]
	public void SmokeModeRequiresAnIsolatedProfile(string mode)
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-smoke-test-{Guid.NewGuid():N}");
		try
		{
			bool parsed = SmokeTestOptions.TryParse(["--smoke-test", mode, "--profile", profile], out SmokeTestOptions? options, out string error);

			Assert.True(parsed);
			Assert.NotNull(options);
			Assert.Equal(mode, options!.Mode == SmokeTestMode.FirstRun ? "first-run" : "initialized");
			Assert.Empty(error);
			Assert.True(Directory.Exists(profile));
		}
		finally
		{
			if (Directory.Exists(profile)) Directory.Delete(profile, true);
		}
	}

	[Fact]
	public void SmokeModeRejectsExistingDatabase()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-smoke-test-{Guid.NewGuid():N}");
		string dataDir = Path.Combine(profile, "data");
		Directory.CreateDirectory(dataDir);
		File.WriteAllText(Path.Combine(dataDir, AppPaths.DatabaseFileName), "not a test database");
		try
		{
			bool parsed = SmokeTestOptions.TryParse(["--smoke-test", "first-run", "--profile", profile], out SmokeTestOptions? options, out string error);

			Assert.False(parsed);
			Assert.Null(options);
			Assert.Contains("nori.db", error, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(profile, true);
		}
	}
}
