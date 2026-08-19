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
}
