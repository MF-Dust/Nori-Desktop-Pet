using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nori.Gateway.Services;

/// <summary>
/// 从部署目录读取资源发布清单.
/// </summary>
public sealed class AssetManifestStore
{
	private readonly string _path;

	public AssetManifestStore(string path = "configs/assets.json") => _path = path;

	public AssetManifestItem? Find(string type, string name)
	{
		if (!File.Exists(_path)) return null;
		try
		{
			AssetManifestFile? file = JsonSerializer.Deserialize<AssetManifestFile>(File.ReadAllText(_path));
			return file?.Assets.FirstOrDefault(item =>
				string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
		}
		catch (JsonException exception)
		{
			throw new InvalidOperationException($"资源 Manifest 格式错误: {_path}", exception);
		}
	}
}

public sealed class AssetManifestFile
{
	[JsonPropertyName("assets")]
	public List<AssetManifestItem> Assets { get; set; } = [];
}

public sealed class AssetManifestItem
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
