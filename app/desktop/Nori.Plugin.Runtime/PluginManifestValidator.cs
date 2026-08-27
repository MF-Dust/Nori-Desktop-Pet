namespace Nori.Plugin.Runtime;

/// <summary>清单校验器的稳定公开入口。</summary>
public static class PluginManifestValidator
{
	public static PluginManifest Validate(PluginManifest manifest) => PluginManifestReader.Validate(manifest);
	public static bool IsCompatible(PluginApiVersion host, PluginApiVersion plugin) => PluginManifestReader.IsCompatible(host, plugin);
	public static void EnsureCompatible(PluginApiVersion host, PluginApiVersion plugin) => PluginManifestReader.EnsureCompatible(host, plugin);
	public static bool IsHostVersionSupported(PluginVersion host, string minimum) => PluginManifestReader.IsHostVersionSupported(host, minimum);
}

/// <summary>清单读取器的别名入口。</summary>
public static class PluginManifestLoader
{
	public static PluginManifest Read(string path) => PluginManifestReader.Read(path);
	public static PluginManifest ReadJson(string json) => PluginManifestReader.ReadJson(json);
}
