using System.IO.Compression;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Runtime;

namespace Nori.Plugin.Runtime.Tests;

public sealed class PluginRuntimeReviewTests
{
	[Fact]
	public async Task 禁用尚未激活的插件会保持Disabled状态()
	{
		string root = CreateTemp();
		try
		{
			string package = CreateTestPackage(root, "disabled.plugin");
			PluginManager manager = new(new PluginRuntimeOptions
			{
				PluginsDirectory = Path.Combine(root, "plugins"),
				DataDirectory = Path.Combine(root, "plugin-data"),
				KnownCapabilityIds = [],
			});
			manager.Install(package);

			await manager.DisableAsync("disabled.plugin");

			Assert.Equal(PluginLifecycleState.Disabled, Assert.Single(manager.Plugins).State);
			await manager.DisposeAsync();
		}
		finally { DeleteDirectory(root); }
	}

	[Fact]
	public void 安装目录中的Contract副本会在加载前被拒绝()
	{
		string root = CreateTemp();
		try
		{
			string lib = Path.Combine(root, "lib");
			Directory.CreateDirectory(lib);
			File.Copy(typeof(INoriPlugin).Assembly.Location, Path.Combine(lib, "Nori.Plugin.Abstractions.dll"));

			PluginException exception = Assert.Throws<PluginException>(() => PluginLoadContext.EnsureReferencesAllowed(root));
			Assert.Equal(PluginErrorCodes.ContractAssemblyDenied, exception.Code);
		}
		finally { DeleteDirectory(root); }
	}

	[Fact]
	public void Windows原生Dll不会被误判为损坏的托管程序集()
	{
		if (!OperatingSystem.IsWindows()) return;

		string root = CreateTemp();
		try
		{
			string runtime = Path.Combine(root, "runtimes", "win-x64", "native");
			Directory.CreateDirectory(runtime);
			string systemDll = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
			Assert.True(File.Exists(systemDll));
			File.Copy(systemDll, Path.Combine(runtime, "plugin-native.dll"));

			PluginLoadContext.EnsureReferencesAllowed(root);
		}
		finally { DeleteDirectory(root); }
	}

	private static string CreateTestPackage(string root, string id)
	{
		string package = Path.Combine(root, $"{id}.noripack");
		string assembly = typeof(Nori.Plugin.TestPlugin.TestPlugin).Assembly.Location;
		using FileStream file = File.Create(package);
		using ZipArchive archive = new(file, ZipArchiveMode.Create);
		WriteEntry(archive, "manifest.json", $"{{\"schemaVersion\":1,\"id\":\"{id}\",\"name\":\"Demo\",\"description\":\"Demo plugin\",\"version\":\"1.0.0\",\"authors\":[{{\"name\":\"Nori\"}}],\"apiVersion\":\"1.0\",\"minHostVersion\":\"1.0.0\",\"runtime\":{{\"kind\":\"dotnet\",\"assembly\":\"lib/Nori.Plugin.TestPlugin.dll\",\"entryType\":\"Nori.Plugin.TestPlugin.TestPlugin\"}},\"capabilities\":[],\"optionalCapabilities\":[],\"platforms\":[],\"dependencies\":[]}}");
		ZipArchiveEntry assemblyEntry = archive.CreateEntry("lib/Nori.Plugin.TestPlugin.dll");
		using Stream target = assemblyEntry.Open();
		using FileStream source = File.OpenRead(assembly);
		source.CopyTo(target);
		return package;
	}

	private static void WriteEntry(ZipArchive archive, string name, string content)
	{
		using StreamWriter writer = new(archive.CreateEntry(name).Open());
		writer.Write(content);
	}

	private static string CreateTemp()
	{
		string path = Path.Combine(Path.GetTempPath(), "nori-plugin-review-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteDirectory(string path)
	{
		try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
	}
}
