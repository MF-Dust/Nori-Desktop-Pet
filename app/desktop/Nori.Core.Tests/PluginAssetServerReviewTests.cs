using System.Net;
using Nori.Core.Assets;

namespace Nori.Core.Tests;

public sealed class PluginAssetServerReviewTests
{
	[Fact]
	public async Task 超过64位但符合Manifest规范的插件ID可以读取公开资源()
	{
		string root = Path.Combine(Path.GetTempPath(), $"nori-plugin-asset-review-{Guid.NewGuid():N}");
		string appRoot = Path.Combine(root, "app");
		string resourcesRoot = Path.Combine(root, "resources");
		string pluginId = "io." + new string('a', 70);
		string pluginRoot = Path.Combine(root, "plugins", pluginId);
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(resourcesRoot);
		Directory.CreateDirectory(Path.Combine(pluginRoot, "web"));
		await File.WriteAllTextAsync(Path.Combine(appRoot, "index.html"), "app");
		await File.WriteAllTextAsync(Path.Combine(pluginRoot, "web", "index.html"), "long-plugin-id");

		try
		{
			await using AssetServer server = await AssetServer.StartAsync(new AssetServerOptions
			{
				AppRoot = appRoot,
				ResourcesRoot = resourcesRoot,
				PluginRootResolver = id => string.Equals(id, pluginId, StringComparison.Ordinal) ? pluginRoot : null,
			});
			using HttpClient client = new();

			HttpResponseMessage response = await client.GetAsync(new Uri(server.PluginAssetUrl(pluginId, "web/index.html")));

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal("long-plugin-id", await response.Content.ReadAsStringAsync());
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
		}
	}
}
