using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Nori.Core.Configuration;
using Nori.Core.Logging;
using Nori.Core.Mcp;
using Nori.Core.Memory;
using Nori.Core.Network;
using Nori.Core.Skills;
using Nori.Core.Tools;
using Nori.Desktop.Diagnostics;

namespace Nori.Desktop.Runtime;

/// <summary>
/// 原生设置界面的领域操作边界。
///
/// 页面只持有本类暴露的 typed patch，不直接读取配置键，也不复制桥接命令的业务规则。
/// BridgeCommands 后续仍保留旧 JSON 命令以兼容首次运行与旧前端。
/// </summary>
public sealed class SettingsOperations(AppRuntime runtime)
{
	private readonly AppRuntime _runtime = runtime;

	private ConfigStore Config => _runtime.Services.Config;

	/// <summary>读取当前脱敏快照</summary>
	public UiSnapshot Snapshot() => _runtime.BuildSnapshot();

	/// <summary>更新 AI 配置；ApiKey 为 null 表示不变，空串表示清除。</summary>
	public void UpdateAi(AiSettingsPatch patch)
	{
		SetText("llm_provider", patch.Provider);
		SetText("llm_api_base", patch.BaseUrl);
		SetSecret("llm_api_key", patch.ApiKey);
		SetText("llm_model", patch.Model);
		SetText("nori_user_persona", patch.Persona);
		Invalidate("ai");
	}

	/// <summary>按当前 provider 获取远程模型列表。</summary>
	public Task<IReadOnlyList<string>> FetchModelsAsync(string? provider, string baseUrl, string? apiKey, CancellationToken cancellationToken = default)
	{
		string key = apiKey ?? Config.GetStringOr("llm_api_key", "");
		return _runtime.Services.Llm.FetchModelsAsync(provider, baseUrl, key, cancellationToken);
	}

	/// <summary>更新语音配置。</summary>
	public void UpdateVoice(VoiceSettingsPatch patch)
	{
		if (patch.Volume is { } volume)
		{
			_runtime.Voice.SetVolume(volume);
		}
		SetText("tts_provider", patch.TtsProvider);
		SetText("tts_base_url", patch.TtsBaseUrl);
		SetSecret("tts_api_key", patch.TtsApiKey);
		SetText("tts_voice", patch.TtsVoice);
		SetNumber("tts_speed", patch.TtsSpeed);
		SetBool("tts_auto_play", patch.TtsAutoPlay);
		SetText("gptsovits_base_url", patch.GptsovitsBaseUrl);
		SetText("gptsovits_ref_audio", patch.GptsovitsRefAudio);
		SetText("gptsovits_prompt_text", patch.GptsovitsPromptText);
		SetText("gptsovits_prompt_lang", patch.GptsovitsPromptLang);
		SetText("stt_provider", patch.SttProvider);
		SetText("stt_base_url", patch.SttBaseUrl);
		SetSecret("stt_api_key", patch.SttApiKey);
		Invalidate("voice");
	}

	/// <summary>确认已经看过旧浏览器语音提示。</summary>
	public void AcknowledgeVoiceNotice()
	{
		SetText("voice_notice_pending", "0");
		Invalidate("voice");
	}

	/// <summary>更新通用设置。</summary>
	public void UpdateGeneral(GeneralSettingsPatch patch)
	{
		SetText(ConfigStore.KeyLanguage, patch.Language);
		SetBool("pet_auto_summon", patch.PetAutoSummon);
		Invalidate("general");
	}

	/// <summary>更新主动交互设置。</summary>
	public void UpdateProactive(ProactiveSettingsPatch patch)
	{
		SetBool("proactive_idle_enabled", patch.IdleEnabled);
		SetNumber("proactive_idle_minutes", patch.IdleMinutes);
		SetBool("proactive_daily_greeting", patch.DailyGreeting);
		Invalidate("proactive");
	}

	/// <summary>更新 Embedding 配置。</summary>
	public void UpdateEmbedding(EmbeddingSettingsPatch patch)
	{
		SetText("embedding_model", patch.Model);
		SetText("embedding_api_base", patch.BaseUrl);
		SetSecret("embedding_api_key", patch.ApiKey);
		SetText("embedding_dimensions", patch.Dimensions);
		Invalidate("embedding");
	}

	/// <summary>试听当前 TTS 配置。</summary>
	public Task TestVoiceAsync(string? text = null, CancellationToken cancellationToken = default) =>
		_runtime.Voice.SpeakAsync(
			string.IsNullOrWhiteSpace(text) ? "主人好呀！我是 Nori，这是一条声音播放测试~" : text,
			cancellationToken: cancellationToken);

