using Aliyun.OSS;
using Aliyun.OSS.Common;
using Nori.Gateway.Configuration;

namespace Nori.Gateway.Services;

/// <summary>
/// OSS 服务
///
/// 对应 Go 版 internal/service/oss.go: 校验对象存在后生成签名下载 URL
/// </summary>
public sealed class OssService
{
	private readonly OssSection _config;
	private readonly OssClient _client;
	private readonly ILogger<OssService> _logger;

	public OssService(GatewayConfig config, ILogger<OssService> logger)
	{
		_config = config.Oss;
		_logger = logger;
		_client = new OssClient(_config.Endpoint, _config.AccessKeyId, _config.AccessKeySecret);
	}

	/// <summary>
	/// 获取对象签名 URL
	/// </summary>
	/// <param name="objectType">资源类型 (live2d / voice 等)</param>
	/// <param name="objectName">资源名称</param>
	/// <returns>签名 URL; 对象不存在返回 null</returns>
	public string? GetSignedUrl(string objectType, string objectName)
	{
		// 对象路径: live2d/nori.zip
		string objectKey = $"{objectType}/{objectName}.zip";
		bool exists;
		try
		{
			exists = _client.DoesObjectExist(_config.BucketName, objectKey);
		}
		catch (Exception exception) when (exception is OssException or System.Net.WebException)
		{
			_logger.LogError(exception, "检查对象是否存在失败 objectKey={ObjectKey}", objectKey);
			throw new InvalidOperationException($"检查对象是否存在失败: {exception.Message}", exception);
		}
		if (!exists)
		{
			_logger.LogWarning("对象不存在 objectKey={ObjectKey}", objectKey);
			return null;
		}

		try
		{
			Uri signed = _client.GeneratePresignedUri(
				_config.BucketName,
				objectKey,
				DateTime.UtcNow.AddSeconds(_config.UrlExpireSeconds),
				SignHttpMethod.Get);
			_logger.LogInformation("生成签名URL成功 objectKey={ObjectKey}", objectKey);
			return signed.ToString();
		}
		catch (Exception exception) when (exception is OssException or System.Net.WebException)
		{
			_logger.LogError(exception, "生成签名URL失败 objectKey={ObjectKey}", objectKey);
			throw new InvalidOperationException($"生成签名URL失败: {exception.Message}", exception);
		}
	}
}
