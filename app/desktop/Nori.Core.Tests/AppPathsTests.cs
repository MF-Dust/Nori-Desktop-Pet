using Nori.Core.Data;

namespace Nori.Core.Tests;

/// <summary>
/// 数据目录：三平台落点锁定
///
/// 这些路径必须与 Tauri 版 app_data_dir() 完全一致，改了就等于让老用户的
/// nori.db 与本地模型「凭空消失」。Windows 的值尤其不允许漂移。
/// </summary>
public class AppPathsTests
{
	[Fact]
	public void 应用标识不变()
	{
		Assert.Equal("cn.erhio.noriDesktopPet", AppPaths.Identifier);
	}

	[Fact]
	public void 数据目录结构固定为标识加data()
	{
		string dataDir = AppPaths.DataDir;

		Assert.EndsWith(Path.Combine(AppPaths.Identifier, "data"), dataDir, StringComparison.Ordinal);
		Assert.Equal(Path.Combine(dataDir, "nori.db"), AppPaths.DatabasePath);
		Assert.Equal(Path.Combine(dataDir, "resources"), AppPaths.ResourcesDir);
		Assert.Equal(Path.Combine(dataDir, "log"), AppPaths.LogDir);
	}

	[Fact]
	public void Windows落在APPDATA下()
	{
		if (!OperatingSystem.IsWindows()) return;
		string expected = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			AppPaths.Identifier, "data");
		Assert.Equal(expected, AppPaths.DataDir);
	}

	[Fact]
	public void macOS落在LibraryApplicationSupport下()
	{
		if (!OperatingSystem.IsMacOS()) return;
		// .NET 的 ApplicationData 在 macOS 上是 ~/.config, 与 Tauri 不同, 必须特判
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string expected = Path.Combine(home, "Library", "Application Support", AppPaths.Identifier, "data");
		Assert.Equal(expected, AppPaths.DataDir);
		Assert.DoesNotContain(Path.Combine(home, ".config"), AppPaths.DataDir, StringComparison.Ordinal);
	}

	[Fact]
	public void Linux落在XDG数据目录下()
	{
		if (!OperatingSystem.IsLinux()) return;
		// .NET 的 ApplicationData 在 Linux 上是配置目录 (~/.config), 不能代表 XDG 数据目录。
		string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string expectedRoot = !string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(home, ".local", "share");
		Assert.Equal(Path.Combine(expectedRoot, AppPaths.Identifier, "data"), AppPaths.DataDir);
	}
}
