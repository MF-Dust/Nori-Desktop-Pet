using System.Text.Json;
using Avalonia.Controls;
using Nori.Desktop.Bridge;
using Nori.Desktop.Windows;
using Nori.Plugin.Runtime;

namespace Nori.Desktop.Tests;

public sealed class PluginManagementBridgeTests : IAsyncDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "nori-plugin-bridge-tests", Guid.NewGuid().ToString("N"));
	private readonly HttpClient _http = new();

	private sealed class FakeSource(string label, bool visible = true) : IBridgeSource
	{
		public string Label => label;
		public bool IsVisible => visible;
		public Window? Self => null;
		public void PostEvent(string name, object? payload) { }
		public void PostResult(long id, object? value, string? error) { }
	}

	private sealed class FakePicker(string? result) : IPluginPackagePicker
	{
		public int Calls { get; private set; }
		public Task<string?> PickAsync(IBridgeSource source, CancellationToken cancellationToken = default)
		{
			Calls++;
			return Task.FromResult(result);
		}
	}

	[Fact]
	public async Task 五个管理命令都拒绝非main和不可见main()
	{
		PluginManager manager = CreateManager();
		AppServices services = Services(manager, new FakePicker(null));
		PluginManagementBridgeCommands commands = new(services);
		string[] names = ["plugin_list", "plugin_install_local", "plugin_enable", "plugin_disable", "plugin_uninstall"];
		foreach (string name in names)
		{
			await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commands.InvokeAsync(new FakeSource(WindowLabels.Init), name, Args(new {id = "io.nori.test"})));
			await Assert.ThrowsAsync<UnauthorizedAccessException>(() => commands.InvokeAsync(new FakeSource(WindowLabels.Main, false), name, Args(new {id = "io.nori.test"})));
		}
		await manager.DisposeAsync();
	}

	[Fact]
	public async Task plugin_list返回稳定DTO且不泄露安装路径或异常对象()
	{
		CreateInstalledLayout("io.nori.dto", "1.2.3");
		PluginManager manager = CreateManager();
		AppServices services = Services(manager, new FakePicker(null));
		PluginManagementBridgeCommands commands = new(services);

		object? result = await commands.InvokeAsync(new FakeSource(WindowLabels.Main), "plugin_list", Args(new { }));
		string json = JsonSerializer.Serialize(result, BridgeJson.Options);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement item = document.RootElement.GetProperty("plugins")[0];
		Assert.Equal("io.nori.dto", item.GetProperty("id").GetString());
		Assert.Equal("installed", item.GetProperty("state").GetString());
		Assert.Equal("Nori Test", item.GetProperty("author").GetString());
		Assert.False(item.TryGetProperty("installPath", out _));
		Assert.False(item.TryGetProperty("storagePath", out _));
		Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("stackTrace", json, StringComparison.OrdinalIgnoreCase);
		await manager.DisposeAsync();
	}

	[Fact]
	public async Task 安装取消是正常结果且前端路径参数完全被忽略()
	{
		PluginManager manager = CreateManager();
		FakePicker picker = new(null);
		AppServices services = Services(manager, picker);
		PluginManagementBridgeCommands commands = new(services);
		object? result = await commands.InvokeAsync(
			new FakeSource(WindowLabels.Main),
			"plugin_install_local",
			Args(new {path = Path.Combine(_root, "forged.noripack"), filePath = "C:\\forged.noripack"}));
		string json = JsonSerializer.Serialize(result, BridgeJson.Options);
		Assert.Contains("\"cancelled\":true", json, StringComparison.Ordinal);
		Assert.Equal(1, picker.Calls);
		await manager.DisposeAsync();
	}

	[Fact]
	public async Task SafeMode允许列表禁用卸载并拒绝安装启用()
	{
		CreateInstalledLayout("io.nori.safe", "1.0.0");
		PluginManager manager = CreateManager(safeMode: true);
		FakePicker picker = new(null);
		AppServices services = Services(manager, picker, safeMode: true);
		PluginManagementBridgeCommands commands = new(services);
		FakeSource main = new(WindowLabels.Main);

		object? list = await commands.InvokeAsync(main, "plugin_list", Args(new { }));
		Assert.Contains("safe_mode_disabled", JsonSerializer.Serialize(list, BridgeJson.Options), StringComparison.Ordinal);
		await Assert.ThrowsAsync<PluginException>(() => commands.InvokeAsync(main, "plugin_install_local", Args(new { })));
		Assert.Equal(0, picker.Calls);
		await Assert.ThrowsAsync<PluginException>(() => commands.InvokeAsync(main, "plugin_enable", Args(new {id = "io.nori.safe"})));

		object? disabled = await commands.InvokeAsync(main, "plugin_disable", Args(new {id = "io.nori.safe"}));
		Assert.Contains("\"enabled\":false", JsonSerializer.Serialize(disabled, BridgeJson.Options), StringComparison.Ordinal);
		object? uninstalled = await commands.InvokeAsync(main, "plugin_uninstall", Args(new {id = "io.nori.safe", deleteData = false}));
		Assert.Contains("\"success\":true", JsonSerializer.Serialize(uninstalled, BridgeJson.Options), StringComparison.Ordinal);
		await manager.DisposeAsync();
	}

	[Fact]
	public void Router把plugin前缀归到Plugins领域()
	{
		Assert.Equal(BridgeCommandDomain.Plugins, BridgeCommandRouter.Classify("plugin_list"));
		Assert.Equal(BridgeCommandDomain.Plugins, BridgeCommandRouter.Classify("plugin_uninstall"));
	}

	private PluginManager CreateManager(bool safeMode = false)
	{
		Directory.CreateDirectory(_root);
		return new PluginManager(new PluginRuntimeOptions
		{
			PluginsDirectory = Path.Combine(_root, "plugins"),
			DataDirectory = Path.Combine(_root, "plugin-data"),
			SafeMode = safeMode,
		});
	}

	private AppServices Services(PluginManager manager, IPluginPackagePicker picker, bool safeMode = false) => new()
	{
		Database = null!,
		Config = null!,
		Logger = null!,
		Resources = null!,
		Chat = null!,
		Memory = null!,
		Embedding = null!,
		Llm = null!,
		Mcp = null!,
		Http = _http,
		AgentOperations = null!,
		Plugins = manager,
		PluginPackagePicker = picker,
		SafeMode = safeMode,
	};

	private void CreateInstalledLayout(string id, string version)
	{
		string directory = Path.Combine(_root, "plugins", id, version);
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(_root, "plugins", id, PluginPackageInstaller.CurrentFileName), JsonSerializer.Serialize(new {Version = version}));
		File.WriteAllText(Path.Combine(directory, PluginPackageInstaller.ManifestFileName), $"{{\"schemaVersion\":1,\"id\":\"{id}\",\"name\":\"Bridge Test\",\"description\":\"Plugin DTO test\",\"version\":\"{version}\",\"authors\":[{{\"name\":\"Nori Test\"}}],\"homepage\":\"https://example.test\",\"repository\":\"https://example.test/repo\",\"license\":\"MIT\",\"apiVersion\":\"1.0\",\"minHostVersion\":\"1.0.0\",\"runtime\":{{\"kind\":\"dotnet\",\"assembly\":\"lib/missing.dll\",\"entryType\":\"Missing.Entry\"}},\"ui\":{{\"webRoot\":\"web\"}},\"capabilities\":[\"ui.webview\"],\"optionalCapabilities\":[],\"platforms\":[],\"dependencies\":[]}}");
	}

	private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value, BridgeJson.Options);

	public async ValueTask DisposeAsync()
	{
		_http.Dispose();
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
		await ValueTask.CompletedTask;
	}
}
