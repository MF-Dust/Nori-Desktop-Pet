using System.Text.Json.Serialization;

namespace Nori.Core.Resources;

/// <summary>
/// 资源发布清单项.
/// </summary>
public sealed record ResourceManifest
{
	[JsonPropertyName("type")]
	public required string Type { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("version")]
	public required string Version { get; init; }

	[JsonPropertyName("size")]
	public required long Size { get; init; }

	[JsonPropertyName("sha256")]
	public required string Sha256 { get; init; }

	[JsonPropertyName("object")]
	public required string Object { get; init; }
}
