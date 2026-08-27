namespace Nori.Plugin.Runtime;

/// <summary>清单校验器的稳定公开入口。</summary>
public static class PluginManifestValidator
{
	public static PluginManifest Validate(PluginManifest manifest) => PluginManifestReader.Validate(manifest);
	public static bool IsCompatible(PluginVersion host, PluginVersion plugin) => PluginManifestReader.IsCompatible(host, plugin);
	public static void EnsureCompatible(PluginVersion hostSchema, PluginVersion pluginSchema, PluginVersion hostApi, PluginVersion pluginApi) =>
		PluginManifestReader.EnsureCompatible(hostSchema, pluginSchema, hostApi, pluginApi);
}

/// <summary>清单读取器的别名入口。</summary>
public static class PluginManifestLoader
{
	public static PluginManifest Read(string path) => PluginManifestReader.Read(path);
	public static PluginManifest ReadJson(string json) => PluginManifestReader.ReadJson(json);
}
