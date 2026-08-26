namespace Nori.Desktop.Automation.Browser;

/// <summary>探测当前发布目录是否包含 Playwright 的 Node/JS driver runtime。</summary>
public static class PlaywrightRuntimeAvailability
{
	public const string MissingReason = "浏览器自动化组件未安装";

	/// <summary>
	/// Microsoft.Playwright.dll 只是托管 API；真正执行还需要发布目录中的 .playwright/package 与 node。
	/// </summary>
	public static bool IsAvailable(string? baseDirectory = null)
	{
		string root = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
		string playwrightRoot = Path.Combine(root, ".playwright");
		return Directory.Exists(Path.Combine(playwrightRoot, "package"))
			&& Directory.Exists(Path.Combine(playwrightRoot, "node"));
	}

	public static object Snapshot() => new
	{
		available = IsAvailable(),
		reason = IsAvailable() ? null : MissingReason,
	};
}
