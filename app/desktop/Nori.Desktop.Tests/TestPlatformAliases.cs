global using OperatingSystem = Nori.Desktop.Tests.TestOperatingSystem;

namespace Nori.Desktop.Tests;

/// <summary>
/// 让默认测试夹具在所有 CI 平台上使用一致的 Windows 自动化语义。
/// 需要验证非 Windows 拒绝路径的测试仍通过 automationWindows=false 显式注入。
/// </summary>
internal static class TestOperatingSystem
{
	public static bool IsWindows() => true;
}
