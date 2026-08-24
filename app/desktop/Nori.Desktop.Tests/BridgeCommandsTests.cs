using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Live2D;
using Nori.Core.Mcp;
using Nori.Core.Resources;
using Nori.Desktop.Bridge;
using Nori.Desktop.Runtime;
using Nori.Desktop.Windows;
using Avalonia.Controls;

namespace Nori.Desktop.Tests;

/// <summary>
/// 后端化桥接命令面测试: 来源授权、快照脱敏、历史规范化与提醒持久化
/// </summary>
public class BridgeCommandsTests : IDisposable
{
	private sealed class FakeBridgeSource(string label, bool isVisible = true) : IBridgeSource
	{
		public string Label => label;
		public bool IsVisible => isVisible;
		public Window? Self => null;
		public List<(string Name, object? Payload)> Events { get; } = [];

		public void PostEvent(string name, object? payload) => Events.Add((name, payload));

		public void PostResult(long id, object? value, string? error)
		{
		}
	}

	private sealed class FakeWindowManager : IWindowManager
	{
		public List<(string Name, object? Payload)> Broadcasts { get; } = [];
		private readonly Dictionary<string, bool> _visible = [];

		public event Action<string, bool>? VisibilityChanged;

		public Window? Get(string? label) => null;
		public NoriWindow? GetNoriWindow(string? label) => null;
		public PetWindow? Pet => null;

		public void CreateAll(NoriBridge bridge, AppServices services)
		{
		}
		public void Show(string label) => SetVisible(label, true);

		public void Hide(string label) => SetVisible(label, false);

		public void Close(string label) => SetVisible(label, false);

		public void TogglePet() => SetVisible(WindowLabels.Pet, !IsWindowVisible(WindowLabels.Pet));

		public bool IsWindowVisible(string label) => _visible.TryGetValue(label, out bool visible) && visible;

		private void SetVisible(string label, bool visible)
		{
			if (IsWindowVisible(label) == visible) return;
			_visible[label] = visible;
			VisibilityChanged?.Invoke(label, visible);
		}

		public void Broadcast(string name, object? payload) => Broadcasts.Add((name, payload));

		public void ShowPetSpeech(string text)
		{
		}

		public void ClearPetSpeech()
		{
		}

