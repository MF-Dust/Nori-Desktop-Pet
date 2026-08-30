using System.Net.Sockets;
using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Resources;
using Nori.Core.Voice;
using Nori.PluginRuntime;

namespace Nori.Desktop.Bridge;

/// <summary>Bridge 命令失败的遥测分类。</summary>
public enum BridgeFailureClass
{
	Expected,
	ExternalService,
	Cancelled,
	Unexpected,
}

/// <summary>
/// Bridge 失败的最小日志与遥测决策。
///
/// Tags 是随异常上报的安全标签 (经白名单归一化), 当前只包含 failure_kind。
/// </summary>
public readonly record struct BridgeFailure(
	BridgeFailureClass Class,
	string LogLevel,
	bool Telemetry,
	IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>
/// 按异常类型和稳定错误码分类 Bridge 失败。
///
/// 分类只决定日志级别和是否捕获异常, 不改变命令失败向前端返回 reject 的行为。
/// 分类完全不读取异常 Message, 避免文本匹配带来的漂移。
/// </summary>
public static class BridgeFailureClassifier
{
	public static BridgeFailure Classify(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		if (exception is AggregateException aggregate) return ClassifyAggregate(aggregate);
		if (exception is VoiceProviderException voiceProvider) return ClassifyVoiceProvider(voiceProvider);
		if (exception is TaskCanceledException or TimeoutException)
			return ExternalService("timeout");
		if (exception is OperationCanceledException)
			return new(BridgeFailureClass.Cancelled, "info", false, FailureKind("cancelled"));
		if (exception is HttpRequestException requestException)
			return ExternalService(IsConnectFailure(requestException) ? "connect" : "http_status");
		if (exception is ChatException or ResourceException or UriFormatException or ArgumentException
			or InvalidOperationException or JsonException or UnauthorizedAccessException)
			return Expected();
		if (exception is PluginException pluginException)
			return IsExpectedPluginFailure(pluginException.Code)
				? Expected()
				: Unexpected();
		return Unexpected();
	}

	private static BridgeFailure Expected() =>
		new(BridgeFailureClass.Expected, "warn", false, FailureKind("validation"));

	/// <summary>Voice Provider 失败统一按外部服务处理, 并带上 provider / failure_kind 安全标签。</summary>
	private static BridgeFailure ClassifyVoiceProvider(VoiceProviderException exception)
	{
		string kind = exception.FailureKind switch
		{
			VoiceFailureKind.Network => "connect",
			VoiceFailureKind.Timeout => "timeout",
			VoiceFailureKind.HttpRejected => "http_status",
			VoiceFailureKind.ProviderRejected => "provider_rejected",
			VoiceFailureKind.InvalidResponse => "invalid_response",
			_ => "empty_response",
		};
		Dictionary<string, string> tags = new()
		{
			["failure_kind"] = kind,
			["provider"] = exception.Provider,
		};
		return new(BridgeFailureClass.ExternalService, "warn", true, tags);
	}

	private static BridgeFailure ExternalService(string kind) =>
		new(BridgeFailureClass.ExternalService, "warn", true, FailureKind(kind));

	private static BridgeFailure Unexpected() =>
		new(BridgeFailureClass.Unexpected, "error", true, FailureKind("unexpected"));

	private static IReadOnlyDictionary<string, string> FailureKind(string kind) =>
		new Dictionary<string, string> { ["failure_kind"] = kind };

	/// <summary>连接层失败 (TCP/DNS) 与 HTTP 状态失败分开计数, 便于区分网络故障与 Provider 拒绝。</summary>
	private static bool IsConnectFailure(HttpRequestException exception)
	{
		for (Exception? current = exception.InnerException; current is not null; current = current.InnerException)
		{
			if (current is SocketException) return true;
		}
		return false;
	}

	/// <summary>
	/// 聚合异常 (如 Task.WhenAll 同时有生产者与消费者失败) 取最严重的分类,
	/// 不让包装进 Aggregate 的噪音升级或降级语义。
	/// </summary>
	private static BridgeFailure ClassifyAggregate(AggregateException aggregate)
	{
		BridgeFailure worst = new(BridgeFailureClass.Cancelled, "info", false, FailureKind("cancelled"));
		foreach (Exception inner in aggregate.InnerExceptions)
		{
			BridgeFailure candidate = Classify(inner);
			if (Severity(candidate.Class) > Severity(worst.Class)) worst = candidate;
		}
		return worst;
	}

	private static int Severity(BridgeFailureClass value) => value switch
	{
		BridgeFailureClass.Cancelled => 0,
		BridgeFailureClass.Expected => 1,
		BridgeFailureClass.ExternalService => 2,
		_ => 3,
	};

	private static bool IsExpectedPluginFailure(string code) =>
		code is "plugin.invalid_manifest"
			or "plugin.duplicate_manifest_property"
			or "plugin.unknown_schema"
			or "plugin.incompatible_api"
			or "plugin.incompatible_host"
			or "plugin.unsupported_platform"
			or "plugin.unknown_capability"
			or "plugin.capability_missing"
			or "plugin.capability_not_granted"
			or "plugin.capability_unavailable"
			or "plugin.missing_dependency"
			or "plugin.dependency_cycle"
			or "plugin.invalid_dependency"
			or "plugin.duplicate_contribution"
			or "plugin.invalid_package"
			or "plugin.package_path_denied"
			or "plugin.contract_assembly_denied"
			or "plugin.forbidden_reference"
			or "plugin.entry_assembly_missing"
			or "plugin.entry_type_not_found"
			or "plugin.entry_constructor_missing"
			or "plugin.asset_denied"
			or "plugin.bridge_denied"
			or "plugin.safe_mode_disabled"
			or "plugin.startup_recovery_disabled"
			or "plugin.invalid_id"
			or "plugin.not_found"
			or "plugin.user_disabled"
			or "plugin.dependency_in_use";
}
