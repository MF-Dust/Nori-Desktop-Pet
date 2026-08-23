using System.Globalization;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;
using Nori.Core.Security;

namespace Nori.Core.Configuration;

/// <summary>
/// 配置读写
///
/// 对应 Rust 版 config.rs 的自由函数集合. 配置键一律 snake_case, 与前端常量保持一致.
///
/// 敏感配置 (API Key 等) 用 AES-256-GCM 加密后落库, 主密钥交给各平台密钥库保管
/// (Windows DPAPI / macOS Keychain / Linux libsecret, 见 SecretKeyStore)。
/// 旧的 `enc:dpapi:` 值读到后按需解密并惰性升级成新格式; 非 Windows 上读到
/// 旧值只能视为不可用, 由 UI 引导用户重填该项 —— 绝不静默清空其他配置。
/// </summary>
public sealed class ConfigStore(NoriDatabase database, ISecretKeyStore? keyStore = null)
{
	private readonly ISecretKeyStore _keyStore = keyStore ?? new SecretKeyStore();

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

	/// <summary>配置键: 旧版界面语言 (仅用于 v1 → v2 迁移)</summary>
	public const string LegacyKeyLanguage = "app_language";

	/// <summary>配置键: 桌宠模型</summary>
	public const string KeySelectedModel = "selected_model";

	/// <summary>配置键: 首次初始化是否已完成</summary>
	public const string KeyFirstRunCompleted = "first_run_completed";

	/// <summary>配置键: 是否允许发送脱敏错误遥测</summary>
	public const string KeyTelemetryEnabled = "telemetry_enabled";

	/// <summary>配置键: 桌宠窗口 X 坐标</summary>
	public const string KeyPetWindowX = "pet_window_x";

	/// <summary>配置键: 桌宠窗口 Y 坐标</summary>
	public const string KeyPetWindowY = "pet_window_y";

	/// <summary>配置键: 全局音频音量 (0.0 ~ 1.0)</summary>
	public const string KeyAudioVolume = "audio_volume";

	/// <summary>当前配置结构版本</summary>
	public const long ConfigSchemaVersion = 2;

	/// <summary>没有版本记录的旧数据库所使用的最后旧版本</summary>
	private const long LegacyConfigSchemaVersion = 1;

	/// <summary>默认桌宠模型</summary>
	public const string DefaultModel = "arg-nori";

	private readonly NoriDatabase _database = database;

