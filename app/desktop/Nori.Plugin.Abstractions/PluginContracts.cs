using System.Collections.ObjectModel;

namespace Nori.Plugin.Abstractions;

/// <summary>插件生命周期状态。</summary>
public enum PluginLifecycleState
{
	Discovered,
	Disabled,
	Loading,
	Active,
	Faulted,
	PendingRestart,
	Unloaded,
}

/// <summary>插件能力声明。</summary>
public sealed record PluginCapability(string Name, IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>插件贡献项。</summary>
public sealed record PluginContribution(string Id, string Kind, object Value);

/// <summary>插件注册表的只读视图。</summary>
public interface IPluginContributions
{
	IReadOnlyCollection<PluginContribution> Items { get; }
}

/// <summary>后续 UI provider 使用的注册钩子，不暴露宿主内部服务。</summary>
public interface IPluginUiProviderRegistry
{
	void Register(string providerId, object provider);
}

/// <summary>插件可用的最小宿主上下文。</summary>
public sealed class PluginContext
{
	public required string PluginId { get; init; }
	public required string DataDirectory { get; init; }
	public required IPluginStorage Storage { get; init; }
	public required IPluginAssetReader Assets { get; init; }
	public required IPluginContributionRegistry Contributions { get; init; }
	public required IPluginCapabilityRegistry Capabilities { get; init; }
	public IPluginUiProviderRegistry? UiProviders { get; init; }
	public CancellationToken ShutdownToken { get; init; }
}

/// <summary>插件持久化存储。</summary>
public interface IPluginStorage
{
	string? Get(string key);
	void Set(string key, string value);
	bool Remove(string key);
	IReadOnlyCollection<string> Keys { get; }
}

/// <summary>插件公开资源读取器。</summary>
public interface IPluginAssetReader
{
	Stream OpenRead(string relativePath);
	bool Exists(string relativePath);
	IReadOnlyList<string> List(string? relativeDirectory = null);
}

/// <summary>插件贡献注册表。</summary>
public interface IPluginContributionRegistry
{
	void Register(PluginContribution contribution);
	bool Remove(string id);
	IReadOnlyCollection<PluginContribution> Items { get; }
}

/// <summary>插件能力注册表。</summary>
public interface IPluginCapabilityRegistry
{
	void Register(PluginCapability capability);
	bool Has(string name);
	IReadOnlyCollection<PluginCapability> Items { get; }
}

/// <summary>受信任的进程内插件入口。程序集加载隔离不等同于安全沙箱。</summary>
public interface INoriPlugin
{
	ValueTask StartAsync(PluginContext context, CancellationToken cancellationToken = default);
	ValueTask StopAsync(CancellationToken cancellationToken = default);
}
