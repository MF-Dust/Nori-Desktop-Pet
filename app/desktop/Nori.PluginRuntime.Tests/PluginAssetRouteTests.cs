using System.Net;
using Nori.Core.Assets;

namespace Nori.PluginRuntime.Tests;

public sealed class PluginAssetRouteTests : IAsyncLifetime
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "nori-plugin-asset-tests", Guid.NewGuid().ToString("N"));
	private readonly string _pluginId = "io.nori.asset";
	private readonly HttpClient _client = new();
	private PluginRuntimeHost _runtime = null!;
	private AssetServer _server = null!;

	public async Task InitializeAsync()
	{
		string appRoot = Path.Combine(_root, "app");
		string resourcesRoot = Path.Combine(_root, "resources");
		string pluginRoot = Path.Combine(_root, "plugins", _pluginId, "1.0.0");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(resourcesRoot);
		Directory.CreateDirectory(Path.Combine(pluginRoot, "web"));
		Directory.CreateDirectory(Path.Combine(_root, "plugins", _pluginId));
		await File.WriteAllTextAsync(Path.Combine(appRoot, "index.html"), "app");
		await File.WriteAllTextAsync(Path.Combine(pluginRoot, "web", "index.html"), "plugin asset");
		await File.WriteAllTextAsync(Path.Combine(pluginRoot, "manifest.json"),
			$"{{\"schemaVersion\":1,\"id\":\"{_pluginId}\",\"name\":\"Asset Plugin\",\"description\":\"Asset route test\",\"version\":\"1.0.0\",\"authors\":[{{\"name\":\"Nori\"}}],\"apiVersion\":\"2.0\",\"minHostVersion\":\"1.0.0\",\"runtime\":{{\"kind\":\"dotnet\",\"assembly\":\"lib/missing.dll\",\"entryType\":\"Missing.Entry\"}},\"ui\":{{\"webRoot\":\"web\"}},\"capabilities\":[],\"optionalCapabilities\":[],\"platforms\":[],\"dependencies\":[]}}");
		await File.WriteAllTextAsync(Path.Combine(_root, "plugins", _pluginId, PluginPackageInstaller.CurrentFileName), "{\"Version\":\"1.0.0\"}");

		_runtime = new PluginRuntimeHost(new PluginRuntimeHostOptions { DataDirectory = _root });
		_runtime.Discover();
		_server = await AssetServer.StartAsync(new AssetServerOptions
		{
			AppRoot = appRoot,
			ResourcesRoot = resourcesRoot,
			AdditionalRoutes = [_runtime.AssetRoute],
		});
	}

	public async Task DisposeAsync()
	{
		_client.Dispose();
		if (_server is not null) await _server.DisposeAsync();
		if (_runtime is not null) await _runtime.DisposeAsync();
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
	}

	[Fact]
	public async Task 插件公开资源可访问且manifest不可访问()
	{
		_runtime.Discover();
		HttpResponseMessage asset = await _client.GetAsync(_server.PublicUrl("plugins", $"{_pluginId}/web/index.html"));
		Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
		Assert.Equal("plugin asset", await asset.Content.ReadAsStringAsync());

		HttpResponseMessage manifest = await _client.GetAsync(new Uri($"{_server.Origin}{_server.Prefix}/plugins/{_pluginId}/manifest.json"));
		Assert.Equal(HttpStatusCode.NotFound, manifest.StatusCode);
	}

	[Theory]
	[InlineData("web/../manifest.json")]
	[InlineData("%2e%2e/manifest.json")]
	[InlineData("web/%2e%2e/manifest.json")]
	public async Task 插件资源路径穿越被拒绝(string path)
	{
		HttpResponseMessage response = await _client.GetAsync(new Uri($"{_server.Origin}{_server.Prefix}/plugins/{_pluginId}/{path}"));
		Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task 插件路由使用同一随机前缀和Host保护()
	{
		HttpResponseMessage noPrefix = await _client.GetAsync(new Uri($"{_server.Origin}/plugins/{_pluginId}/web/index.html"));
		Assert.Equal(HttpStatusCode.NotFound, noPrefix.StatusCode);

		using HttpRequestMessage request = new(HttpMethod.Get, _server.PublicUrl("plugins", $"{_pluginId}/web/index.html"));
		request.Headers.Host = "evil.example.com";
		HttpResponseMessage forged = await _client.SendAsync(request);
		Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
	}
}