	/// <summary>添加一条手动记忆。</summary>
	public async Task<MemoryItem> AddMemoryAsync(string content, double importance, string? tags, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		MemoryItem item = await _runtime.Memory.AddAsync(content, "manual", importance, tags, "manual");
		Invalidate("memory");
		return item;
	}

	/// <summary>读取记忆列表。</summary>
	public IReadOnlyList<MemoryItem> ListMemories(int limit = 50) => _runtime.Services.Memory.GetAll(limit);

	/// <summary>按关键词执行混合检索。</summary>
	public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(string keyword, int limit = 50, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return _runtime.Memory.SearchHybridAsync(keyword, limit);
	}

	/// <summary>删除一条记忆。</summary>
	public bool DeleteMemory(long id)
	{
		bool deleted = _runtime.Services.Memory.Delete(id);
		if (deleted) Invalidate("memory");
		return deleted;
	}

	/// <summary>清空全部记忆。</summary>
	public void ClearMemories()
	{
		_runtime.Services.Memory.Clear();
		_runtime.Memory.ClearCache();
		Invalidate("memory");
	}

	/// <summary>重建全部记忆向量。</summary>
	public async Task<int> ReembedMemoriesAsync(CancellationToken cancellationToken = default)
	{
		int count = await _runtime.Memory.ReembedAllAsync(cancellationToken);
		Invalidate("memory");
		return count;
	}

	/// <summary>读取技能市场。</summary>
	public IReadOnlyList<SkillRecord> MarketplaceSkills() => SkillService.Marketplace();

	/// <summary>启用或停用技能。</summary>
	public void ToggleSkill(string id, bool enabled)
	{
		if (!_runtime.Skills.Toggle(id, enabled)) throw new InvalidOperationException($"未找到技能: {id}");
		Invalidate("skills");
	}

	/// <summary>从市场安装预设技能。</summary>
	public SkillRecord InstallMarketplaceSkill(string id)
	{
		SkillRecord result = _runtime.Skills.InstallFromMarketplace(id);
		Invalidate("skills");
		return result;
	}

