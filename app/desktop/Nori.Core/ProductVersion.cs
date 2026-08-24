using System.Reflection;

namespace Nori.Core;

/// <summary>应用产品版本的运行时读取入口。</summary>
public static class ProductVersion
{
	private const string DefaultVersion = "Dev";

	/// <summary>读取程序集的完整 informational version, 保留 v、codename 和提交短 hash。</summary>
	public static string Current
	{
		get
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			string? value = assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			return string.IsNullOrWhiteSpace(value) ? DefaultVersion : value.Trim();
		}
	}
}
