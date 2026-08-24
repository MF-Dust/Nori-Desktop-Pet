using Nori.Core.Agent;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Emotion;
using Nori.Core.Memory;
using Nori.Core.Skills;
using Nori.Core.Tools;

namespace Nori.Core.Tests;

/// <summary>使用确定性适配器验证 Agent 主回路与安全 Trace，不访问网络。</summary>
public sealed class AgentEvaluationTests : IDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"nori-agent-eval-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	public AgentEvaluationTests()
	{
		_database = NoriDatabase.Open(_databasePath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("1.0.3");
		_config.Set("llm_provider", new ConfigValue.Text("openai"));
		_config.Set("llm_api_base", new ConfigValue.Text("https://example.test/v1"));
		_config.Set("llm_api_key", new ConfigValue.Text("test-key"));
		_config.Set("llm_model", new ConfigValue.Text("deterministic-model"));
		_config.Set("memory_enabled", new ConfigValue.Boolean(false));
	}

	[Fact]
	public async Task 确定性适配器完成主回路并产生无正文Trace()
	{
		using HttpClient http = new();
		ChatService chat = new(http, _database, _config);
		await using MemoryService memory = new(new MemoryStore(_database), new EmbeddingStub(), _config);
		SkillService skills = new(_config, http);
		using EmotionManager emotion = new(_config);
		emotion.Initialize();
		ToolRegistry tools = new();
		AgentTraceCollector trace = new(32);
		const string response = "{\"type\":\"message\",\"text\":\"确定性回复\"}";
		DeterministicAdapter adapter = new(response);

		AgentEngine engine = new(
			http,
			_config,
			chat,
			tools,
			skills,
			emotion,
			memory,
			pet: null,
			motionNames: static () => [],
			expressionNames: static () => [],
			trace: trace,
			adapterFactory: (_, _) => adapter);

		ProtocolMessage result = await engine.RunAsync(
			"测试消息",
			"eval-session",
			new AgentCallbacks(),
			CancellationToken.None);

		Assert.Equal("确定性回复", result.Text);
		Assert.Equal(1, adapter.StreamCalls);
		Assert.Contains(trace.Snapshot(), item => item.Phase == "llm" && item.Status == "completed");
		Assert.Contains(trace.Snapshot(), item => item.Phase == "run" && item.Status == "completed");
		Assert.All(trace.Snapshot(), item =>
		{
			Assert.DoesNotContain("测试消息", System.Text.Json.JsonSerializer.Serialize(item), StringComparison.Ordinal);
			Assert.DoesNotContain("确定性回复", System.Text.Json.JsonSerializer.Serialize(item), StringComparison.Ordinal);
		});
	}

	public void Dispose()
	{
		_database.Dispose();
		try { File.Delete(_databasePath); } catch (IOException) { }
		GC.SuppressFinalize(this);
	}

	private sealed class DeterministicAdapter(string response) : ILlmAdapter
	{
		public int StreamCalls { get; private set; }

		public Task<string> CompleteAsync(
			string baseUrl,
			string apiKey,
			string model,
			string systemPrompt,
			IReadOnlyList<ChatMessageInput> messages,
			CancellationToken cancellationToken = default) => Task.FromResult(response);

		public Task<string> StreamAsync(
			string baseUrl,
			string apiKey,
			string model,
			string systemPrompt,
			IReadOnlyList<ChatMessageInput> messages,
			Action<string> onChunk,
			Action<LlmUsageInfo>? onUsage = null,
			CancellationToken cancellationToken = default)
		{
			StreamCalls++;
			onChunk(response);
			onUsage?.Invoke(new LlmUsageInfo
			{
				PromptTokens = 12,
				CompletionTokens = 4,
				TotalTokens = 16,
				CachedTokens = 2,
				DurationMs = 3,
				Model = model,
			});
			return Task.FromResult(response);
		}

		public Task<IReadOnlyList<string>> FetchModelsAsync(
			string baseUrl,
			string apiKey,
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<string>>(["deterministic-model"]);
	}

	private sealed class EmbeddingStub : Nori.Core.Embedding.IEmbeddingAdapter
	{
		public Task<float[]> GetEmbeddingAsync(string baseUrl, string apiKey, string model, string input, int? dimensions = null, CancellationToken cancellationToken = default) =>
			Task.FromResult<float[]>([1f, 0f]);

		public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(string baseUrl, string apiKey, string model, IReadOnlyList<string> inputs, int? dimensions = null, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<float[]>>([new[] {1f, 0f}]);
	}
}
