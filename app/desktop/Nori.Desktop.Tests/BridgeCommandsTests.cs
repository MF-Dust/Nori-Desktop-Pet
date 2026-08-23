using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Mcp;
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
	private sealed class FakeBridgeSource(string label) : IBridgeSource
	{
		public string Label => label;
		public bool IsVisible => true;
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
	public async Task ui_get_snapshot任意窗口可读且秘密不回传()
	{
		_config.Set("llm_api_key", new ConfigValue.Text("sk-super-secret"));
		BridgeCommands commands = CreateCommands();

		object? snapshot = await commands.InvokeAsync(new FakeBridgeSource("init"), "ui_get_snapshot", Args(new { }));
		string json = JsonSerializer.Serialize(snapshot);

		Assert.Contains("\"hasApiKey\":true", json, StringComparison.Ordinal);
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
	public async Task first_run_select_model只允许首启窗口且写入配置()
	{
		BridgeCommands commands = CreateCommands();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource("main"), "first_run_select_model", Args(new {modelId = "nori"})));

		await commands.InvokeAsync(new FakeBridgeSource("first-run"), "first_run_select_model", Args(new {modelId = "nori"}));
		Assert.Equal("nori", _config.GetStringOr(ConfigStore.KeySelectedModel, ""));
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
		Assert.Contains("\"pet\":{\"visible\":false}", hidden, StringComparison.Ordinal);
		Assert.Contains("\"sidebarCollapsed\":false", hidden, StringComparison.Ordinal);

		_windows.Show(WindowLabels.Pet);
		await commands.InvokeAsync(new FakeBridgeSource("main"), "settings_update_general", Args(new {sidebarCollapsed = true}));

		string shown = JsonSerializer.Serialize(
			await commands.InvokeAsync(new FakeBridgeSource("main"), "ui_get_snapshot", Args(new { })));
		Assert.Contains("\"pet\":{\"visible\":true}", shown, StringComparison.Ordinal);
		Assert.Contains("\"sidebarCollapsed\":true", shown, StringComparison.Ordinal);
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
