using YamlDotNet.Serialization;

namespace Nori.Gateway.Configuration;

/// <summary>
/// 网关配置
/// </summary>
public sealed class GatewaySection
{
	/// <summary>监听端口</summary>
	[YamlMember(Alias = "port")]
	public int Port { get; set; } = 8084;

	/// <summary>数据目录</summary>
	[YamlMember(Alias = "data-path")]
	public string DataPath { get; set; } = "data";

	/// <summary>临时目录</summary>
	[YamlMember(Alias = "temp-path")]
	public string TempPath { get; set; } = "temp";
}

/// <summary>
/// 日志配置
/// </summary>
public sealed class LoggerSection
{
	/// <summary>输出方式: file / console / both</summary>
	[YamlMember(Alias = "output")]
	public string Output { get; set; } = "both";

	/// <summary>日志级别</summary>
	[YamlMember(Alias = "level")]
	public string Level { get; set; } = "info";

	/// <summary>系统日志路径</summary>
	[YamlMember(Alias = "log-path")]
	public string LogPath { get; set; } = "logs/server.log";

	/// <summary>请求日志路径</summary>
	[YamlMember(Alias = "request-log-path")]
	public string RequestLogPath { get; set; } = "logs/request.log";

	/// <summary>单个文件大小上限 (MB)</summary>
	[YamlMember(Alias = "max-size")]
	public int MaxSize { get; set; } = 64;

	/// <summary>保留文件数</summary>
	[YamlMember(Alias = "max-backups")]
	public int MaxBackups { get; set; } = 7;

	/// <summary>保留天数</summary>
	[YamlMember(Alias = "max-age")]
	public int MaxAge { get; set; } = 30;

	/// <summary>是否压缩</summary>
	[YamlMember(Alias = "compress")]
	public bool Compress { get; set; }
}

/// <summary>
/// OSS 配置
/// </summary>
public sealed class OssSection
{
	/// <summary>OSS Endpoint</summary>
	[YamlMember(Alias = "endpoint")]
	public string Endpoint { get; set; } = "";

	/// <summary>AccessKey ID</summary>
	[YamlMember(Alias = "access-key-id")]
	public string AccessKeyId { get; set; } = "";

	/// <summary>AccessKey Secret</summary>
	[YamlMember(Alias = "access-key-secret")]
	public string AccessKeySecret { get; set; } = "";

	/// <summary>Bucket 名</summary>
	[YamlMember(Alias = "bucket-name")]
	public string BucketName { get; set; } = "";

	/// <summary>签名 URL 有效期 (秒)</summary>
	[YamlMember(Alias = "url-expire-seconds")]
	public int UrlExpireSeconds { get; set; } = 3600;
}

/// <summary>
/// 完整配置
///
/// 字段与键名与 Go 版 configs/config.yaml 保持一致, 服务器上那份配置不需要改写.
/// </summary>
public sealed class GatewayConfig
{
	[YamlMember(Alias = "gateway")]
	public GatewaySection Gateway { get; set; } = new();

	[YamlMember(Alias = "logger")]
	public LoggerSection Logger { get; set; } = new();

	[YamlMember(Alias = "oss")]
	public OssSection Oss { get; set; } = new();

	/// <summary>
	/// 从工作目录下的 configs/config.yaml 加载
	///
	/// Go 版在配置缺失时 init() 静默返回, 首次 config.Get() 才 panic;
	/// 这里改成启动即抛出可读错误.
	/// </summary>
	public static GatewayConfig Load(string path = "configs/config.yaml")
	{
		if (!File.Exists(path))
		{
			throw new InvalidOperationException($"找不到配置文件: {Path.GetFullPath(path)}。请参考 configs/config.example.yaml 创建。");
		}
		IDeserializer deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
		GatewayConfig config = deserializer.Deserialize<GatewayConfig>(File.ReadAllText(path))
			?? throw new InvalidOperationException($"配置文件为空: {path}");
		if (config.Oss.Endpoint.Length == 0 || config.Oss.BucketName.Length == 0)
		{
			throw new InvalidOperationException("配置缺少 oss.endpoint 或 oss.bucket-name");
		}
		return config;
	}
}
