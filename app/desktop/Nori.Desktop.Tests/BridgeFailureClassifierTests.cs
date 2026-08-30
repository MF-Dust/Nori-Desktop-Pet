using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Resources;
using Nori.Core.Voice;
using Nori.Desktop.Bridge;
using Nori.PluginRuntime;

namespace Nori.Desktop.Tests;

/// <summary>
/// Bridge 失败分类测试: 只按异常类型与稳定错误码分类, 断言不依赖异常 Message。
/// </summary>
public sealed class BridgeFailureClassifierTests
{
	[Theory]
	[InlineData(typeof(ChatException))]
	[InlineData(typeof(ResourceException))]
	[InlineData(typeof(UriFormatException))]
	[InlineData(typeof(ArgumentException))]
	[InlineData(typeof(InvalidOperationException))]
	[InlineData(typeof(JsonException))]
	[InlineData(typeof(UnauthorizedAccessException))]
	public void 领域与校验异常分类为Expected且不发遥测(Type exceptionType)
	{
		Exception exception = exceptionType switch
		{
			_ when exceptionType == typeof(ChatException) => new ChatException("测试消息"),
			_ when exceptionType == typeof(ResourceException) => new ResourceException("测试消息"),
			_ when exceptionType == typeof(UriFormatException) => new UriFormatException("测试消息"),
			_ when exceptionType == typeof(ArgumentException) => new ArgumentException("测试消息"),
			_ when exceptionType == typeof(JsonException) => new JsonException("测试消息"),
			_ when exceptionType == typeof(UnauthorizedAccessException) => new UnauthorizedAccessException("测试消息"),
			_ => new InvalidOperationException("测试消息"),
		};

		BridgeFailure failure = BridgeFailureClassifier.Classify(exception);

		Assert.Equal(BridgeFailureClass.Expected, failure.Class);
		Assert.Equal("warn", failure.LogLevel);
		Assert.False(failure.Telemetry);
		Assert.NotNull(failure.Tags);
		Assert.Equal("validation", failure.Tags!["failure_kind"]);
	}

	[Fact]
	public void HTTP状态失败分类为ExternalService并标注failure_kind()
	{
		BridgeFailure failure = BridgeFailureClassifier.Classify(
			new HttpRequestException("测试消息", inner: null, HttpStatusCode.TooManyRequests));

		Assert.Equal(BridgeFailureClass.ExternalService, failure.Class);
		Assert.Equal("warn", failure.LogLevel);
		Assert.True(failure.Telemetry);
		Assert.Equal("http_status", failure.Tags!["failure_kind"]);
	}

	[Fact]
	public void 连接层失败标注connect()
	{
		HttpRequestException exception = new("测试消息", new SocketException((int)SocketError.HostUnreachable));

		BridgeFailure failure = BridgeFailureClassifier.Classify(exception);

		Assert.Equal(BridgeFailureClass.ExternalService, failure.Class);
		Assert.True(failure.Telemetry);
		Assert.Equal("connect", failure.Tags!["failure_kind"]);
	}

	[Theory]
	[InlineData(typeof(TaskCanceledException))]
	[InlineData(typeof(TimeoutException))]
	public void 超时失败分类为ExternalService并标注timeout(Type exceptionType)
	{
		Exception exception = exceptionType == typeof(TaskCanceledException)
			? new TaskCanceledException("测试消息")
			: (Exception)new TimeoutException("测试消息");

		BridgeFailure failure = BridgeFailureClassifier.Classify(exception);

		Assert.Equal(BridgeFailureClass.ExternalService, failure.Class);
		Assert.True(failure.Telemetry);
		Assert.Equal("timeout", failure.Tags!["failure_kind"]);
	}

	[Fact]
	public void 取消分类为Cancelled且不发遥测()
	{
		BridgeFailure failure = BridgeFailureClassifier.Classify(new OperationCanceledException("测试消息"));

		Assert.Equal(BridgeFailureClass.Cancelled, failure.Class);
		Assert.Equal("info", failure.LogLevel);
		Assert.False(failure.Telemetry);
	}

