using System.IO.Compression;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Runtime;

namespace Nori.Plugin.Runtime.Tests;

public sealed class PluginRuntimeTests
{
	[Fact]
	public void 清单解析保留独立版本并执行兼容性规则()
	{
		PluginManifest manifest = PluginManifestReader.ReadJson("""
			{"schemaVersion":"1.2.0","pluginId":"demo.plugin","name":"Demo","version":"4.5.6","apiVersion":"1.1.0","entryAssembly":"Demo.dll","entryType":"Demo.Plugin"}
			""");

		Assert.Equal(new PluginVersion(1, 2, 0), manifest.Schema);
		Assert.Equal(new PluginVersion(4, 5, 6), manifest.Plugin);
		Assert.True(PluginManifestReader.IsCompatible(new PluginVersion(1, 3, 0), manifest.Schema));
		Assert.False(PluginManifestReader.IsCompatible(new PluginVersion(2, 0, 0), manifest.Schema));
	}

	[Fact]
	public void 存储按插件目录持久化且资源只允许公开目录()
	{
		string root = CreateTemp();
		JsonPluginStorage storage = new(Path.Combine(root, "plugin-data", "demo"));
		storage.Set("answer", "42");
		Assert.Equal("42", new JsonPluginStorage(Path.Combine(root, "plugin-data", "demo")).Get("answer"));

		Directory.CreateDirectory(Path.Combine(root, "web"));
		File.WriteAllText(Path.Combine(root, "web", "index.html"), "ok");
		File.WriteAllText(Path.Combine(root, "plugin.json"), "not public");
		PluginAssetReader assets = new(root);
		Assert.True(assets.Exists("web/index.html"));
		Assert.False(assets.Exists("plugin.json"));
	}

	[Fact]
	public async Task 安装包使用当前目录并可发现测试插件()
	{
		string root = CreateTemp();
		string package = Path.Combine(root, "demo.nori-plugin");
		string assembly = typeof(Nori.Plugin.TestPlugin.TestPlugin).Assembly.Location;
		using (FileStream file = File.Create(package))
		using (ZipArchive zip = new(file, ZipArchiveMode.Create))
		{
			Write(zip, "bundle/plugin.json", """
				{"schemaVersion":"1.0.0","pluginId":"test.plugin","name":"Test","version":"1.0.0","apiVersion":"1.0.0","entryAssembly":"Nori.Plugin.TestPlugin.dll","entryType":"Nori.Plugin.TestPlugin.TestPlugin","capabilities":["test"]}
				""");
			ZipArchiveEntry entry = zip.CreateEntry("bundle/lib/Nori.Plugin.TestPlugin.dll");
			using (Stream target = entry.Open())
			using (FileStream source = File.OpenRead(assembly))
				source.CopyTo(target);
			Write(zip, "bundle/web/index.html", "ok");
		}

		PluginManager manager = new(new PluginRuntimeOptions { PluginsDirectory = Path.Combine(root, "plugins"), DataDirectory = Path.Combine(root, "data") });
		manager.Install(package);
		Assert.Contains(manager.Discover(), item => item.Id == "test.plugin");
		await manager.StartAllAsync();
		PluginInfo info = Assert.Single(manager.Plugins);
		Assert.Equal(PluginLifecycleState.Active, info.State);
		Assert.True(File.Exists(Path.Combine(root, "data", "test.plugin", "storage.json")));
		await manager.DisposeAsync();
	}

	[Fact]
	public async Task 安全模式只发现并禁用插件()
	{
		string root = CreateTemp();
		string current = Path.Combine(root, "plugins", "current", "safe.plugin");
		Directory.CreateDirectory(current);
		File.WriteAllText(Path.Combine(current, "plugin.json"), """
			{"schemaVersion":"1.0.0","pluginId":"safe.plugin","name":"Safe","version":"1.0.0","apiVersion":"1.0.0","entryAssembly":"Safe.dll","entryType":"Safe.Plugin"}
			""");
		PluginManager manager = new(new PluginRuntimeOptions { PluginsDirectory = Path.Combine(root, "plugins"), DataDirectory = Path.Combine(root, "data"), SafeMode = true });
		Assert.Equal(PluginLifecycleState.Disabled, Assert.Single(manager.Discover()).State);
		await manager.StartAllAsync();
		Assert.Equal(PluginLifecycleState.Disabled, Assert.Single(manager.Plugins).State);
	}

	private static void Write(ZipArchive archive, string name, string value)
	{
		using StreamWriter writer = new(archive.CreateEntry(name).Open());
		writer.Write(value);
	}

	private static string CreateTemp()
	{
		string path = Path.Combine(Path.GetTempPath(), "nori-plugin-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}
}