	/// <summary>
	/// 读取配置, 不存在返回 null (自动解密 DPAPI 保护的敏感字段)
	/// </summary>
	public ConfigValue? Get(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT value FROM config WHERE key = $key";
		command.Parameters.AddWithValue("$key", key);
		if (command.ExecuteScalar() is not string stored) return null;
		string decrypted = UnprotectValue(stored);
		return ConfigValue.FromStorage(decrypted);
	});

	/// <summary>
	/// 写入配置 (存在则覆盖, 敏感字段自动进行 DPAPI 加密)
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
		string toStore = ProtectValue(key, value.ToStorage());
		command.Parameters.AddWithValue("$value", toStore);
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
		while (reader.Read())
		{
			string key = reader.GetString(0);
			string decrypted = UnprotectValue(reader.GetString(1));
			result.Add(new KeyValuePair<string, ConfigValue>(key, ConfigValue.FromStorage(decrypted)));
		}
		return (IReadOnlyList<KeyValuePair<string, ConfigValue>>)result;
	});

	/// <summary>
	/// 判断是否为敏感配置项 (需要加密存储)
	/// </summary>
	private static bool IsSensitiveKey(string key) =>
		key.EndsWith("_api_key", StringComparison.OrdinalIgnoreCase) ||
		key.EndsWith("_secret", StringComparison.OrdinalIgnoreCase) ||
		key.EndsWith("_token", StringComparison.OrdinalIgnoreCase) ||
		key.EndsWith("_password", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 加密敏感数据 (AES-256-GCM, 主密钥来自平台密钥库)
	/// </summary>
	private string ProtectValue(string key, string plainText)
	{
		if (!IsSensitiveKey(key) || string.IsNullOrEmpty(plainText)) return plainText;
		try
		{
			return SecretProtector.Protect(_keyStore.LoadOrCreate(), plainText);
		}
		catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
		{
			// 密钥库不可用时宁可存明文也不能让用户的密钥丢失, 但要留下痕迹
			return plainText;
		}
	}

	/// <summary>
	/// 解密敏感数据; 无法解密时原样返回 (调用方按“需要重填”处理)
	/// </summary>
	private string UnprotectValue(string stored)
	{
		if (SecretProtector.IsProtected(stored))
		{
			return SecretProtector.TryUnprotect(_keyStore.LoadOrCreate(), stored, out string plain) ? plain : stored;
		}
		if (SecretProtector.IsLegacyDpapi(stored)) return UnprotectLegacyDpapi(stored);
		return stored;
	}

	/// <summary>
	/// 解密旧的 DPAPI 值 (仅 Windows 可解)
	///
	/// 非 Windows 上无法解密: 原样返回, 由 IsUnreadableSecret 判定并提示用户重填。
	/// </summary>
	private static string UnprotectLegacyDpapi(string stored)
	{
		if (!OperatingSystem.IsWindows()) return stored;
		try
		{
			byte[] encrypted = Convert.FromBase64String(stored[SecretProtector.LegacyDpapiPrefix.Length..]);
			byte[] decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
				encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
			return System.Text.Encoding.UTF8.GetString(decrypted);
		}
		catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException)
		{
			return stored;
		}
	}

	/// <summary>
	/// 判断某个敏感值是否已经无法解密 (需要用户重新填写)
	///
	/// 典型场景: 用户把 nori.db 从 Windows 搬到 Linux, 旧的 DPAPI 值只能重填。
	/// </summary>
	public static bool IsUnreadableSecret(string stored) =>
		SecretProtector.IsProtected(stored) || SecretProtector.IsLegacyDpapi(stored);

	/// <summary>
	/// 把仍是旧格式的敏感值惰性升级成新格式 (只在能解密时进行)
	/// </summary>
	public void UpgradeSecretFormat(string key)
	{
		string stored = RawValue(key);
		if (stored.Length == 0 || !SecretProtector.IsLegacyDpapi(stored)) return;
		string plain = UnprotectLegacyDpapi(stored);
		if (plain == stored) return; // 解不开: 留着让 UI 提示重填
		Set(key, new ConfigValue.Text(plain));
	}

	/// <summary>读取未经解密的原始存储值 (迁移与诊断用)</summary>
	public string RawValue(string key) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT value FROM config WHERE key = $key";
		command.Parameters.AddWithValue("$key", key);
		return command.ExecuteScalar() as string ?? "";
	});

	/// <summary>
	/// 读取字符串配置, 缺失/类型不符时返回 fallback
	/// </summary>
	public string GetStringOr(string key, string fallback) => ConfigValue.AsStringOr(Get(key), fallback);

	/// <summary>
	/// 读取布尔配置, 兼容历史上可能写入的 0/1 与 true/false 字符串。
	/// </summary>
	public bool GetBoolOr(string key, bool fallback)
	{
		ConfigValue? value = Get(key);
		if (value is ConfigValue.Boolean boolean) return boolean.Value;
		string raw = ConfigValue.AsStringOr(value, "");
		return raw switch
		{
			"1" => true,
			"0" => false,
			_ when bool.TryParse(raw, out bool parsed) => parsed,
			_ => fallback,
		};
	}

	/// <summary>
	/// 初始化默认配置: 只补缺失项, 不覆盖用户已有配置.
	/// 先完成版本迁移, 再插入 language 默认值, 这样旧 app_language 不会被系统语言默认值抢先覆盖.
	/// </summary>
	public void InitDefaults(string appVersion)
	{
		EnsureSchemaVersion();
		_database.Locked(connection =>
		{
			(string Key, ConfigValue Value)[] defaults =
			[
				(KeyConfigSchemaVersion, new ConfigValue.Integer(ConfigSchemaVersion)),
				(KeyAppVersion, new ConfigValue.Text(appVersion)),
				(KeyInstalledAt, new ConfigValue.Text(Now())),
				(KeyLanguage, new ConfigValue.Text(SystemLanguage())),
				(KeySelectedModel, new ConfigValue.Text(DefaultModel)),
				(KeyFirstRunCompleted, new ConfigValue.Boolean(false)),
				(KeyTelemetryEnabled, new ConfigValue.Boolean(true)),
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
	}

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
	/// 数据库结构迁移: v1 → v2 规范化 language 并清理旧键.
	/// 迁移与版本号写入在同一个 SQLite 事务中完成.
	/// </summary>
	private void MigrateSchema(long from, long to) => _database.Locked(connection =>
	{
		using SqliteTransaction transaction = connection.BeginTransaction();
		try
		{
			long version = from;
			while (version < to)
			{
				switch (version)
				{
					case LegacyConfigSchemaVersion:
						MigrateLanguage(connection, transaction);
						break;
					default:
						throw new InvalidOperationException($"不支持的配置数据库版本: {version}");
				}
				version++;
				SetSchemaVersion(connection, transaction, version);
			}
			transaction.Commit();
		}
		catch
		{
			try
			{
				transaction.Rollback();
			}
			catch
			{
				// 保留原始迁移异常.
			}
			throw;
		}
	});

	/// <summary>规范化语言键: language 已存在时优先保留, 否则回填 app_language.</summary>
	private static void MigrateLanguage(SqliteConnection connection, SqliteTransaction transaction)
	{
		using SqliteCommand copy = connection.CreateCommand();
		copy.Transaction = transaction;
		copy.CommandText = """
			INSERT INTO config (key, value)
			SELECT $language, legacy.value
			FROM config AS legacy
			WHERE legacy.key = $legacy
				AND NOT EXISTS (SELECT 1 FROM config WHERE key = $language);
			""";
		copy.Parameters.AddWithValue("$language", KeyLanguage);
		copy.Parameters.AddWithValue("$legacy", LegacyKeyLanguage);
		copy.ExecuteNonQuery();

		using SqliteCommand delete = connection.CreateCommand();
		delete.Transaction = transaction;
		delete.CommandText = "DELETE FROM config WHERE key = $legacy";
		delete.Parameters.AddWithValue("$legacy", LegacyKeyLanguage);
		delete.ExecuteNonQuery();
	}

	/// <summary>在迁移事务中写入配置版本.</summary>
	private static void SetSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, long version)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = """
			INSERT INTO config (key, value) VALUES ($key, $value)
			ON CONFLICT(key) DO UPDATE SET value = excluded.value;
			""";
		command.Parameters.AddWithValue("$key", KeyConfigSchemaVersion);
		command.Parameters.AddWithValue("$value", version.ToString(CultureInfo.InvariantCulture));
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// 读取配置结构版本. 没有版本记录的数据库按 v1 处理, 以便执行遗留键迁移.
	/// </summary>
	private long ReadSchemaVersion() => Get(KeyConfigSchemaVersion) switch
	{
		null => LegacyConfigSchemaVersion,
		ConfigValue.Integer integer => integer.Value,
		ConfigValue.Boolean boolean => boolean.Value ? 1 : 0,
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
