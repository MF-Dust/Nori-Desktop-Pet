using Nori.Desktop.Bridge;

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
	[InlineData("get_diagnostic_info", BridgeCommandDomain.Diagnostics)]
	[InlineData("chat_start", BridgeCommandDomain.Application)]
	public void 命令按稳定领域分类(string command, BridgeCommandDomain expected)
	{
		Assert.Equal(expected, BridgeCommandRouter.Classify(command));
	}

	[Fact]
	public void 未知命令保留Other兜底()
	{
		Assert.Equal(BridgeCommandDomain.Other, BridgeCommandRouter.Classify("future_command"));
	}
}
