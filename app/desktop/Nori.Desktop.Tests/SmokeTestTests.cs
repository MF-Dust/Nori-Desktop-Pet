using System.Text.Json;
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
	public void SafeModeCanBeCombinedWithSmokeMode()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-safe-smoke-{Guid.NewGuid():N}");
		try
		{
			bool parsed = StartupOptions.TryParse(
				["--safe-mode", "--smoke-test", "initialized", "--profile", profile],
				out StartupOptions? options,
				out string error);

			Assert.True(parsed);
			Assert.Empty(error);
			Assert.NotNull(options);
			Assert.True(options!.SafeMode);
			Assert.Equal(SmokeTestMode.Initialized, options.SmokeTest!.Mode);
		}
		finally
		{
			if (Directory.Exists(profile)) Directory.Delete(profile, true);
		}
	}

	[Fact]
	public void DuplicateSafeModeIsRejected()
	{
		bool parsed = StartupOptions.TryParse(
			["--safe-mode", "--safe-mode"],
			out StartupOptions? options,
			out string error);

		Assert.False(parsed);
		Assert.Null(options);
		Assert.Contains("只能指定一次", error, StringComparison.Ordinal);
	}

	[Fact]
	public void SafeModeWithoutSmokeModeIsAccepted()
	{
		bool parsed = StartupOptions.TryParse(["--safe-mode"], out StartupOptions? options, out string error);

		Assert.True(parsed);
		Assert.Empty(error);
		Assert.NotNull(options);
		Assert.True(options!.SafeMode);
		Assert.Null(options.SmokeTest);
	}

	[Fact]
	public void SmokeModeRejectsInvalidMode()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-invalid-smoke-{Guid.NewGuid():N}");

		bool parsed = SmokeTestOptions.TryParse(
			["--smoke-test", "unsupported", "--profile", profile],
			out SmokeTestOptions? options,
			out string error);

		Assert.False(parsed);
		Assert.Null(options);
		Assert.Contains("first-run 或 initialized", error, StringComparison.Ordinal);
		Assert.False(Directory.Exists(profile));
	}

	[Fact]
	public void SmokeModeRejectsDuplicateSmokeTestArgument()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-duplicate-smoke-{Guid.NewGuid():N}");

		bool parsed = SmokeTestOptions.TryParse(
			["--smoke-test", "first-run", "--profile", profile, "--smoke-test", "initialized"],
			out SmokeTestOptions? options,
			out string error);

		Assert.False(parsed);
		Assert.Null(options);
		Assert.Contains("只能指定一次", error, StringComparison.Ordinal);
	}

	[Fact]
	public void SmokeModeRejectsMissingOrDuplicateProfile()
	{
		bool missingProfile = SmokeTestOptions.TryParse(
			["--smoke-test", "first-run"],
			out SmokeTestOptions? missingOptions,
			out string missingError);
		Assert.False(missingProfile);
		Assert.Null(missingOptions);
		Assert.Contains("--profile", missingError, StringComparison.Ordinal);

		string profile = Path.Combine(Path.GetTempPath(), $"nori-duplicate-profile-{Guid.NewGuid():N}");
		bool duplicateProfile = SmokeTestOptions.TryParse(
			["--smoke-test", "first-run", "--profile", profile, "--profile", profile + "-other"],
			out SmokeTestOptions? duplicateOptions,
			out string duplicateError);
		Assert.False(duplicateProfile);
		Assert.Null(duplicateOptions);
		Assert.Contains("只能带一个", duplicateError, StringComparison.Ordinal);
	}

	[Fact]
	public void ReadyJsonContainsStableSchemaAndSafeMode()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-ready-{Guid.NewGuid():N}");
		try
		{
			bool parsed = SmokeTestOptions.TryParse(
				["--smoke-test", "first-run", "--profile", profile],
				out SmokeTestOptions? options,
				out string error);
			Assert.True(parsed);
			Assert.Empty(error);
			SmokeTestOptions smoke = Assert.IsType<SmokeTestOptions>(options);

			SmokeTestRuntime.WriteReady(smoke, firstRun: true, safeMode: true);

			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(smoke.ReadinessPath));
			JsonElement root = document.RootElement;
			Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
			Assert.Equal(Nori.Core.ProductVersion.Current, root.GetProperty("product_version").GetString());
			Assert.Equal(NoriDatabase.DatabaseSchemaVersion, root.GetProperty("database_schema_version").GetInt64());
			Assert.Equal(Nori.Core.Configuration.ConfigStore.ConfigSchemaVersion, root.GetProperty("config_schema_version").GetInt64());
			Assert.True(root.GetProperty("safe_mode").GetBoolean());
			Assert.Equal("ready", root.GetProperty("status").GetString());
			Assert.Equal("first-run", root.GetProperty("mode").GetString());
			Assert.Equal("first-run", root.GetProperty("initial_window").GetString());
			Assert.False(File.Exists(smoke.ReadinessPath + ".tmp"));
		}
		finally
		{
			if (Directory.Exists(profile)) Directory.Delete(profile, true);
		}
	}

	[Fact]
	public void SmokeModeRejectsNonEmptyProfileWithoutDeletingIt()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-smoke-nonempty-{Guid.NewGuid():N}");
		Directory.CreateDirectory(profile);
		string sentinel = Path.Combine(profile, "sentinel.txt");
		File.WriteAllText(sentinel, "keep");
		try
		{
			bool parsed = SmokeTestOptions.TryParse(["--smoke-test", "first-run", "--profile", profile], out SmokeTestOptions? options, out string error);
			Assert.False(parsed);
			Assert.Null(options);
			Assert.Contains("完全为空", error, StringComparison.Ordinal);
			Assert.Equal("keep", File.ReadAllText(sentinel));
		}
		finally { Directory.Delete(profile, true); }
	}

	[Fact]
	public void SmokeModeRejectsExistingDatabase()
	{
		string profile = Path.Combine(Path.GetTempPath(), $"nori-smoke-test-{Guid.NewGuid():N}");
		string dataDir = Path.Combine(profile, "data");
		Directory.CreateDirectory(dataDir);
		string databaseDirectory = Path.Combine(dataDir, "core", "database");
		Directory.CreateDirectory(databaseDirectory);
		File.WriteAllText(Path.Combine(databaseDirectory, AppPaths.DatabaseFileName), "not a test database");
		try
		{
			bool parsed = SmokeTestOptions.TryParse(["--smoke-test", "first-run", "--profile", profile], out SmokeTestOptions? options, out string error);

			Assert.False(parsed);
			Assert.Null(options);
			Assert.Contains("完全为空", error, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(profile, true);
		}
	}
}
