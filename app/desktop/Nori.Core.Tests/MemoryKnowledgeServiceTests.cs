using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Embedding;
using Nori.Core.Memory;

namespace Nori.Core.Tests;

public sealed class MemoryKnowledgeServiceTests : IAsyncDisposable
{
	private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"nori-knowledge-{Guid.NewGuid():N}.db");
	private readonly string _knowledgePath = Path.Combine(Path.GetTempPath(), $"nori-knowledge-{Guid.NewGuid():N}.md");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;
	private readonly KnowledgeService _knowledge;

	public MemoryKnowledgeServiceTests()
	{
		_database = NoriDatabase.Open(_databasePath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("test");
		_config.Set("memory_knowledge_path", new ConfigValue.Text(_knowledgePath));
		MemoryService memory = new(new MemoryStore(_database), new StubEmbedding(), _config);
		_knowledge = new KnowledgeService(_database, memory, _config);
	}

	[Fact]
	public async Task MemoryMd按认知Gate检索并支持增量重建()
	{
		await File.WriteAllTextAsync(_knowledgePath, """
			# 当前

			## 自我
			[NORI_KNOWS]
			Nori 知道自己的名字。

			## 残响
			[NORI_ECHO]
			研究员这个词带来熟悉感。

			## 未知
			[NORI_UNKNOWN]
			某段尚未恢复的背景。
			""");
		MemoryIndexStatus status = await _knowledge.ReindexAsync();
		Assert.Equal(3, status.Total);
		Assert.NotEmpty(_knowledge.Search("名字"));
		Assert.Empty(_knowledge.Search("背景"));
		Assert.NotEmpty(_knowledge.Search("分析未知背景"));
		Assert.NotEmpty(_knowledge.SearchEchoes("研究员"));

		MemoryIndexStatus unchanged = await _knowledge.ReindexAsync();
		Assert.Equal(3, unchanged.Processed);
	}

	public async ValueTask DisposeAsync()
	{
		await _knowledge.DisposeAsync();
		_database.Dispose();
		try { File.Delete(_databasePath); File.Delete($"{_databasePath}-wal"); File.Delete($"{_databasePath}-shm"); File.Delete(_knowledgePath); }
		catch (IOException) { }
	}

	private sealed class StubEmbedding : IEmbeddingAdapter
	{
		public Task<float[]> GetEmbeddingAsync(string baseUrl, string apiKey, string model, string input, int? dimensions = null, CancellationToken cancellationToken = default) => Task.FromResult<float[]>([1, 0]);
		public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(string baseUrl, string apiKey, string model, IReadOnlyList<string> inputs, int? dimensions = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<float[]>>(inputs.Select(_ => new float[] {1, 0}).ToList());
	}
}
