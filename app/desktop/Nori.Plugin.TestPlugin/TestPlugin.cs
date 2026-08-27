using Nori.Plugin.Abstractions;

namespace Nori.Plugin.TestPlugin;

/// <summary>用于运行时端到端测试的最小插件。</summary>
public sealed class TestPlugin : INoriPlugin
{
	public async ValueTask StartAsync(PluginContext context, CancellationToken cancellationToken = default)
	{
		context.Capabilities.Register(new PluginCapability("test.lifecycle"));
		context.Contributions.Register(new PluginContribution("test.started", "test", "started"));
		context.Storage.Set("started", "true");
		await ValueTask.CompletedTask;
	}

	public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
