using System.Globalization;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;

namespace Nori.Core.Configuration;

/// <summary>
/// 配置读写
///
/// 对应 Rust 版 config.rs 的自由函数集合. 配置键一律 snake_case, 与前端常量保持一致.
/// </summary>
public sealed class ConfigStore(NoriDatabase database)
{
	/// <summary>配置键: 配置结构版本 (不兼容变更时 +1, 用于迁移)</summary>
	public const string KeyConfigSchemaVersion = "config_schema_version";

	/// <summary>配置键: 首次启动 (数据库创建) 时间</summary>
	public const string KeyInstalledAt = "installed_at";

	/// <summary>配置键: 首次初始化完成时间</summary>
	public const string KeyInitializedAt = "initialized_at";

	/// <summary>配置键: 应用版本 (首次安装时的版本)</summary>
	public const string KeyAppVersion = "app_version";

	/// <summary>配置键: 界面语言</summary>
	public const string KeyLanguage = "language";

	/// <summary>配置键: 桌宠模型</summary>
	public const string KeySelectedModel = "selected_model";

	/// <summary>配置键: 首次初始化是否已完成</summary>
	public const string KeyFirstRunCompleted = "first_run_completed";

	/// <summary>配置键: 桌宠窗口 X 坐标</summary>
	public const string KeyPetWindowX = "pet_window_x";

	/// <summary>配置键: 桌宠窗口 Y 坐标</summary>
	public const string KeyPetWindowY = "pet_window_y";

	/// <summary>配置键: 全局音频音量 (0.0 ~ 1.0)</summary>
	public const string KeyAudioVolume = "audio_volume";

	/// <summary>当前配置结构版本</summary>
	public const long ConfigSchemaVersion = 1;

	/// <summary>默认桌宠模型</summary>
	public const string DefaultModel = "arg-nori";

	private readonly NoriDatabase _database = database;

