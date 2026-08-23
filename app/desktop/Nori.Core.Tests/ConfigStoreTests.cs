using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Security;

namespace Nori.Core.Tests;

/// <summary>
/// 配置库读写与首次运行标记, 跑在临时 SQLite 文件上
/// </summary>
public class ConfigStoreTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"nori-test-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	/// <summary>测试用主密钥: 固定值, 不碰用户真实数据目录</summary>
	private sealed class FixedKeyStore : ISecretKeyStore
	{
		private readonly byte[] _key = Enumerable.Range(0, SecretKeyStore.KeySize).Select(index => (byte)index).ToArray();
		public byte[] LoadOrCreate() => _key;
		public bool IsFileFallback => true;
	}

	public ConfigStoreTests()
	{
		_database = NoriDatabase.Open(_path);
		_config = new ConfigStore(_database, new FixedKeyStore());
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
	public void v1迁移会回填并删除旧语言键()
	{
		string path = Path.Combine(Path.GetTempPath(), $"nori-language-migration-{Guid.NewGuid():N}.db");
		try
		{
			using NoriDatabase database = NoriDatabase.Open(path);
			database.Locked(connection =>
			{
				using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
				command.CommandText = "INSERT INTO config (key, value) VALUES ('config_schema_version', '1'), ('app_language', 'en-US')";
				command.ExecuteNonQuery();
			});

			ConfigStore config = new(database);
			config.EnsureSchemaVersion();

			Assert.Equal("en-US", config.GetStringOr(ConfigStore.KeyLanguage, ""));
			Assert.False(config.Exists(ConfigStore.LegacyKeyLanguage));
			Assert.Equal(ConfigStore.ConfigSchemaVersion, Assert.IsType<ConfigValue.Integer>(config.Get(ConfigStore.KeyConfigSchemaVersion)).Value);
		}
		finally
		{
			TryDeleteDatabase(path);
		}
	}

	[Fact]
	public void v1迁移保留已存在的规范语言键()
	{
		string path = Path.Combine(Path.GetTempPath(), $"nori-language-precedence-{Guid.NewGuid():N}.db");
		try
		{
			using NoriDatabase database = NoriDatabase.Open(path);
			database.Locked(connection =>
			{
				using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
				command.CommandText = "INSERT INTO config (key, value) VALUES ('config_schema_version', '1'), ('language', 'zh-CN'), ('app_language', 'en-US')";
				command.ExecuteNonQuery();
			});

			ConfigStore config = new(database);
			config.EnsureSchemaVersion();

			Assert.Equal("zh-CN", config.GetStringOr(ConfigStore.KeyLanguage, ""));
			Assert.False(config.Exists(ConfigStore.LegacyKeyLanguage));
		}
		finally
		{
			TryDeleteDatabase(path);
		}
	}

	private static void TryDeleteDatabase(string path)
	{
		try
		{
			File.Delete(path);
			File.Delete($"{path}-wal");
			File.Delete($"{path}-shm");
		}
		catch (IOException)
		{
		}
	}

	[Fact]
	public void 敏感APIKey自动加解密透明存取()
	{
		const string plainKey = "sk-test-secret-key-123456789";
		_config.Set("llm_api_key", new ConfigValue.Text(plainKey));

		// 读取时透明解密
		ConfigValue? value = _config.Get("llm_api_key");
		Assert.NotNull(value);
		Assert.Equal(plainKey, value.ToStorage());

		// 底层 SQLite 里必须是新格式密文, 且不含明文 (三平台一致)
		using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}");
		connection.Open();
		using var cmd = connection.CreateCommand();
		cmd.CommandText = "SELECT value FROM config WHERE key = 'llm_api_key'";
		string rawInDb = (string)cmd.ExecuteScalar()!;
		Assert.StartsWith(SecretProtector.Prefix, rawInDb, StringComparison.Ordinal);
		Assert.DoesNotContain(plainKey, rawInDb, StringComparison.Ordinal);
	}

	[Fact]
	public void 换了主密钥的密文读不出明文而不是崩掉()
	{
		const string plainKey = "sk-rotate-me";
		_config.Set("llm_api_key", new ConfigValue.Text(plainKey));
		string cipher = _config.RawValue("llm_api_key");

		// 另一把密钥的 store 读同一条记录: 拿不到明文, 但也不能抛
		ConfigStore other = new(_database, new WrongKeyStore());
		string readBack = other.GetStringOr("llm_api_key", "");
		Assert.NotEqual(plainKey, readBack);
		Assert.Equal(cipher, readBack);
		Assert.True(ConfigStore.IsUnreadableSecret(readBack));
	}

	[Fact]
	public void 非敏感键不加密()
	{
		_config.Set("llm_model", new ConfigValue.Text("gpt-x"));
		Assert.Equal("gpt-x", _config.RawValue("llm_model"));
	}

	private sealed class WrongKeyStore : ISecretKeyStore
	{
		private readonly byte[] _key = Enumerable.Repeat((byte)0xAB, SecretKeyStore.KeySize).ToArray();
		public byte[] LoadOrCreate() => _key;
		public bool IsFileFallback => true;
	}
}
