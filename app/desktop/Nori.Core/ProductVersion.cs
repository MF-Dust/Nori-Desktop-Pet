using System.Reflection;

namespace Nori.Core;

/// <summary>应用产品版本的运行时读取入口。</summary>
public static class ProductVersion
{
	private const string UnknownVersion = "unknown";

	/// <summary>读取程序集信息版本，去掉 source-link 构建元数据。</summary>
	public static string Current
	{
		get
		{
			string? value = Assembly.GetExecutingAssembly()
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (string.IsNullOrWhiteSpace(value))
				value = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
			if (string.IsNullOrWhiteSpace(value)) return UnknownVersion;
			int metadata = value.IndexOf('+');
			return (metadata >= 0 ? value[..metadata] : value).Trim();
		}
	}
}