	/// <summary>从 URL 安装技能。</summary>
	public async Task<SkillRecord> InstallSkillFromUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		SkillRecord result = await _runtime.Skills.InstallFromUrlAsync(url, cancellationToken);
		Invalidate("skills");
		return result;
	}

	/// <summary>保存自定义技能。</summary>
	public SkillRecord SaveSkill(SkillRecord skill)
	{
		SkillRecord result = _runtime.Skills.SaveCustom(skill);
		Invalidate("skills");
		return result;
	}

	/// <summary>卸载技能。</summary>
	public bool UninstallSkill(string id)
	{
		bool result = _runtime.Skills.Uninstall(id);
		if (result) Invalidate("skills");
		return result;
	}

	/// <summary>读取技能指令正文。</summary>
	public string ExportSkill(string id) => _runtime.Skills.Export(id);

	/// <summary>读取 MCP 服务列表并刷新动态工具。</summary>
	public async Task<IReadOnlyList<McpServerStatusInfo>> GetMcpServersAsync()
	{
		IReadOnlyList<McpServerStatusInfo> result = await _runtime.Services.Mcp.GetServersAsync();
		await _runtime.RefreshMcpToolsAsync();
		Invalidate("mcp", "tools");
		return result;
	}

	/// <summary>从公开 URL 导入 MCP 配置。</summary>
	public async Task ImportMcpUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		Uri uri = new(url);
		UrlAccessPolicy.EnsurePublicHttp(uri);
		using HttpResponseMessage response = await UrlAccessPolicy.GetWithSafeRedirectsAsync(_runtime.Services.Http, uri, allowPrivate: false, cancellationToken: cancellationToken);
		if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
		string text = await response.Content.ReadAsStringAsync(cancellationToken);
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		List<McpServerConfig> configs = [];
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("mcpServers", out JsonElement servers) && servers.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty server in servers.EnumerateObject()) configs.Add(ParseImportedMcp(server.Name, server.Value));
		}
		else if (root.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in root.EnumerateArray()) configs.Add(ParseImportedMcp(GetString(item, "name") ?? "导入的 MCP 服务", item));
		}
		else if (root.ValueKind == JsonValueKind.Object)
		{
			configs.Add(ParseImportedMcp(GetString(root, "name") ?? GetString(root, "id") ?? "导入的 MCP 服务", root));
		}
		if (configs.Count == 0) throw new InvalidOperationException("未识别的 MCP 配置结构");
		foreach (McpServerConfig config in configs) await SaveMcpServerAsync(config);
	}

	/// <summary>保存 MCP 服务。</summary>
	public async Task<McpServerStatusInfo> SaveMcpServerAsync(McpServerConfig config)
	{
		McpServerStatusInfo result = await _runtime.Services.Mcp.SaveServerAsync(config);
		await _runtime.RefreshMcpToolsAsync();
		Invalidate("mcp", "tools");
		return result;
	}

	private static McpServerConfig ParseImportedMcp(string name, JsonElement source)
	{
		List<string> args = [];
		if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("args", out JsonElement argsElement) && argsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement value in argsElement.EnumerateArray()) if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text) args.Add(text);
		}
		string? url = GetString(source, "url");
		return new McpServerConfig
		{
			Id = $"mcp_import_{Guid.NewGuid():N}"[..20],
			Name = name,
			Transport = url is null ? McpTransportType.Stdio : McpTransportType.Sse,
			Command = GetString(source, "command") ?? "npx",
			Args = args.ToArray(),
			Url = url,
			Enabled = true,
			AutoConnect = true,
		};
	}

	private static string? GetString(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	/// <summary>删除 MCP 服务。</summary>
	public async Task<bool> DeleteMcpServerAsync(string id)
	{
		bool result = await _runtime.Services.Mcp.DeleteServerAsync(id);
		await _runtime.RefreshMcpToolsAsync();
		Invalidate("mcp", "tools");
		return result;
	}

	/// <summary>连接 MCP 服务。</summary>
	public async Task<McpServerStatusInfo> ConnectMcpServerAsync(string id)
	{
		McpServerStatusInfo result = await _runtime.Services.Mcp.ConnectServerAsync(id);
		await _runtime.RefreshMcpToolsAsync();
		Invalidate("mcp", "tools");
		return result;
	}

	/// <summary>断开 MCP 服务。</summary>
	public async Task<McpServerStatusInfo> DisconnectMcpServerAsync(string id)
	{
		McpServerStatusInfo result = await _runtime.Services.Mcp.DisconnectServerAsync(id);
		await _runtime.RefreshMcpToolsAsync();
		Invalidate("mcp", "tools");
		return result;
	}

	/// <summary>测试 MCP 服务配置。</summary>
	public Task<McpServerStatusInfo> TestMcpServerAsync(McpServerConfig config) => _runtime.Services.Mcp.TestServerAsync(config);

	/// <summary>调用 MCP 工具。</summary>
	public async Task<object?> CallMcpToolAsync(string serverId, string toolName, JsonObject? arguments, CancellationToken cancellationToken = default)
	{
		McpToolResult result = await _runtime.Services.Mcp.CallToolAsync(serverId, toolName, arguments, cancellationToken);
		if (result.IsError) throw new InvalidOperationException(result.AsText());
		return result.AsText();
	}

	/// <summary>启用或停用内置工具。</summary>
	public void ToggleTool(string name, bool enabled)
	{
		if (!_runtime.Tools.SetEnabled(name, enabled)) throw new InvalidOperationException($"未找到工具: {name}");
		PersistDisabledTools();
		Invalidate("tools");
	}

	/// <summary>执行设置页手动测试，仅允许 safe 工具。</summary>
	public async Task<object?> ExecuteSafeToolAsync(string name, JsonNode? arguments)
	{
		RegisteredTool tool = _runtime.Tools.Get(name) ?? throw new InvalidOperationException($"未找到工具: {name}");
		if (tool.PermissionLevel != "safe") throw new InvalidOperationException($"{name} 标记为 {tool.PermissionLevel}, 手动测试仅支持 safe 工具");
		ToolResult result = await _runtime.Tools.ExecuteAsync(name, arguments);
		if (result.Error is not null) throw new InvalidOperationException(result.Error);
		return result.Result;
	}

	/// <summary>读取最近日志。</summary>
	public IReadOnlyList<LogSnapshot> RecentLogs() => _runtime.Services.Logger.RecentLogs().Select(entry => new LogSnapshot
	{
		Time = entry.Time,
		Level = entry.Level,
		Source = entry.Source == LogSource.Frontend ? "frontend" : "backend",
		Message = entry.Message,
	}).ToArray();

	/// <summary>清空内存日志缓冲。</summary>
	public void ClearRecentLogs() => _runtime.Services.Logger.ClearRecentLogs();

	/// <summary>读取诊断信息。</summary>
	public Dictionary<string, string> DiagnosticInfo() => Nori.Desktop.Diagnostics.DiagnosticInfo.Build();

	/// <summary>打开日志目录。</summary>
	public void OpenLogFolder()
	{
		Directory.CreateDirectory(Nori.Core.Data.AppPaths.LogDir);
		Process.Start(new ProcessStartInfo {FileName = Nori.Core.Data.AppPaths.LogDir, UseShellExecute = true});
	}

	/// <summary>触发垃圾回收并返回释放的托管内存。</summary>
	public long CollectGarbage()
	{
		long before = GC.GetTotalMemory(false);
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true);
		long after = GC.GetTotalMemory(true);
		long released = Math.Max(0, before - after);
		_runtime.Services.Logger.Write(LogSource.Backend, "info", $"调试垃圾回收完成: 释放 {released} 字节");
		return released;
	}

	/// <summary>写入一条测试日志。</summary>
	public void WriteTestLog() => _runtime.Services.Logger.Write(LogSource.Backend, "warn", "调试页测试日志: 原生设置页 → 宿主日志链路正常");

	/// <summary>触发宿主崩溃探针。</summary>
	public void TriggerCrashTest(string mode)
	{
		switch (mode)
		{
			case "ui_thread":
				Dispatcher.UIThread.Post(() => throw new InvalidOperationException("调试崩溃测试: UI 线程未处理异常"));
				break;
			case "background_thread":
				new Thread(() => throw new InvalidOperationException("调试崩溃测试: 后台线程未处理异常")).Start();
				break;
			case "unobserved_task":
				_ = Task.Run(() => throw new InvalidOperationException("调试崩溃测试: 未观察任务异常"));
				break;
			default:
				throw new InvalidOperationException($"未知的崩溃测试模式: {mode}");
		}
	}

	/// <summary>创建定时提醒。</summary>
	public Nori.Core.Proactive.ReminderItem AddReminder(string content, double delayMinutes)
	{
		Nori.Core.Proactive.ReminderItem item = _runtime.Proactive.AddReminder(content, delayMinutes);
		Invalidate("proactive");
		return item;
	}

	/// <summary>取消定时提醒。</summary>
	public bool CancelReminder(string id)
	{
		bool result = _runtime.Proactive.CancelReminder(id);
		Invalidate("proactive");
		return result;
	}

	private void SetText(string key, string? value)
	{
		if (value is not null) Config.Set(key, new ConfigValue.Text(value));
	}

	private void SetSecret(string key, string? value)
	{
		if (value is null) return;
		if (value.Length == 0)
		{
			Config.Delete(key);
			return;
		}
		Config.Set(key, new ConfigValue.Text(value));
		_runtime.Services.Logger.Write(LogSource.Backend, "info", $"已更新敏感配置: {key}");
	}

	private void SetBool(string key, bool? value)
	{
		if (value is not { } actual) return;
		Config.Set(key, new ConfigValue.Text(actual ? "1" : "0"));
	}

	private void SetNumber(string key, double? value)
	{
		if (value is not { } actual) return;
		Config.Set(key, new ConfigValue.Text(actual.ToString(CultureInfo.InvariantCulture)));
	}

	private void Invalidate(params string[] topics) => _runtime.InvalidateSnapshot(topics);

	private void PersistDisabledTools()
	{
		IReadOnlyList<string> disabled = _runtime.Tools.DisabledNames();
		JsonNode? node = JsonNode.Parse(JsonSerializer.Serialize(disabled));
		if (node is not null) Config.Set("tools_disabled", new ConfigValue.Json(node));
	}
}

