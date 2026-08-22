using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.Security.AntiSSRF;

namespace Nori.Core.Network;

/// <summary>
/// 应用出站 HTTP 客户端集合。
///
/// Local 用于用户显式配置的 LLM、Embedding、TTS 与 MCP 端点，保留系统代理；
/// Public 用于网页/天气/搜索/技能等公网请求，使用 AntiSSRF 并禁止自动重定向。
/// </summary>
public sealed class NoriHttpClients : IDisposable
{
	/// <summary>本地/模型请求超时</summary>
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(130);

	public HttpClient Local { get; }

	public HttpClient Public { get; }

	private NoriHttpClients(HttpClient local, HttpClient @public)
	{
		Local = local;
		Public = @public;
	}

	/// <summary>创建一组带统一 TLS 与超时策略的客户端。</summary>
	public static NoriHttpClients Create(bool allowInsecureTls, TimeSpan? timeout = null)
	{
		TimeSpan requestTimeout = timeout ?? DefaultTimeout;
		HttpClientHandler localHandler = new()
		{
			ServerCertificateCustomValidationCallback = allowInsecureTls
				? static (_, _, _, _) => true
				: null,
		};
		HttpClient local = new(localHandler) {Timeout = requestTimeout};

		AntiSSRFPolicy policy = new(PolicyConfigOptions.ExternalOnlyLatest)
		{
			AllowPlainTextHttp = true,
			AddXFFHeader = false,
		};
		AntiSSRFHandler publicHandler = policy.GetHandler();
		publicHandler.AllowAutoRedirect = false;
		publicHandler.MaxAutomaticRedirections = UrlAccessPolicy.MaxRedirects;
		publicHandler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
		publicHandler.SslOptions = new SslClientAuthenticationOptions
		{
			EnabledSslProtocols = SslProtocols.None,
			RemoteCertificateValidationCallback = allowInsecureTls
				? static (_, _, _, _) => true
				: null,
		};
		HttpClient @public = new(publicHandler) {Timeout = requestTimeout};

		return new NoriHttpClients(local, @public);
	}

	public void Dispose()
	{
		Public.Dispose();
		Local.Dispose();
	}
}
