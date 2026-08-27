namespace Nori.PluginRuntime;

/// <summary>插件合同程序集的唯一识别与拒绝规则。</summary>
internal static class PluginAssemblyPolicy
{
	private static readonly HashSet<string> ContractAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
	{
		typeof(INoriPlugin).Assembly.GetName().Name!,
		"Nori.Plugin.Abstractions",
		"Nori.Plugin.Games.Abstractions",
		"Nori.Plugin.Arcade.Abstractions",
		"Nori.Plugin.Harness.Abstractions",
		"Nori.Plugin.Runtime",
	};

	public static string CurrentAssemblyName => typeof(INoriPlugin).Assembly.GetName().Name!;

	public static bool IsContractAssemblyName(string name) => ContractAssemblyNames.Contains(name);

	public static bool IsLegacyContractAssemblyName(string name) =>
		IsContractAssemblyName(name) && !string.Equals(name, CurrentAssemblyName, StringComparison.OrdinalIgnoreCase);

	public static bool IsContractAssemblyFile(string fileName) =>
		IsContractAssemblyName(Path.GetFileNameWithoutExtension(fileName));
}
