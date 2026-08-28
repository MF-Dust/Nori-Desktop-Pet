using Microsoft.Data.Sqlite;
using Nori.Core.Data;

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
	public void 非空无marker拒绝启动()
	{
		string package = Path.Combine(_root, "package");
		AppStoragePaths paths = new(package);
		Directory.CreateDirectory(paths.DataRoot);
		File.WriteAllText(Path.Combine(paths.DataRoot, "unexpected"), "x");

		Assert.Throws<InvalidOperationException>(() => StorageBootstrapper.Bootstrap(paths, "Dev", "win-x64", Path.Combine(_root, "none")));
	}

	public void Dispose()
	{
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
	}
}
