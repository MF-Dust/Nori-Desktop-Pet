using System.Text.Json.Serialization;

namespace Nori.Desktop.Plugins;

/// <summary>
/// 供前端与桥接层安全读取的脱敏插件描述符摘要 (严格排除 InstallPath 等敏感文件系统路径)
/// </summary>
public sealed record PluginDescriptorSummary
{
	/// <summary>插件唯一 ID</summary>
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	/// <summary>插件显示名称</summary>
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	/// <summary>插件语义化版本</summary>
	[JsonPropertyName("version")]
	public required string Version { get; init; }

	/// <summary>插件描述文案</summary>
	[JsonPropertyName("description")]
	public string Description { get; init; } = "";

	/// <summary>插件作者信息</summary>
	[JsonPropertyName("author")]
	public string Author { get; init; } = "";

	/// <summary>插件已声明/已获授权的能力列表</summary>
	[JsonPropertyName("capabilities")]
	public IReadOnlyList<string> Capabilities { get; init; } = [];
}