	/// <summary>
	/// 读取配置, 不存在返回 null
	/// </summary>
	public ConfigValue? Get(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT value FROM config WHERE key = $key";
		command.Parameters.AddWithValue("$key", key);
		return command.ExecuteScalar() is string stored ? ConfigValue.FromStorage(stored) : null;
	});

	/// <summary>
	/// 写入配置 (存在则覆盖)
	/// </summary>
	public void Set(string key, ConfigValue value) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			INSERT INTO config (key, value)
			VALUES ($key, $value)
			ON CONFLICT(key)
			DO UPDATE SET value = excluded.value
			""";
		command.Parameters.AddWithValue("$key", key);
		command.Parameters.AddWithValue("$value", value.ToStorage());
		command.ExecuteNonQuery();
	});

	/// <summary>
	/// 删除配置, 返回是否真的删除了记录
	/// </summary>
	public bool Delete(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM config WHERE key = $key";
		command.Parameters.AddWithValue("$key", key);
		return command.ExecuteNonQuery() > 0;
	});

	/// <summary>
	/// 判断配置是否存在
	/// </summary>
	public bool Exists(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM config WHERE key = $key";
		command.Parameters.AddWithValue("$key", key);
		return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
	});

	/// <summary>
	/// 获取所有配置 (按键排序)
	/// </summary>
	public IReadOnlyList<KeyValuePair<string, ConfigValue>> GetAll() => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT key, value FROM config ORDER BY key";
		using SqliteDataReader reader = command.ExecuteReader();
		List<KeyValuePair<string, ConfigValue>> result = [];
		while (reader.Read()) result.Add(new KeyValuePair<string, ConfigValue>(reader.GetString(0), ConfigValue.FromStorage(reader.GetString(1))));
		return (IReadOnlyList<KeyValuePair<string, ConfigValue>>)result;
	});

	/// <summary>
	/// 读取字符串配置, 缺失/类型不符时返回 fallback
	/// </summary>
	public string GetStringOr(string key, string fallback) => ConfigValue.AsStringOr(Get(key), fallback);

	/// <summary>
	/// 初始化默认配置: 只补缺失项, 不覆盖用户已有配置
	/// </summary>
	public void InitDefaults(string appVersion) => _database.Locked(connection =>
	{
		(string Key, ConfigValue Value)[] defaults =
		[
			(KeyConfigSchemaVersion, new ConfigValue.Integer(ConfigSchemaVersion)),
			(KeyAppVersion, new ConfigValue.Text(appVersion)),
			(KeyInstalledAt, new ConfigValue.Text(Now())),
			(KeyLanguage, new ConfigValue.Text(SystemLanguage())),
			(KeySelectedModel, new ConfigValue.Text(DefaultModel)),
			(KeyFirstRunCompleted, new ConfigValue.Boolean(false)),
		];
		foreach ((string key, ConfigValue value) in defaults)
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "INSERT OR IGNORE INTO config (key, value) VALUES ($key, $value)";
			command.Parameters.AddWithValue("$key", key);
			command.Parameters.AddWithValue("$value", value.ToStorage());
			command.ExecuteNonQuery();
		}
	});

	/// <summary>
	/// 检查配置结构版本
	///
	/// 低于当前版本则逐级迁移; 高于当前版本直接报错, 防止旧程序改坏新数据库.
	/// </summary>
	public void EnsureSchemaVersion()
	{
		long current = ReadSchemaVersion();
		if (current > ConfigSchemaVersion)
		{
			throw new InvalidOperationException($"配置数据库版本 {current} 高于当前应用支持版本 {ConfigSchemaVersion}, 请升级应用");
		}
		if (current < ConfigSchemaVersion) MigrateSchema(current, ConfigSchemaVersion);
	}

	/// <summary>
	/// 数据库结构迁移: 未来的 v1 → v2 等在这里逐级处理
	/// </summary>
	private void MigrateSchema(long from, long to)
	{
		long version = from;
		while (version < to)
		{
			switch (version)
			{
				case 1:
					// 当前没有 v2, 暂时不执行实际迁移
					break;
				default:
					throw new InvalidOperationException($"不支持的配置数据库版本: {version}");
			}
			version++;
		}
		Set(KeyConfigSchemaVersion, new ConfigValue.Integer(to));
	}

	/// <summary>
	/// 读取配置结构版本, 缺失或类型异常时按当前版本处理
	/// </summary>
	private long ReadSchemaVersion() => Get(KeyConfigSchemaVersion) switch
	{
		ConfigValue.Integer integer => integer.Value,
		ConfigValue.Text text when long.TryParse(text.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
		_ => ConfigSchemaVersion,
	};

	/// <summary>
	/// 判断是否首次启动
	/// </summary>
	public bool IsFirstRun() => Get(KeyFirstRunCompleted) switch
	{
		null => true,
		ConfigValue.Boolean boolean => !boolean.Value,
		ConfigValue.Integer integer => integer.Value == 0,
		ConfigValue.Text text => text.Value == "0" || text.Value.Equals("false", StringComparison.OrdinalIgnoreCase),
		_ => false,
	};

	/// <summary>
	/// 标记首次启动完成
	/// </summary>
	public void MarkFirstRunCompleted() => Set(KeyFirstRunCompleted, new ConfigValue.Boolean(true));

	/// <summary>
	/// 记录首次初始化完成时间 (只写一次)
	/// </summary>
	public void MarkInitialized()
	{
		if (!Exists(KeyInitializedAt)) Set(KeyInitializedAt, new ConfigValue.Text(Now()));
	}

	/// <summary>
	/// 首次初始化配置快照, 对应前端 invoke("get_init_config")
	/// </summary>
	public InitConfig GetInitConfig()
	{
		string? initializedAt = Get(KeyInitializedAt) is ConfigValue.Text text && text.Value.Length > 0 ? text.Value : null;
		return new InitConfig
		{
			ConfigSchemaVersion = ReadSchemaVersion(),
			AppVersion = GetStringOr(KeyAppVersion, "unknown"),
			InstalledAt = GetStringOr(KeyInstalledAt, ""),
			InitializedAt = initializedAt,
			Language = GetStringOr(KeyLanguage, SystemLanguage()),
			SelectedModel = GetStringOr(KeySelectedModel, DefaultModel),
		};
	}

	/// <summary>
	/// 当前本地时间, 形如 2026-01-01 12:00:00
	/// </summary>
	private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary>
	/// 系统语言, 获取失败时回退 zh-CN
	/// </summary>
	public static string SystemLanguage()
	{
		string name = CultureInfo.CurrentUICulture.Name;
		return string.IsNullOrEmpty(name) ? "zh-CN" : name;
	}
}
