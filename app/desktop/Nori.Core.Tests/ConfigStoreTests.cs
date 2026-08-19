using Nori.Core.Configuration;
using Nori.Core.Data;

namespace Nori.Core.Tests;

/// <summary>
/// 配置库读写与首次运行标记, 跑在临时 SQLite 文件上
/// </summary>
public class ConfigStoreTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"nori-test-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	public ConfigStoreTests()
	{
		_database = NoriDatabase.Open(_path);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_config.EnsureSchemaVersion();
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
	public void 默认配置在初始化后就位()
	{
		Assert.Equal("arg-nori", _config.GetStringOr(ConfigStore.KeySelectedModel, ""));
		Assert.Equal("0.1.0", _config.GetStringOr(ConfigStore.KeyAppVersion, ""));
		Assert.NotEqual("", _config.GetStringOr(ConfigStore.KeyInstalledAt, ""));
		Assert.True(_config.Exists(ConfigStore.KeyLanguage));
	}

	[Fact]
	public void 重复初始化不覆盖用户已有配置()
	{
		_config.Set(ConfigStore.KeySelectedModel, new ConfigValue.Text("nori"));
		_config.InitDefaults("0.2.0");
		Assert.Equal("nori", _config.GetStringOr(ConfigStore.KeySelectedModel, ""));
		Assert.Equal("0.1.0", _config.GetStringOr(ConfigStore.KeyAppVersion, ""));
	}

	[Fact]
	public void 首次运行标记流程()
	{
		Assert.True(_config.IsFirstRun());
		_config.MarkFirstRunCompleted();
		Assert.False(_config.IsFirstRun());

		Assert.Null(_config.GetInitConfig().InitializedAt);
		_config.MarkInitialized();
		string? first = _config.GetInitConfig().InitializedAt;
		Assert.NotNull(first);
		// 再次调用不应改写时间
		_config.MarkInitialized();
		Assert.Equal(first, _config.GetInitConfig().InitializedAt);
	}

	[Fact]
	public void 读写删除与存在性()
	{
		Assert.Null(_config.Get("l2d_scale_arg-nori"));
		_config.Set("l2d_scale_arg-nori", new ConfigValue.Text("1.25"));
		Assert.Equal("1.25", Assert.IsType<ConfigValue.Text>(_config.Get("l2d_scale_arg-nori")).Value);
		Assert.True(_config.Exists("l2d_scale_arg-nori"));
		Assert.True(_config.Delete("l2d_scale_arg-nori"));
		Assert.False(_config.Delete("l2d_scale_arg-nori"));
		Assert.Null(_config.Get("l2d_scale_arg-nori"));
	}

	[Fact]
	public void 覆盖写入不会插入重复行()
	{
		_config.Set("k", new ConfigValue.Text("a"));
		_config.Set("k", new ConfigValue.Text("b"));
		Assert.Equal("b", _config.GetStringOr("k", ""));
		Assert.Single(_config.GetAll(), pair => pair.Key == "k");
	}

	[Fact]
	public void 全部配置按键排序()
	{
		_config.Set("zzz", new ConfigValue.Text("1"));
		_config.Set("aaa", new ConfigValue.Text("2"));
		List<string> keys = [.. _config.GetAll().Select(pair => pair.Key)];
		Assert.Equal([.. keys.Order(StringComparer.Ordinal)], keys);
	}

	[Fact]
	public void 数据库版本高于程序时拒绝启动()
	{
		_config.Set(ConfigStore.KeyConfigSchemaVersion, new ConfigValue.Integer(ConfigStore.ConfigSchemaVersion + 1));
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(_config.EnsureSchemaVersion);
		Assert.Contains("请升级应用", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 动作组以JSON形态往返()
	{
		const string groups = """[{"group":"Idle","names":["01_Idle_Loop"]}]""";
		_config.Set("l2d_motions_arg-nori", new ConfigValue.Text(groups));
		// 存进去是字符串, 读出来会被推断成 JSON —— 与 Rust 版一致, chat.rs 的 motion_hint 依赖这一点
		ConfigValue.Json value = Assert.IsType<ConfigValue.Json>(_config.Get("l2d_motions_arg-nori"));
		Assert.Equal(groups, value.ToStorage());
	}
}
