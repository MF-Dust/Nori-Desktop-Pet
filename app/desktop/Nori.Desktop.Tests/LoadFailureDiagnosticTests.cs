using System.Reflection;
using Nori.Desktop.Diagnostics;

namespace Nori.Desktop.Tests;

/// <summary>加载类失败 (NORI-1X / NORI-24) 的安全诊断标签测试。</summary>
public sealed class LoadFailureDiagnosticTests
{
	[Fact]
	public void FileLoad失败标签只含程序集文件名与HRESULT()
	{
		Dictionary<string, string>? tags = CrashReporter.LoadFailureTags(
			new FileLoadException("Could not load file.", @"C:\app\vendor\Blocked.dll")) as Dictionary<string, string>;

		Assert.NotNull(tags);
		Assert.Equal("file_load", tags!["exception_kind"]);
		Assert.Equal("Blocked.dll", tags["assembly"]);
		Assert.StartsWith("0x", tags["hresult"], StringComparison.Ordinal);
		Assert.DoesNotContain("vendor", tags["assembly"], StringComparison.Ordinal);
	}

	[Fact]
	public void FileLoad失败标签在POSIX路径下也只取文件名()
	{
		Dictionary<string, string>? tags = CrashReporter.LoadFailureTags(
			new FileLoadException("Could not load file.", "/opt/nori/vendor/Blocked.so")) as Dictionary<string, string>;

		Assert.NotNull(tags);
		Assert.Equal("Blocked.so", tags!["assembly"]);
	}

	[Fact]
	public void TypeLoad失败标签含类型名()
	{
		Dictionary<string, string>? tags = CrashReporter.LoadFailureTags(
			TypeLoad("Some.Plugin.MissingType")) as Dictionary<string, string>;

		Assert.NotNull(tags);
		Assert.Equal("type_load", tags!["exception_kind"]);
		Assert.Equal("Some.Plugin.MissingType", tags["type_name"]);
	}

	[Fact]
	public void 内层加载失败也能被识别()
	{
		Dictionary<string, string>? tags = CrashReporter.LoadFailureTags(
			new InvalidOperationException("包装", new FileLoadException("blocked", "OnlyName.dll"))) as Dictionary<string, string>;

		Assert.NotNull(tags);
		Assert.Equal("OnlyName.dll", tags!["assembly"]);
	}

	[Fact]
	public void 普通异常不产生加载标签()
	{
		Assert.Null(CrashReporter.LoadFailureTags(new NullReferenceException()));
		Assert.Null(CrashReporter.LoadFailureTags(new InvalidOperationException("测试")));
	}

	/// <summary>真实类型加载失败时 TypeName 由运行时填充; 测试里用反射模拟同样的内部字段。</summary>
	private static TypeLoadException TypeLoad(string typeName)
	{
		TypeLoadException exception = new($"Could not load type '{typeName}'.");
		FieldInfo? field = typeof(TypeLoadException)
			.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
			.FirstOrDefault(candidate => candidate.FieldType == typeof(string) &&
				(candidate.Name.Contains("className", StringComparison.OrdinalIgnoreCase) ||
				 candidate.Name.Contains("typeName", StringComparison.OrdinalIgnoreCase)));
		Assert.NotNull(field);
		field!.SetValue(exception, typeName);
		return exception;
	}
}
