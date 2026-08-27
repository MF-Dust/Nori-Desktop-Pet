namespace Nori.Plugin.Harness.Abstractions;

/// <summary>插件测试夹具暴露的观察接口。</summary>
public interface IPluginHarness
{
	IReadOnlyDictionary<string, string> Observations { get; }
}
