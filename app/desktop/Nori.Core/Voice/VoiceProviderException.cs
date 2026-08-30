namespace Nori.Core.Voice;

/// <summary>Voice Provider 失败类别; 遥测分类只依赖这个枚举, 不依赖异常 Message。</summary>
public enum VoiceFailureKind
{
	/// <summary>TCP 连接/DNS 解析失败等网络层错误。</summary>
	Network,

	/// <summary>请求超时 (HttpClient 超时或 Provider 网关超时)。</summary>
	Timeout,

	/// <summary>Provider 以 HTTP 状态码拒绝 (401/429/5xx 等)。</summary>
	HttpRejected,

	/// <summary>Provider 返回业务层错误 (status_code != 0、错误 JSON 等)。</summary>
	ProviderRejected,

	/// <summary>Provider 响应结构不符合预期 (缺字段/JSON 无效)。</summary>
	InvalidResponse,

	/// <summary>Provider 响应为空或音频为空。</summary>
	EmptyResponse,
}

/// <summary>
/// Voice Provider 失败的统一领域异常。
///
/// Message 仍可给 UI 展示, 但包含的是 Provider 返回的脱敏错误;
/// 遥测分类只读取 Provider/FailureKind/状态码字段, 不做文本匹配。
/// </summary>
public class VoiceProviderException : Exception
{
	/// <summary>Provider 标识 (openai/gemini/minimax/gptsovits/whisper/custom), 已是小写稳定标识。</summary>
	public string Provider { get; }

	/// <summary>失败类别。</summary>
	public VoiceFailureKind FailureKind { get; }

	/// <summary>HTTP 状态码 (仅 HttpRejected 时有值)。</summary>
	public int? HttpStatusCode { get; }

	/// <summary>Provider 业务状态码 (如 MiniMax status_code), 仅 ProviderRejected 时有值。</summary>
	public int? ProviderStatusCode { get; }

	public VoiceProviderException(
		string provider,
		VoiceFailureKind failureKind,
		string message,
		Exception? innerException = null,
		int? httpStatusCode = null,
		int? providerStatusCode = null)
		: base(message, innerException)
	{
		Provider = provider;
		FailureKind = failureKind;
		HttpStatusCode = httpStatusCode;
		ProviderStatusCode = providerStatusCode;
	}
}
