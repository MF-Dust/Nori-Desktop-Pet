using Nori.Core.Data;
using Nori.Core.Memory;

namespace Nori.Core.Tests;

/// <summary>
/// 记忆存储库单元测试
/// </summary>
public class MemoryStoreTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"nori-memory-test-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly MemoryStore _memory;

	public MemoryStoreTests()
	{
		_database = NoriDatabase.Open(_path);
		_memory = new MemoryStore(_database);
	}

	public void Dispose()
	{
		_database.Dispose();
		try
		{
			File.Delete(_path);
		}
		catch (IOException)
		{
		}
		GC.SuppressFinalize(this);
	}

	[Fact]
	public void 添加与查询全部记忆()
	{
		MemoryItem item = _memory.Add("fact", "主人喜欢吃草莓蛋糕", 0.9, "chat", "food");

		Assert.True(item.Id > 0);
		Assert.Equal("fact", item.Type);
		Assert.Equal("主人喜欢吃草莓蛋糕", item.Content);
		Assert.Equal(0.9, item.Importance);
		Assert.Equal("food", item.Tags);

		IReadOnlyList<MemoryItem> all = _memory.GetAll();
		Assert.Single(all);
		Assert.Equal("主人喜欢吃草莓蛋糕", all[0].Content);
	}

	[Fact]
	public void 搜索记忆()
	{
		_memory.Add("fact", "主人养了一只猫叫咪咪", 0.8, "chat", "pet");
		_memory.Add("fact", "明天下午三点有会议", 0.7, "chat", "schedule");

		IReadOnlyList<MemoryItem> catResults = _memory.Search("猫");
		Assert.Single(catResults);
		Assert.Equal("主人养了一只猫叫咪咪", catResults[0].Content);

		IReadOnlyList<MemoryItem> scheduleResults = _memory.Search("会议");
		Assert.Single(scheduleResults);
		Assert.Equal("明天下午三点有会议", scheduleResults[0].Content);

		IReadOnlyList<MemoryItem> notFound = _memory.Search("不存在的内容");
		Assert.Empty(notFound);
	}

	[Fact]
	public void 更新记忆()
	{
		MemoryItem item = _memory.Add("fact", "主人住在北京", 0.5);

		bool updated = _memory.Update(item.Id, "主人住在上海", 0.8, "city");
		Assert.True(updated);

		IReadOnlyList<MemoryItem> all = _memory.GetAll();
		Assert.Single(all);
		Assert.Equal("主人住在上海", all[0].Content);
		Assert.Equal(0.8, all[0].Importance);
		Assert.Equal("city", all[0].Tags);
	}

	[Fact]
	public void 删除与清空记忆()
	{
		MemoryItem item1 = _memory.Add("fact", "记忆一", 0.5);
		MemoryItem item2 = _memory.Add("fact", "记忆二", 0.6);

		Assert.Equal(2, _memory.GetAll().Count);

		bool deleted = _memory.Delete(item1.Id);
		Assert.True(deleted);
		Assert.Single(_memory.GetAll());
		Assert.Equal(item2.Id, _memory.GetAll()[0].Id);

		_memory.Clear();
		Assert.Empty(_memory.GetAll());
	}

	[Fact]
	public void 向量余弦相似度计算()
	{
		float[] v1 = [1.0f, 0.0f, 0.0f];
		float[] v2 = [1.0f, 0.0f, 0.0f];
		float[] v3 = [0.0f, 1.0f, 0.0f];

		double simSame = MemoryStore.CosineSimilarity(v1, v2);
		double simOrthogonal = MemoryStore.CosineSimilarity(v1, v3);

		Assert.True(Math.Abs(simSame - 1.0) < 0.0001);
		Assert.True(Math.Abs(simOrthogonal - 0.0) < 0.0001);
	}

	[Fact]
	public void 向量语义检索与混合检索()
	{
		// 模拟 3 维向量
		_memory.Add("fact", "主人喜欢喝拿铁", 0.9, "chat", "drink", "[0.9, 0.1, 0.0]");
		_memory.Add("fact", "主人养了一只柯基", 0.8, "chat", "pet", "[0.0, 0.1, 0.9]");

		float[] queryDrink = [0.85f, 0.15f, 0.0f];

		IReadOnlyList<MemorySearchResult> results = _memory.SearchSemantic(queryDrink, 5);
		Assert.NotEmpty(results);
		Assert.Equal("主人喜欢喝拿铁", results[0].Item.Content);
		Assert.True(results[0].Similarity > 0.8);

		IReadOnlyList<MemoryItem> hybrid = _memory.SearchHybrid("咖啡", queryDrink, 5);
		Assert.NotEmpty(hybrid);
		Assert.Equal("主人喜欢喝拿铁", hybrid[0].Content);
	}
}

public class EmbeddingAdapterTests
{
	private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(handler(request));
		}
	}

	[Fact]
	public async Task OpenAiEmbeddingAdapter_解析BgeM3向量响应()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			Assert.Equal(HttpMethod.Post, req.Method);
			Assert.Equal("https://api.siliconflow.cn/v1/embeddings", req.RequestUri?.ToString());

			string json = """
				{
				  "data": [
				    {
				      "index": 0,
				      "embedding": [0.123, -0.456, 0.789]
				    }
				  ]
				}
				""";

			return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		Nori.Core.Embedding.OpenAiEmbeddingAdapter adapter = new(client);

		float[] vec = await adapter.GetEmbeddingAsync(
			"https://api.siliconflow.cn/v1",
			"sk-test",
			"BAAI/bge-m3",
			"测试输入文本");

		Assert.Equal(3, vec.Length);
		Assert.Equal(0.123f, vec[0]);
		Assert.Equal(-0.456f, vec[1]);
		Assert.Equal(0.789f, vec[2]);
	}

	[Fact]
	public async Task OpenAiEmbeddingAdapter_指定维数时请求体携带dimensions()
	{
		string? capturedBody = null;

		using MockHttpMessageHandler handler = new(req =>
		{
			capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

			string json = """
				{
				  "data": [
				    {"index": 0, "embedding": [0.1, 0.2]}
				  ]
				}
				""";

			return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
			};
		});

		using HttpClient client = new(handler);
		Nori.Core.Embedding.OpenAiEmbeddingAdapter adapter = new(client);

		await adapter.GetEmbeddingAsync(
			"https://api.openai.com/v1",
			"sk-test",
			"text-embedding-3-small",
			"测试输入文本",
			dimensions: 512);

		Assert.NotNull(capturedBody);
		Assert.Contains("\"dimensions\":512", capturedBody);

		// 不指定维数时请求体不应携带该字段, 避免不支持的端点报错
		await adapter.GetEmbeddingAsync(
			"https://api.openai.com/v1",
			"sk-test",
			"text-embedding-3-small",
			"测试输入文本");

		Assert.NotNull(capturedBody);
		Assert.DoesNotContain("dimensions", capturedBody);
	}
}
