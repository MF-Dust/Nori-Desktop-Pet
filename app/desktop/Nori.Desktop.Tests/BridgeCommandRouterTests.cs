using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Nori.Desktop.Automation;
using Nori.Desktop.Bridge;
using Nori.PluginRuntime;

namespace Nori.Desktop.Tests;

public sealed class BridgeCommandRouterTests
{
	[Theory]
	[InlineData("window_show", BridgeCommandDomain.Window)]
	[InlineData("automation_browser_status", BridgeCommandDomain.Automation)]
	[InlineData("llm_fetch_models", BridgeCommandDomain.Ai)]
	[InlineData("settings_update_voice", BridgeCommandDomain.Voice)]
	[InlineData("model_select", BridgeCommandDomain.Model)]
	[InlineData("memory_list", BridgeCommandDomain.Memory)]
	[InlineData("skills_toggle", BridgeCommandDomain.Skills)]
	[InlineData("mcp_get_servers", BridgeCommandDomain.Mcp)]
	[InlineData("tools_set_enabled", BridgeCommandDomain.Tools)]
	[InlineData("plugin_list", BridgeCommandDomain.Plugins)]
	[InlineData("get_diagnostic_info", BridgeCommandDomain.Diagnostics)]
	[InlineData("chat_start", BridgeCommandDomain.Application)]
	public void 命令按稳定领域分类(string command, BridgeCommandDomain expected)
	{
		Assert.Equal(expected, BridgeCommandRouter.Classify(command));
	}

	[Fact]
	public async Task PluginWidgetsUsesWidgetsEnvelope()
	{
		string root = Path.Combine(Path.GetTempPath(), $"nori-plugin-widgets-{Guid.NewGuid():N}");
		try
		{
			await using PluginRuntimeHost runtime = new(new PluginRuntimeHostOptions { DataDirectory = root });
			AppServices services = (AppServices)RuntimeHelpers.GetUninitializedObject(typeof(AppServices));
			services.PluginRuntime = runtime;
			BridgeCommandRouter router = new(services);
			using JsonDocument arguments = JsonDocument.Parse("{}");
			object? result = await router.InvokeAsync(new TestBridgeSource(), "plugin_widgets", arguments.RootElement);

			string json = JsonSerializer.Serialize(result, BridgeJson.Options);
			Assert.Equal("{\"widgets\":[]}", json);
		}
		finally
		{
			try { Directory.Delete(root, true); } catch (IOException) { }
		}
	}

	[Fact]
	public void 浏览器降级状态使用camelCase枚举值()
	{
		string json = JsonSerializer.Serialize(BridgeCommandRouter.BrowserUnavailableStatus(), BridgeJson.Options);

		Assert.Contains("\"state\":\"stopped\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"state\":\"Stopped\"", json, StringComparison.Ordinal);
	}

	[Fact]
	public void 视觉探测结果使用reason字段()
	{
		string json = JsonSerializer.Serialize(new AutomationVisionProbeSnapshot(false, "测试原因"), BridgeJson.Options);

		Assert.Contains("\"reason\":\"测试原因\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("\"message\"", json, StringComparison.Ordinal);
	}

	[Fact]
	public void 未知命令保留Other兜底()
	{
		Assert.Equal(BridgeCommandDomain.Other, BridgeCommandRouter.Classify("future_command"));
	}

	private sealed class TestBridgeSource : IBridgeSource
	{
		public string Label => "main";
		public bool IsVisible => true;
		public Window? Self => null;
		public void PostEvent(string name, object? payload) { }
		public void PostResult(long id, object? value, string? error) { }
	}
}
