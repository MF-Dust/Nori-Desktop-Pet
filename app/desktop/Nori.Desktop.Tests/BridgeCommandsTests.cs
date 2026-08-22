using System.Text.Json;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Embedding;
using Nori.Core.Logging;
using Nori.Core.Mcp;
using Nori.Desktop.Bridge;
using Nori.Desktop.Windows;
using Avalonia.Controls;

namespace Nori.Desktop.Tests;

/// <summary>
/// 桥接命令分发集成测试: 使用 fake 窗口边界与可控 HTTP 直接驱动 BridgeCommands
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

	public Window? Get(string? label) => null;
		public NoriWindow? GetNoriWindow(string? label) => null;
		public PetWindow? Pet => null;

		public void CreateAll(NoriBridge bridge, AppServices services)
		{
		}
		public void Show(string label)
		{
		}

		public void Hide(string label)
		{
		}

		public void Close(string label)
		{
		}

		public void TogglePet()
		{
		}

		public void Broadcast(string name, object? payload) => Broadcasts.Add((name, payload));

		public void Shutdown()
		{
		}
	}

	private sealed class MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
		: HttpMessageHandler
	{
		public HttpRequestMessage? LastRequest { get; private set; }
		public int CallCount { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			CallCount++;
			LastRequest = request;
			return await handler(request, cancellationToken);
		}
	}

	private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"nori-bridge-{Guid.NewGuid():N}");
	private readonly string _dbPath;
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;
	private readonly MockHttpHandler _httpHandler;
	private readonly HttpClient _http;
	private readonly FakeWindowManager _windows = new();
	private readonly AppServices _services;

	public BridgeCommandsTests()
	{
		Directory.CreateDirectory(_tempDir);
		_dbPath = Path.Combine(_tempDir, "nori.db");
		_database = NoriDatabase.Open(_dbPath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_httpHandler = new MockHttpHandler((_, _) =>
			Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
			}));
		_http = new HttpClient(_httpHandler);
		_services = BuildServices(_http);
	}

	/// <summary>按给定 HttpClient 装配一套服务容器</summary>
	private AppServices BuildServices(HttpClient httpClient) => new()
	{
		Database = _database,
		Config = _config,
		Logger = new FileLogger(Path.Combine(_tempDir, "logs")),
		Resources = new Nori.Core.Resources.ResourceManager(_tempDir),
		Chat = new ChatService(httpClient, _database, _config),
		Memory = new Nori.Core.Memory.MemoryStore(_database),
		Embedding = new OpenAiEmbeddingAdapter(httpClient),
		Llm = new LlmClient(httpClient),
		Mcp = new McpManager(httpClient, _config),
		Http = httpClient,
		AgentOperations = new AgentOperationRegistry(),
		Windows = _windows,
	};

	public void Dispose()
	{
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

	[Fact]
	public async Task set_config写入并广播()
	{
		BridgeCommands commands = CreateCommands();
		await commands.InvokeAsync(new FakeBridgeSource("main"), "set_config", Args(new {key = "l2d_opacity", value = "0.5"}));

		Assert.Equal("0.5", _config.GetStringOr("l2d_opacity", ""));
		(string Name, object? Payload)? broadcast = _windows.Broadcasts.Find(item => item.Name == "nori:config-changed");
		Assert.NotNull(broadcast);
		Assert.Contains("l2d_opacity", JsonSerializer.Serialize(broadcast.Value.Payload), StringComparison.Ordinal);
	}

	[Fact]
	public async Task delete_config对称广播且未删除时不广播()
	{
		BridgeCommands commands = CreateCommands();
		_config.Set("l2d_shadow", new ConfigValue.Boolean(true));
		FakeBridgeSource source = new("main");

		object? deleted = await commands.InvokeAsync(source, "delete_config", Args(new {key = "l2d_shadow"}));
		Assert.Equal(true, deleted);
		Assert.False(_config.Exists("l2d_shadow"));
		object? deletedPayload = _windows.Broadcasts.LastOrDefault(item => item.Name == "nori:config-changed").Payload;
		Assert.Contains("\"deleted\":true", JsonSerializer.Serialize(deletedPayload), StringComparison.Ordinal);

		int before = _windows.Broadcasts.Count;
		object? missing = await commands.InvokeAsync(source, "delete_config", Args(new {key = "l2d_shadow"}));
		Assert.Equal(false, missing);
		Assert.Equal(before, _windows.Broadcasts.Count);
	}

	[Fact]
	public async Task import_local_resource带filePath时直接导入()
	{
		string zipPath = Path.Combine(_tempDir, "pack.zip");
		using (FileStream stream = File.Create(zipPath))
		using (System.IO.Compression.ZipArchive archive = new(stream, System.IO.Compression.ZipArchiveMode.Create))
		{
			using StreamWriter writer = new(archive.CreateEntry("ARGNori_web/ARGNori.model3.json").Open());
			writer.Write("{}");
		}

		BridgeCommands commands = CreateCommands();
		object? result = await commands.InvokeAsync(
			new FakeBridgeSource("main"),
			"import_local_resource",
			Args(new {filePath = zipPath, resourceType = "live2d"}));

		Assert.NotNull(result);
		Assert.True(_services.Resources.IsInstalled(Nori.Core.Resources.ResourceType.Live2D, "arg-nori"));
	}

	[Fact]
	public async Task search_anysearch官方端点携带存储密钥()
	{
		_config.Set("anysearch_api_key", new ConfigValue.Text("sk-stored"));
		BridgeCommands commands = CreateCommands();

		await commands.InvokeAsync(new FakeBridgeSource("main"), "search_anysearch", Args(new {query = "天气"}));

		Assert.Equal(1, _httpHandler.CallCount);
		Assert.Equal("https://api.anysearch.com/v1/search", _httpHandler.LastRequest!.RequestUri!.ToString());
		Assert.Equal("Bearer sk-stored",
			string.Join(' ', _httpHandler.LastRequest.Headers.GetValues("Authorization")));
	}

	[Fact]
	public async Task search_anysearch自定义端点缺少密钥时拒绝且不外发存储密钥()
	{
		_config.Set("anysearch_api_key", new ConfigValue.Text("sk-stored"));
		BridgeCommands commands = CreateCommands();

		await Assert.ThrowsAsync<InvalidOperationException>(() => commands.InvokeAsync(
			new FakeBridgeSource("main"),
			"search_anysearch",
			Args(new {query = "天气", endpoint = "https://relay.example.com/v1/search"})));

		Assert.Equal(0, _httpHandler.CallCount);
	}

	[Fact]
	public async Task search_anysearch自定义端点使用显式密钥()
	{
		_config.Set("anysearch_api_key", new ConfigValue.Text("sk-stored"));
		BridgeCommands commands = CreateCommands();

		await commands.InvokeAsync(new FakeBridgeSource("main"), "search_anysearch", Args(new
		{
			query = "天气",
			endpoint = "https://relay.example.com/v1/search",
			apiKey = "sk-mine",
		}));

		Assert.Equal("https://relay.example.com/v1/search", _httpHandler.LastRequest!.RequestUri!.ToString());
		Assert.Equal("Bearer sk-mine", string.Join(' ', _httpHandler.LastRequest.Headers.GetValues("Authorization")));
	}

	[Fact]
	public async Task 未知命令拒绝()
	{
		BridgeCommands commands = CreateCommands();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource("main"), "definitely_not_a_command", Args(new { })));
	}

	[Fact]
	public async Task 非首次运行窗口调用complete_first_run被拒绝()
	{
		BridgeCommands commands = CreateCommands();
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			commands.InvokeAsync(new FakeBridgeSource("main"), "complete_first_run", Args(new { })));
	}

	[Fact]
	public async Task cancel_agent_session只取消同来源的操作()
	{
		BridgeCommands commands = CreateCommands();
		FakeBridgeSource main = new("main");
		using CancellationTokenSource registered = _services.AgentOperations.Register("main", "session-1", CancellationToken.None);

		object? sameSource = await commands.InvokeAsync(main, "cancel_agent_session", Args(new {sessionId = "session-1"}));
		Assert.Equal(true, sameSource);
		Assert.True(registered.Token.IsCancellationRequested);

		using CancellationTokenSource other = _services.AgentOperations.Register("init", "session-2", CancellationToken.None);
		object? crossSource = await commands.InvokeAsync(main, "cancel_agent_session", Args(new {sessionId = "session-2"}));
		Assert.Equal(false, crossSource);
		Assert.False(other.Token.IsCancellationRequested);
	}

	[Fact]
	public async Task 聊天流取消后解除注册表登记()
	{
		MockHttpHandler hangingHandler = new(async (_, token) =>
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			token.Register(() => gate.TrySetResult());
			await gate.Task;
			throw new OperationCanceledException(token);
		});
		using HttpClient client = new(hangingHandler);
		AppServices hanging = BuildServices(client);

		BridgeCommands commands = new(hanging, action => action());
		FakeBridgeSource source = new("main");
		JsonElement args = Args(new
		{
			baseUrl = "https://api.example.com/v1",
			apiKey = "k",
			model = "m",
			messages = new[] {new {role = "user", content = "hi"}},
			sessionId = "session-cancel",
			persist = false,
		});

		Task invokeTask = commands.InvokeAsync(source, "chat_completion_stream", args);

		// 等待宿主 HTTP 请求已挂起, 再通过同来源窗口取消该 session
		DateTime deadline = DateTime.UtcNow.AddSeconds(5);
		while (hangingHandler.CallCount == 0 && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
		Assert.Equal(1, hangingHandler.CallCount);
		object? cancelled = await commands.InvokeAsync(source, "cancel_agent_session", Args(new {sessionId = "session-cancel"}));
		Assert.Equal(true, cancelled);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invokeTask);

		// 取消路径完成后 CTS 必须已解除登记
		Assert.False(hanging.AgentOperations.TryCancel("main", "session-cancel"));
	}
}