public sealed record AiSettingsPatch
{
	public string? Provider { get; init; }
	public string? BaseUrl { get; init; }
	public string? ApiKey { get; init; }
	public string? Model { get; init; }
	public string? Persona { get; init; }
}

public sealed record VoiceSettingsPatch
{
	public double? Volume { get; init; }
	public string? TtsProvider { get; init; }
	public string? TtsBaseUrl { get; init; }
	public string? TtsApiKey { get; init; }
	public string? TtsVoice { get; init; }
	public double? TtsSpeed { get; init; }
	public bool? TtsAutoPlay { get; init; }
	public string? GptsovitsBaseUrl { get; init; }
	public string? GptsovitsRefAudio { get; init; }
	public string? GptsovitsPromptText { get; init; }
	public string? GptsovitsPromptLang { get; init; }
	public string? SttProvider { get; init; }
	public string? SttBaseUrl { get; init; }
	public string? SttApiKey { get; init; }
}

public sealed record GeneralSettingsPatch
{
	public string? Language { get; init; }
	public bool? PetAutoSummon { get; init; }
}

public sealed record ProactiveSettingsPatch
{
	public bool? IdleEnabled { get; init; }
	public double? IdleMinutes { get; init; }
	public bool? DailyGreeting { get; init; }
}

public sealed record EmbeddingSettingsPatch
{
	public string? Model { get; init; }
	public string? BaseUrl { get; init; }
	public string? ApiKey { get; init; }
	public string? Dimensions { get; init; }
}
