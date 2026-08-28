using Microsoft.Data.Sqlite;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Security;

namespace Nori.Core.Tests;

public sealed class StorageBootstrapperTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "nori-storage-tests", Guid.NewGuid().ToString("N"));

	public StorageBootstrapperTests() => Directory.CreateDirectory(_root);

	[Fact]
	public void 首次迁移数据库WAL和固定文件到包内布局()
	{
		string legacy = Path.Combine(_root, "legacy");
		string package = Path.Combine(_root, "package");
		Directory.CreateDirectory(legacy);
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, "nori.db")))
		{
			database.Locked(connection =>
			{
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = "INSERT INTO config(key,value) VALUES ('migration_test','ok');";
				command.ExecuteNonQuery();
			});
		}
		Directory.CreateDirectory(Path.Combine(legacy, "knowledge"));
		File.WriteAllText(Path.Combine(legacy, "knowledge", "Memory.md"), "# test");
		File.WriteAllText(Path.Combine(legacy, "secret.key"), "key");

		AppStoragePaths paths = new(package);
		StorageBootstrapResult result = StorageBootstrapper.Bootstrap(paths, "v1.2.3-test+abcdef0", "win-x64", legacy);

		Assert.True(result.Migrated);
		Assert.True(File.Exists(paths.DatabasePath));
		Assert.Equal("# test", File.ReadAllText(paths.KnowledgePath));
		Assert.Equal("key", File.ReadAllText(paths.SecretPath));
		using NoriDatabase migrated = NoriDatabase.Open(paths: paths);
		Assert.Equal("ok", migrated.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT value FROM config WHERE key='migration_test';";
			return command.ExecuteScalar()?.ToString();
		}));
	}

	[Fact]
	public void 迁移后旧加密配置仍可解密()
	{
		string legacy = Path.Combine(_root, "legacy");
		string package = Path.Combine(_root, "package");
		Directory.CreateDirectory(legacy);
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, "nori.db")))
		{
			ConfigStore config = new(database, new SecretKeyStore(legacy));
			config.InitDefaults("old");
			config.Set("provider_api_key", new ConfigValue.Text("secret-value"));
		}

		AppStoragePaths paths = new(package);
		StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		using NoriDatabase migrated = NoriDatabase.Open(paths: paths);
		ConfigStore migratedConfig = new(migrated, new SecretKeyStore(paths));

		Assert.Equal("secret-value", migratedConfig.GetStringOr("provider_api_key", ""));
	}

	[Fact]
	public void 有效marker优先且不合并旧源()
	{
		string package = Path.Combine(_root, "package");
		AppStoragePaths paths = new(package);
		StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", Path.Combine(_root, "none"));
		string legacy = Path.Combine(_root, "legacy");
		Directory.CreateDirectory(legacy);
		File.WriteAllText(Path.Combine(legacy, "secret.key"), "must-not-copy");

		StorageBootstrapResult result = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);

		Assert.True(result.ExistingMarker);
		Assert.False(File.Exists(paths.SecretPath));
	}

	[Fact]
	public void 禁止迁移时不读取或删除旧源()
	{
		string legacy = Path.Combine(_root, "legacy-disabled");
		string package = Path.Combine(_root, "package-disabled");
		Directory.CreateDirectory(legacy);
		File.WriteAllText(Path.Combine(legacy, "secret.key"), "keep");
		AppStoragePaths paths = new(package);
		StorageBootstrapResult result = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy, allowLegacyMigration: false);
		Assert.False(result.Migrated);
		Assert.False(File.Exists(paths.SecretPath));
		Assert.True(File.Exists(Path.Combine(legacy, "secret.key")));
	}

	[Fact]
	public void 非空无marker拒绝启动()
	{
		string package = Path.Combine(_root, "package");
		AppStoragePaths paths = new(package);
		Directory.CreateDirectory(paths.DataRoot);
		File.WriteAllText(Path.Combine(paths.DataRoot, "unexpected"), "x");

		Assert.Throws<InvalidOperationException>(() => StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", Path.Combine(_root, "none")));
	}

	[Fact]
	public void 迁移清理后第二次启动允许无收据()
	{
		string legacy = Path.Combine(_root, "legacy-restart");
		string package = Path.Combine(_root, "package-restart");
		Directory.CreateDirectory(legacy);
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, AppPaths.DatabaseFileName))) { }
		AppStoragePaths paths = new(package);
		StorageBootstrapResult migration = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		StorageBootstrapper.CleanupLegacy(migration, paths, legacy);

		Assert.False(File.Exists(paths.CleanupReceiptPath));
		StorageBootstrapResult reopened = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		Assert.True(reopened.ExistingMarker);
		Assert.True(reopened.Migrated);
		Assert.False(Directory.Exists(legacy));
	}

	[Fact]
	public void 清理失败保留收据并允许显式来源重试()
	{
		string legacy = Path.Combine(_root, "legacy-retry");
		string package = Path.Combine(_root, "package-retry");
		Directory.CreateDirectory(legacy);
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, AppPaths.DatabaseFileName))) { }
		AppStoragePaths paths = new(package);
		StorageBootstrapResult migration = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		using (NoriDatabase locked = NoriDatabase.Open(Path.Combine(legacy, AppPaths.DatabaseFileName)))
		{
			StorageBootstrapper.CleanupLegacy(migration, paths, legacy);
			Assert.True(File.Exists(paths.CleanupReceiptPath));
		}
		StorageBootstrapper.CleanupLegacy(migration, paths, legacy);
		Assert.False(File.Exists(paths.CleanupReceiptPath));
		Assert.False(Directory.Exists(legacy));
	}

	[Fact]
	public void 收据来源伪造或调用方来源不匹配时拒绝清理()
	{
		string legacy = Path.Combine(_root, "legacy-receipt");
		string other = Path.Combine(_root, "other-receipt");
		string package = Path.Combine(_root, "package-receipt");
		Directory.CreateDirectory(legacy);
		File.WriteAllText(Path.Combine(legacy, "keep.txt"), "keep");
		AppStoragePaths paths = new(package);
		StorageBootstrapResult migration = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		string receipt = File.ReadAllText(paths.CleanupReceiptPath).Replace($"\"source\":\"{legacy.Replace("\\", "\\\\", StringComparison.Ordinal)}\"", $"\"source\":\"{other.Replace("\\", "\\\\", StringComparison.Ordinal)}\"", StringComparison.Ordinal);
		File.WriteAllText(paths.CleanupReceiptPath, receipt);

		Assert.Throws<InvalidOperationException>(() => StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy));
		Assert.True(Directory.Exists(legacy));
		Assert.NotNull(migration.MigrationId);
	}

	[Fact]
	public void 迁移备份只复制最新三个且单份超额拒绝()
	{
		string legacy = Path.Combine(_root, "legacy-backups");
		Directory.CreateDirectory(legacy);
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, AppPaths.DatabaseFileName))) { }
		for (int index = 0; index < 5; index++)
		{
			string backup = Path.Combine(legacy, $"nori.db-pre-migration-{index}.bak");
			File.WriteAllText(backup, index.ToString());
			File.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddMinutes(index));
		}
		AppStoragePaths paths = new(Path.Combine(_root, "package-backups"));
		StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		Assert.Equal(3, Directory.EnumerateFiles(paths.DatabaseDirectory, "nori.db-pre-migration-*.bak").Count());
		Assert.True(File.Exists(Path.Combine(paths.DatabaseDirectory, "nori.db-pre-migration-4.bak")));

		string oversized = Path.Combine(legacy, "nori.db-pre-migration-too-large.bak");
		using (FileStream stream = new(oversized, FileMode.CreateNew)) stream.SetLength(64L * 1024 * 1024 + 1);
		Assert.Throws<InvalidOperationException>(() => StorageBootstrapper.Bootstrap(new AppStoragePaths(Path.Combine(_root, "package-too-large")), "Dev", "win-x64", legacy));
	}

	[Fact]
	public async Task 迁移默认知识路径使用稳定标识并支持包根移动()
	{
		string legacy = Path.Combine(_root, "legacy-knowledge");
		string package = Path.Combine(_root, "package-knowledge");
		Directory.CreateDirectory(Path.Combine(legacy, "knowledge"));
		File.WriteAllText(Path.Combine(legacy, "knowledge", "Memory.md"), "# moved");
		using (NoriDatabase database = NoriDatabase.Open(Path.Combine(legacy, AppPaths.DatabaseFileName)))
		{
			ConfigStore config = new(database, new SecretKeyStore(legacy));
			config.Set("memory_knowledge_path", new ConfigValue.Text(Path.Combine(legacy, "knowledge", "Memory.md")));
		}
		AppStoragePaths paths = new(package);
		StorageBootstrapResult migration = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		using NoriDatabase migrated = NoriDatabase.Open(paths: paths);
		ConfigStore migratedConfig = new(migrated, new SecretKeyStore(paths));
		StorageBootstrapper.RelocateKnowledgeIdentifier(migrated, migratedConfig, Path.Combine(legacy, "knowledge", "Memory.md"), paths.KnowledgePath);
		Assert.Equal("nori://knowledge/Memory.md", migratedConfig.GetStringOr("memory_knowledge_path", ""));
		await using (Nori.Core.Memory.MemoryService memory = new(new Nori.Core.Memory.MemoryStore(migrated), new Nori.Core.Embedding.OpenAiEmbeddingAdapter(new HttpClient()), migratedConfig, false))
		await using (Nori.Core.Memory.KnowledgeService knowledge = new(migrated, memory, migratedConfig, paths.KnowledgePath))
			Assert.Equal(paths.KnowledgePath, knowledge.Path);
		Assert.Equal("# moved", File.ReadAllText(paths.KnowledgePath));
		Assert.True(migration.Migrated);
	}

	[Fact]
	public void 迁移保全未知持久文件并原子保留收据()
	{
		string legacy = Path.Combine(_root, "legacy");
		string package = Path.Combine(_root, "package");
		Directory.CreateDirectory(legacy);
		File.WriteAllText(Path.Combine(legacy, "user-export.json"), "keep");
		File.WriteAllText(Path.Combine(legacy, "cache.tmp"), "discard-or-classify");

		AppStoragePaths paths = new(package);
		StorageBootstrapResult result = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);

		Assert.True(result.Migrated);
		Assert.True(File.Exists(Path.Combine(paths.LegacyUnclassifiedDirectory, "user-export.json")));
		Assert.True(File.Exists(paths.CleanupReceiptPath));
		Assert.True(File.Exists(paths.MarkerPath));
		StorageBootstrapResult reopened = StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", legacy);
		Assert.True(reopened.Migrated);
	}

	public void Dispose()
	{
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
	}
}
