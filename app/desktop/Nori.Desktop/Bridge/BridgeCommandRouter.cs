using System.Text.Json;
using Nori.Desktop.Automation.Browser;

namespace Nori.Desktop.Bridge;

/// <summary>Bridge 命令所属领域；用于把传输、策略与具体命令实现逐步解耦。</summary>
public enum BridgeCommandDomain
{
	Application,
	Window,
	Automation,
	Ai,
	Voice,
	Model,
	Memory,
	Skills,
	Mcp,
	Tools,
	Diagnostics,
	Other,
}

/// <summary>
/// Bridge 的领域路由边界。
///
/// 当前具体业务实现仍由 BridgeCommands 承担；新路由先把 NoriBridge 的传输职责与
/// 命令级运行组件策略隔离。后续领域 handler 可以逐个迁出，而不再修改 WebView 传输层。
/// </summary>
public sealed class BridgeCommandRouter(AppServices services)
{
	private readonly AppServices _services = services;

	public async Task<object?> InvokeAsync(
		IBridgeSource source,
		string command,
		JsonElement args,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Browser Automation 是可选 feature pack。发布瘦身包不携带 driver 时，
		// 在路由边界就明确 fail-closed，而不是让传输层或 Playwright 内部报路径错误。
		if (Classify(command) == BridgeCommandDomain.Automation
			&& command.StartsWith("automation_browser_", StringComparison.Ordinal))
		{
			bool available = PlaywrightRuntimeAvailability.IsAvailable();
			if (!available && command is "automation_browser_start" or "automation_browser_start_task")
			{
				throw new InvalidOperationException(PlaywrightRuntimeAvailability.MissingReason);
			}
			if (!available && command == "automation_browser_status")
			{
				return new
				{
					state = "Stopped",
					enabled = false,
					available = false,
					unavailableReason = PlaywrightRuntimeAvailability.MissingReason,
					running = false,
				};
			}
		}

		return await _services.Commands.InvokeAsync(source, command, args, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>纯命令名分类，不读取用户状态或参数。</summary>
	public static BridgeCommandDomain Classify(string command)
	{
		if (string.IsNullOrWhiteSpace(command)) return BridgeCommandDomain.Other;
		if (command.StartsWith("window_", StringComparison.Ordinal)) return BridgeCommandDomain.Window;
		if (command.StartsWith("automation_", StringComparison.Ordinal)) return BridgeCommandDomain.Automation;
		if (command.StartsWith("llm_", StringComparison.Ordinal)
			|| command.StartsWith("ai_", StringComparison.Ordinal)
			|| command.StartsWith("embedding_", StringComparison.Ordinal)
			|| command.StartsWith("settings_update_ai", StringComparison.Ordinal)
			|| command.StartsWith("settings_test_ai", StringComparison.Ordinal)
			|| command.StartsWith("settings_update_embedding", StringComparison.Ordinal)
			|| command.StartsWith("settings_test_embedding", StringComparison.Ordinal))
			return BridgeCommandDomain.Ai;
		if (command.StartsWith("tts_", StringComparison.Ordinal)
			|| command.StartsWith("stt_", StringComparison.Ordinal)
			|| command.StartsWith("audio_", StringComparison.Ordinal)
			|| command.StartsWith("settings_update_voice", StringComparison.Ordinal))
			return BridgeCommandDomain.Voice;
		if (command.StartsWith("model_", StringComparison.Ordinal) || command.StartsWith("pet_", StringComparison.Ordinal))
			return BridgeCommandDomain.Model;
		if (command.StartsWith("memory_", StringComparison.Ordinal)) return BridgeCommandDomain.Memory;
		if (command.StartsWith("skills_", StringComparison.Ordinal)) return BridgeCommandDomain.Skills;
		if (command.StartsWith("mcp_", StringComparison.Ordinal)) return BridgeCommandDomain.Mcp;
		if (command.StartsWith("tools_", StringComparison.Ordinal)) return BridgeCommandDomain.Tools;
		if (command is "get_recent_logs" or "clear_recent_logs" or "get_diagnostic_info" or "export_diagnostics" or "open_log_folder" or "run_gc_collect" or "debug_crash_test")
			return BridgeCommandDomain.Diagnostics;
		if (command.StartsWith("chat_", StringComparison.Ordinal)
			|| command.StartsWith("approval_", StringComparison.Ordinal)
			|| command.StartsWith("reminder_", StringComparison.Ordinal)
			|| command.StartsWith("settings_", StringComparison.Ordinal)
			|| command is "ui_get_snapshot" or "exit_app" or "write_log" or "get_system_language" or "complete_first_run" or "init_ready" or "init_enter_main" or "get_init_config" or "clipboard_write_text" or "open_url")
			return BridgeCommandDomain.Application;
		return BridgeCommandDomain.Other;
	}
}