		public void Shutdown()
		{
		}
	}

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"nori-bridge-{Guid.NewGuid():N}");
	private readonly string _dbPath;
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;
	private readonly HttpClient _http;
	private readonly FakeWindowManager _windows = new();
	private readonly AppServices _services;
	private readonly AppRuntime _runtime;

	public BridgeCommandsTests()
	{
		Directory.CreateDirectory(_tempDir);
		_dbPath = Path.Combine(_tempDir, "nori.db");
		_database = NoriDatabase.Open(_dbPath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_http = new HttpClient();
		_services = new AppServices
		{
			Database = _database,
			Config = _config,
			Logger = new FileLogger(Path.Combine(_tempDir, "logs")),
			Resources = new Nori.Core.Resources.ResourceManager(_tempDir),
			Chat = new ChatService(_http, _database, _config),
			Memory = new Nori.Core.Memory.MemoryStore(_database),
			Embedding = new Nori.Core.Embedding.OpenAiEmbeddingAdapter(_http),
			Llm = new LlmClient(_http),
			Mcp = new McpManager(_http, _config),
			Http = _http,
			AgentOperations = new AgentOperationRegistry(),
			Windows = _windows,
		};
		_runtime = new AppRuntime(_services);
		_services.Runtime = _runtime;
	}

	public void Dispose()
	{
		_runtime.DisposeAsync().GetAwaiter().GetResult();
		_database.Dispose();
		_http.Dispose();
		try
		{
			Directory.Delete(_tempDir, true);
		}
		catch (IOException)
		{
		}
	}

	private BridgeCommands CreateCommands() => new(_services, action => action());

	private static JsonElement Args(object payload) =>
		JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase});

	// ---- 快照与秘密 ----

	[Fact]
	public async Task model_interactions按模型保存并返回()
	{
		string modelDir = _services.Resources.ResourceDir(ResourceType.Live2D, "nori");
		Directory.CreateDirectory(modelDir);
		File.WriteAllText(Path.Combine(modelDir, "nori.model3.json"), """
			{
				"FileReferences": {
					"Moc": "nori.moc3",
					"Textures": [],
					"Expressions": [{"Name": "01_Smile", "File": "01_Smile.exp3.json"}],
					"Motions": {"Reactions": [{"File": "motions/01_Nod.motion3.json"}]}
				}
			}
			""");
		File.WriteAllText(Path.Combine(modelDir, "nori.moc3"), "MOC3");
		File.WriteAllText(Path.Combine(modelDir, "01_Smile.exp3.json"), "{}");
		Directory.CreateDirectory(Path.Combine(modelDir, "motions"));
		File.WriteAllText(Path.Combine(modelDir, "motions", "01_Nod.motion3.json"), "{}");
		PetInteractionConfig config = new()
		{
			Regions =
			[
				new PetInteractionRegion
				{
					Id = "head",
					Name = "头部",
					Rect = new PetInteractionRect {X = 0.2, Y = 0.1, Width = 0.3, Height = 0.2},
					Motion = new PetInteractionAction
					{
						Mode = PetInteractionActionMode.Selected,
						Group = "Reactions",
						Name = "01_Nod",
					},
					Expression = new PetInteractionAction
					{
						Mode = PetInteractionActionMode.Selected,
						Name = "01_Smile",
					},
				},
			],
		};
		JsonElement interactionJson = JsonSerializer.Deserialize<JsonElement>(config.ToJsonNode().ToJsonString());
		BridgeCommands commands = CreateCommands();
		FakeBridgeSource source = new(WindowLabels.Main);

		await commands.InvokeAsync(source, "model_set_interactions", Args(new {modelId = "nori", interactions = interactionJson}));
		object? meta = await commands.InvokeAsync(source, "model_get_meta", Args(new {modelId = "nori"}));
		string json = JsonSerializer.Serialize(meta, BridgeJson.Options);

		Assert.Contains("\"head\"", json, StringComparison.Ordinal);
		Assert.Contains("\"01_Nod\"", json, StringComparison.Ordinal);
		Assert.True(_config.Exists(PetInteractionConfig.StorageKey("nori")));
	}

	[Fact]
	public async Task ai_interaction开关按领域命令持久化()
	{
		BridgeCommands commands = CreateCommands();
		FakeBridgeSource source = new(WindowLabels.Main);

		await commands.InvokeAsync(source, "model_set_behavior", Args(new {aiInteraction = true}));

		Assert.True(_config.GetBoolOr(PetInteractionConfig.AiEnabledKey, false));
		object? snapshot = await commands.InvokeAsync(source, "ui_get_snapshot", Args(new { }));
		Assert.Contains("\"aiInteraction\":true", JsonSerializer.Serialize(snapshot, BridgeJson.Options), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ui_get_snapshot任意窗口可读且秘密不回传()
	{
		_config.Set("llm_api_key", new ConfigValue.Text("sk-super-secret"));
		BridgeCommands commands = CreateCommands();

		object? snapshot = await commands.InvokeAsync(new FakeBridgeSource("init"), "ui_get_snapshot", Args(new { }));
		string json = JsonSerializer.Serialize(snapshot);

		Assert.Contains("\"hasApiKey\":true", json, StringComparison.Ordinal);
		Assert.Contains("\"aiInteraction\":false", json, StringComparison.Ordinal);
		Assert.DoesNotContain("sk-super-secret", json, StringComparison.Ordinal);
	}

	// ---- 来源授权 ----

	[Fact]
	public async Task 业务命令拒绝非main窗口()
	{
		BridgeCommands commands = CreateCommands();
		string[] businessCommands = ["settings_update_voice", "chat_start", "memory_clear", "reminder_add", "tts_stop"];
		foreach (string cmd in businessCommands)
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				commands.InvokeAsync(new FakeBridgeSource("init"), cmd, Args(new { })));
		}
	}


	[Fact]
	public async Task 窗口命令只能操作自身且主界面召唤桌宠要求有效模型()
	{
		BridgeCommands commands = CreateCommands();
		await commands.InvokeAsync(new FakeBridgeSource(WindowLabels.FirstRun), "exit_app", Args(new { }));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Init), "window_show", Args(new {label = WindowLabels.Main})));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Main), "window_show", Args(new {label = WindowLabels.Pet})));

		InstallKnownModel("nori");
		_config.Set(ConfigStore.KeySelectedModel, new ConfigValue.Text("nori"));
		await commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Main), "window_show", Args(new {label = WindowLabels.Pet}));
		Assert.True(_windows.IsWindowVisible(WindowLabels.Pet));
	}

	[Fact]
	public async Task 首启与主界面都可更新AI设置但密钥只写不读()
	{
		BridgeCommands commands = CreateCommands();
		await commands.InvokeAsync(new FakeBridgeSource("first-run"), "settings_update_ai", Args(new
		{
			baseUrl = "https://api.example.com/v1",
			apiKey = "sk-new",
			model = "gpt-x",
		}));
		Assert.Equal("sk-new", _config.GetStringOr("llm_api_key", ""));

		// 显式空串清除密钥
		await commands.InvokeAsync(new FakeBridgeSource("main"), "settings_update_ai", Args(new {apiKey = ""}));
		Assert.False(_config.Exists("llm_api_key"));
	}

	[Fact]
	public async Task approval_respond未匹配请求返回false()
	{
		BridgeCommands commands = CreateCommands();
		object? result = await commands.InvokeAsync(
			new FakeBridgeSource("main"), "approval_respond", Args(new {requestId = "missing", approved = true}));
		Assert.Equal(false, result);
	}

	// ---- 历史规范化 ----

	[Fact]
	public async Task chat_history_page过滤反馈行并规范化旧协议JSON()
	{
		_services.Chat.SaveMessage("assistant", "```json\n{\"type\": \"message\", \"text\": \"旧版回复\"}\n```");
		_services.Chat.SaveMessage("user", "【系统工具执行反馈 - getTime】:\n{}");
		_services.Chat.SaveMessage("user", "你好");

		BridgeCommands commands = CreateCommands();
		var page = await commands.InvokeAsync(new FakeBridgeSource("main"), "chat_history_page", Args(new {limit = 10}));
		var rows = ((IEnumerable<object>)page!).ToList();

		Assert.Equal(2, rows.Count);
		JsonSerializerOptions relaxed = new() {Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
		string json = JsonSerializer.Serialize(rows, relaxed);
		Assert.Contains("旧版回复", json, StringComparison.Ordinal);
		Assert.DoesNotContain("系统工具执行反馈", json, StringComparison.Ordinal);
	}

	// ---- 提醒持久化 ----

	[Fact]
	public async Task reminder_add落库并可被新store恢复()
	{
		BridgeCommands commands = CreateCommands();
		object? added = await commands.InvokeAsync(
			new FakeBridgeSource("main"), "reminder_add", Args(new {content = "喝水", delayMinutes = 30}));
		Assert.NotNull(added);

		// 新的 store 实例从同一数据库读到该提醒 (重启恢复语义)
		Nori.Core.Proactive.ReminderStore store = new(_database);
		Assert.Single(store.List(), item => item.Content == "喝水");
	}

	[Fact]
	public async Task 到期提醒由TakeDue取走并删除()
	{
		Nori.Core.Proactive.ReminderStore store = new(_database);
		store.Add("过期提醒", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000);

		var due = store.TakeDue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		Assert.Single(due);
		Assert.Empty(store.TakeDue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
	}

	// ---- 工具手动测试边界 ----

	[Fact]
	public async Task tools_execute_manual非safe工具拒绝()
	{
		BridgeCommands commands = CreateCommands();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource("main"), "tools_execute_manual",
				Args(new {name = "setClipboardText", arguments = new {text = "x"}})));
	}

	// ---- 旧通用入口已移除 ----

	[Fact]
	public async Task 旧版通用config命令已下线()
	{
		BridgeCommands commands = CreateCommands();
		string[] legacyCommands = ["get_config", "set_config", "fetch_remote_text", "search_anysearch"];
		foreach (string cmd in legacyCommands)
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() =>
				commands.InvokeAsync(new FakeBridgeSource("main"), cmd, Args(new {key = "x"})));
		}
	}

	// ---- 初始化握手与窗口状态 ----

	private void InstallKnownModel(string modelId)
	{
		string directory = _services.Resources.ResourceDir(ResourceType.Live2D, modelId);
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, $"{modelId}.model3.json"),
			"{\"FileReferences\":{\"Moc\":\"model.moc3\",\"Textures\":[]}}");
		File.WriteAllText(Path.Combine(directory, "model.moc3"), "MOC3");
	}

	[Fact]
	public async Task model_select与显示参数拒绝未知未安装和越界输入()
	{
		BridgeCommands commands = CreateCommands();
		FakeBridgeSource main = new(WindowLabels.Main);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(main, "model_select", Args(new {modelId = "other"})));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(main, "model_select", Args(new {modelId = "nori"})));

		InstallKnownModel("nori");
		await commands.InvokeAsync(main, "model_select", Args(new {modelId = "nori"}));
		Assert.Equal("nori", _config.GetStringOr(ConfigStore.KeySelectedModel, ""));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(main, "model_set_display", Args(new {modelId = "nori", opacity = 2})));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(main, "model_set_display", Args(new {modelId = "nori", qualityMode = "unknown"})));
	}

	[Fact]
	public async Task complete_first_run要求可见首启窗口和已安装已知模型并原子提交()
	{
		InstallKnownModel("nori");
		BridgeCommands commands = CreateCommands();

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Main), "complete_first_run",
				Args(new {modelId = "nori", telemetryEnabled = false})));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.FirstRun, false), "complete_first_run",
				Args(new {modelId = "nori", telemetryEnabled = false})));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.FirstRun), "complete_first_run",
				Args(new {modelId = "other", telemetryEnabled = false})));
		Assert.True(_config.IsFirstRun());

		await commands.InvokeAsync(new FakeBridgeSource(WindowLabels.FirstRun), "complete_first_run",
			Args(new {modelId = "nori", telemetryEnabled = false}));

		Assert.False(_config.IsFirstRun());
		Assert.Equal("nori", _config.GetStringOr(ConfigStore.KeySelectedModel, ""));
		Assert.False(_config.GetBoolOr(ConfigStore.KeyTelemetryEnabled, true));
		Assert.NotNull(_config.GetInitConfig().InitializedAt);
		Assert.True(_windows.IsWindowVisible(WindowLabels.Init));
	}

	[Fact]
	public async Task init_enter_main只允许可见init并按有效模型和自动召唤切换窗口()
	{
		InstallKnownModel("arg-nori");
		_config.Set(ConfigStore.KeySelectedModel, new ConfigValue.Text("arg-nori"));
		_config.Set("pet_auto_summon", new ConfigValue.Boolean(true));
		BridgeCommands commands = CreateCommands();

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Main), "init_enter_main", Args(new { })));
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Init, false), "init_enter_main", Args(new { })));

		_windows.Show(WindowLabels.Init);
		await commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Init), "init_enter_main", Args(new { }));

		Assert.True(_windows.IsWindowVisible(WindowLabels.Main));
		Assert.True(_windows.IsWindowVisible(WindowLabels.Pet));
		Assert.False(_windows.IsWindowVisible(WindowLabels.Init));
	}

	[Fact]
	public async Task init_enter_main无效模型时不显示桌宠但仍进入主界面()
	{
		_config.Set(ConfigStore.KeySelectedModel, new ConfigValue.Text("other"));
		_windows.Show(WindowLabels.Init);
		_windows.Show(WindowLabels.Pet);
		BridgeCommands commands = CreateCommands();

		await commands.InvokeAsync(new FakeBridgeSource(WindowLabels.Init), "init_enter_main", Args(new { }));

		Assert.True(_windows.IsWindowVisible(WindowLabels.Main));
		Assert.False(_windows.IsWindowVisible(WindowLabels.Pet));
		Assert.False(_windows.IsWindowVisible(WindowLabels.Init));
	}

	[Fact]
	public async Task init_ready只允许init窗口且标志只能取一次()
	{
		BridgeCommands commands = CreateCommands();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource("main"), "init_ready", Args(new { })));

		// 尚未发生时为 false
		string before = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("init"), "init_ready", Args(new { })));
		Assert.Contains("\"initStartPending\":false", before, StringComparison.Ordinal);

		_runtime.MarkInitStartPending();
		string first = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("init"), "init_ready", Args(new { })));
		string second = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("init"), "init_ready", Args(new { })));

		Assert.Contains("\"initStartPending\":true", first, StringComparison.Ordinal);
		Assert.Contains("\"initStartPending\":false", second, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 快照包含桌宠可见性与侧边栏折叠态()
	{
		BridgeCommands commands = CreateCommands();

		string hidden = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("main"), "ui_get_snapshot", Args(new { })));
		using JsonDocument hiddenDocument = JsonDocument.Parse(hidden);
		Assert.False(hiddenDocument.RootElement.GetProperty("pet").GetProperty("visible").GetBoolean());
		Assert.False(hiddenDocument.RootElement.GetProperty("general").GetProperty("sidebarCollapsed").GetBoolean());

		_windows.Show(WindowLabels.Pet);
		await commands.InvokeAsync(new FakeBridgeSource("main"), "settings_update_general", Args(new {sidebarCollapsed = true}));

		string shown = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("main"), "ui_get_snapshot", Args(new { })));
		using JsonDocument shownDocument = JsonDocument.Parse(shown);
		Assert.True(shownDocument.RootElement.GetProperty("pet").GetProperty("visible").GetBoolean());
		Assert.True(shownDocument.RootElement.GetProperty("general").GetProperty("sidebarCollapsed").GetBoolean());
	}

	[Fact]
	public async Task 快照包含遥测状态且通用设置可即时关闭()
	{
		_config.SetTelemetryConsent(TelemetryConsent.Granted);
		BridgeCommands commands = CreateCommands();
		string before = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("main"), "ui_get_snapshot", Args(new { })));
		using JsonDocument beforeDocument = JsonDocument.Parse(before);
		JsonElement beforeTelemetry = beforeDocument.RootElement.GetProperty("telemetry");
		Assert.True(beforeTelemetry.GetProperty("enabled").GetBoolean());
		Assert.False(beforeTelemetry.GetProperty("available").GetBoolean());
		Assert.Equal("granted", beforeTelemetry.GetProperty("consent").GetString());

		await commands.InvokeAsync(new FakeBridgeSource("main"), "settings_update_general", Args(new {telemetryEnabled = false}));
		Assert.Equal(TelemetryConsent.Denied, _config.GetTelemetryConsent());
		string after = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("main"), "ui_get_snapshot", Args(new { })));
		using JsonDocument afterDocument = JsonDocument.Parse(after);
		JsonElement afterTelemetry = afterDocument.RootElement.GetProperty("telemetry");
		Assert.False(afterTelemetry.GetProperty("enabled").GetBoolean());
		Assert.False(afterTelemetry.GetProperty("available").GetBoolean());
		Assert.Equal("denied", afterTelemetry.GetProperty("consent").GetString());
	}

	[Fact]
	public void 同版本快照复用缓存且失效后重建()
	{
		FakeBridgeSource source = new(WindowLabels.Main);
		object first = _runtime.BuildSnapshot(source);
		object cached = _runtime.BuildSnapshot(source);
		Assert.Same(first, cached);

		_runtime.InvalidateSnapshot("test");
		object rebuilt = _runtime.BuildSnapshot(source);
		Assert.NotSame(first, rebuilt);
	}

	[Fact]
	public void 桌宠显隐变化作废快照()
	{
		// 广播本体走 Dispatcher.UIThread, 单测无 UI 循环, 因此只验证版本递增与快照投影
		int before = _runtime.SnapshotVersion;

		_windows.TogglePet();

		Assert.True(_runtime.SnapshotVersion > before);
		Assert.True(_windows.IsWindowVisible(WindowLabels.Pet));
	}
}