	[Theory]
	[InlineData("plugin.not_found")]
	[InlineData("plugin.package_path_denied")]
	[InlineData("plugin.capability_not_granted")]
	public void 预期插件错误码分类为Expected(string code)
	{
		BridgeFailure failure = BridgeFailureClassifier.Classify(new PluginException(code, "测试消息"));

		Assert.Equal(BridgeFailureClass.Expected, failure.Class);
		Assert.Equal("warn", failure.LogLevel);
		Assert.False(failure.Telemetry);
	}

	[Fact]
	public void 非预期插件错误码分类为Unexpected并上报()
	{
		BridgeFailure failure = BridgeFailureClassifier.Classify(new PluginException("plugin.activation_failed", "测试消息"));

		Assert.Equal(BridgeFailureClass.Unexpected, failure.Class);
		Assert.Equal("error", failure.LogLevel);
		Assert.True(failure.Telemetry);
	}

	[Theory]
	[InlineData(typeof(NullReferenceException))]
	[InlineData(typeof(TypeLoadException))]
	[InlineData(typeof(System.Runtime.InteropServices.COMException))]
	public void 程序缺陷分类为Unexpected并上报(Type exceptionType)
	{
		Exception exception = (Exception)Activator.CreateInstance(exceptionType, "测试消息")!;

		BridgeFailure failure = BridgeFailureClassifier.Classify(exception);

		Assert.Equal(BridgeFailureClass.Unexpected, failure.Class);
		Assert.Equal("error", failure.LogLevel);
		Assert.True(failure.Telemetry);
	}

	[Fact]
	public void VoiceProvider失败分类为ExternalService并携带provider标签()
	{
		BridgeFailure rejected = BridgeFailureClassifier.Classify(
			new VoiceProviderException("minimax", VoiceFailureKind.ProviderRejected, "status_code=1008", providerStatusCode: 1008));
		Assert.Equal(BridgeFailureClass.ExternalService, rejected.Class);
		Assert.True(rejected.Telemetry);
		Assert.Equal("provider_rejected", rejected.Tags!["failure_kind"]);
		Assert.Equal("minimax", rejected.Tags!["provider"]);

		BridgeFailure network = BridgeFailureClassifier.Classify(
			new VoiceProviderException("openai", VoiceFailureKind.Network, "网络失败"));
		Assert.Equal("connect", network.Tags!["failure_kind"]);

		BridgeFailure http = BridgeFailureClassifier.Classify(
			new VoiceProviderException("gemini", VoiceFailureKind.HttpRejected, "HTTP 429", httpStatusCode: 429));
		Assert.Equal("http_status", http.Tags!["failure_kind"]);
		Assert.Equal("gemini", http.Tags!["provider"]);
	}

	[Fact]
	public void 聚合异常取最严重分类()
	{
		AggregateException aggregate = new(
			new HttpRequestException("测试消息"),
			new OperationCanceledException("测试消息"));

		BridgeFailure failure = BridgeFailureClassifier.Classify(aggregate);

		Assert.Equal(BridgeFailureClass.ExternalService, failure.Class);
		Assert.True(failure.Telemetry);
	}

	[Fact]
	public void 分类不依赖异常Message()
	{
		// 同一类型换个 message (含 Error/cancelled/timeout 等字样) 分类必须一致。
		BridgeFailure plain = BridgeFailureClassifier.Classify(new InvalidOperationException("测试消息"));
		BridgeFailure noisy = BridgeFailureClassifier.Classify(new InvalidOperationException(
			"cancelled timeout HTTP 429 plugin.not_found /home/user/secret"));

		Assert.Equal(plain.Class, noisy.Class);
		Assert.Equal(plain.Telemetry, noisy.Telemetry);
		Assert.Equal(plain.Tags!["failure_kind"], noisy.Tags!["failure_kind"]);
	}
}
