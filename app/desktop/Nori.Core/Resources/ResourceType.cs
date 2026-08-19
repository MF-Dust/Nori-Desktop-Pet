using System.Text.Json.Serialization;

namespace Nori.Core.Resources;

/// <summary>
/// 资源类型
///
/// 所有应用资源都通过 ResourceType 管理. 字符串值用于 API 请求 / 前端传参 / 日志.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResourceType>))]
public enum ResourceType
{
	/// <summary>Live2D 模型</summary>
	Live2D,
}

/// <summary>
/// 资源类型扩展
/// </summary>
public static class ResourceTypeExtensions
{
	/// <summary>
	/// 资源在 data/resources 下的目录名, 同时也是 API 请求用的类型串
	/// </summary>
	public static string AsString(this ResourceType type) => type switch
	{
		ResourceType.Live2D => "live2d",
		_ => throw new ArgumentOutOfRangeException(nameof(type)),
	};

	/// <summary>
	/// 从字符串解析资源类型, 无法识别返回 null
	/// </summary>
	public static ResourceType? Parse(string value) => value.Trim().ToLowerInvariant() switch
	{
		"live2d" => ResourceType.Live2D,
		_ => null,
	};
}

/// <summary>
/// 资源信息
/// </summary>
public sealed record ResourceInfo
{
	/// <summary>资源名称</summary>
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	/// <summary>资源类型</summary>
	[JsonPropertyName("resourceType")]
	public required ResourceType ResourceType { get; init; }

	/// <summary>
	/// 资源实际路径
	///
	/// 属于宿主内部文件系统信息, 前端只用于展示, 不应据此直接访问文件
	/// </summary>
	[JsonPropertyName("path")]
	public required string Path { get; init; }

	/// <summary>资源总大小 (字节)</summary>
	[JsonPropertyName("size")]
	public required long Size { get; init; }
}

/// <summary>
/// 下载进度
/// </summary>
public sealed record DownloadProgress
{
	/// <summary>已下载字节数</summary>
	[JsonPropertyName("downloaded")]
	public required long Downloaded { get; init; }

	/// <summary>文件总大小, 服务器没给 Content-Length 时为 null</summary>
	[JsonPropertyName("total")]
	public required long? Total { get; init; }

	/// <summary>下载百分比, 无法计算时为 null</summary>
	[JsonPropertyName("percentage")]
	public required float? Percentage { get; init; }

	/// <summary>
	/// 按已下载与总大小构造进度
	/// </summary>
	public static DownloadProgress Create(long downloaded, long? total) => new()
	{
		Downloaded = downloaded,
		Total = total,
		Percentage = total is > 0 ? (float)Math.Min(100.0, downloaded * 100.0 / total.Value) : null,
	};

	/// <summary>
	/// 下载完成状态
	/// </summary>
	public static DownloadProgress Completed(long total) => new()
	{
		Downloaded = total,
		Total = total,
		Percentage = 100f,
	};
}

/// <summary>
/// 资源相关错误, 消息直接展示给用户
/// </summary>
public sealed class ResourceException(string message, Exception? inner = null) : Exception(message, inner);
