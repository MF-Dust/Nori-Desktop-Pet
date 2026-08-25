using Microsoft.Data.Sqlite;
using Nori.Core.Data;
using Nori.Core.Proactive;

namespace Nori.Core.Tests;

public sealed class ReminderStoreTests
{
	[Fact]
	public void 逐级提醒迁移保留旧列并回填领取字段()
	{
		string path = NewPath();
		try
		{
			using (SqliteConnection connection = new($"Data Source={path}"))
			{
				connection.Open();
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = """
					CREATE TABLE reminders (id TEXT PRIMARY KEY, content TEXT NOT NULL, trigger_at INTEGER NOT NULL, repeat_daily INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL);
					INSERT INTO reminders (id, content, trigger_at, repeat_daily, created_at) VALUES ('legacy', '每日提醒', 123, 1, '2026-01-01T00:00:00Z');
					PRAGMA user_version = 3;
					""";
				command.ExecuteNonQuery();
			}
			using NoriDatabase database = NoriDatabase.Open(path);
			(string repeat, string status, string timezone, string? recurrence, string updated) row = database.Locked(connection =>
			{
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT repeat_daily, status, timezone, recurrence_json, updated_at FROM reminders WHERE id = 'legacy'";
				using SqliteDataReader reader = command.ExecuteReader();
				Assert.True(reader.Read());
				return (reader.GetInt64(0).ToString(), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4));
			});
			Assert.Equal(6, database.Locked(ReadVersion));
			Assert.Equal("1", row.repeat);
			Assert.Equal("pending", row.status);
			Assert.Equal("UTC", row.timezone);
			Assert.Equal("{\"type\":\"daily\"}", row.recurrence);
			Assert.Equal("2026-01-01T00:00:00Z", row.updated);
		}
		finally { DeleteDatabase(path); }
	}

	[Fact]
	public async Task 并发领取同一提醒只返回一份()
	{
		string path = NewPath();
		try
		{
			using NoriDatabase firstDatabase = NoriDatabase.Open(path);
			ReminderStore first = new(firstDatabase);
			first.Add("只投递一次", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);
			using NoriDatabase secondDatabase = NoriDatabase.Open(path);
			ReminderStore second = new(secondDatabase);
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			IReadOnlyList<ReminderItem>[] results = await Task.WhenAll(
				Task.Run(() => first.TakeDue(now)), Task.Run(() => second.TakeDue(now)));
			Assert.Single(results.SelectMany(items => items));
			Assert.Contains(results, items => items.Count == 0);
			Assert.Equal("claimed", Assert.Single(results.SelectMany(items => items)).Status);
		}
		finally { DeleteDatabase(path); }
	}

	[Fact]
	public void 领取失败释放后可重试并确认后结束()
	{
		string path = NewPath();
		try
		{
			using NoriDatabase database = NoriDatabase.Open(path);
			ReminderStore store = new(database);
			ReminderItem added = store.Add("可恢复提醒", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			Assert.Single(store.TakeDue(now));
			Assert.Empty(store.TakeDue(now));
			Assert.True(store.ReleaseClaim(added.Id));
			Assert.Equal(added.Id, Assert.Single(store.TakeDue(now)).Id);
			Assert.True(store.MarkFired(added.Id));
			Assert.Empty(store.TakeDue(now));
			Assert.Empty(store.List());
		}
		finally { DeleteDatabase(path); }
	}

	[Fact]
	public void 过期领取租约可以重试()
	{
		string path = NewPath();
		try
		{
			using NoriDatabase database = NoriDatabase.Open(path);
			ReminderStore store = new(database);
			store.Add("崩溃后重试", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			Assert.Single(store.TakeDue(now));
			database.Locked(connection =>
			{
				using SqliteCommand command = connection.CreateCommand();
				command.CommandText = "UPDATE reminders SET claimed_at = '2000-01-01T00:00:00Z'";
				command.ExecuteNonQuery();
			});
			Assert.Single(store.TakeDue(now));
		}
		finally { DeleteDatabase(path); }
	}

	private static long ReadVersion(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA user_version";
		return Convert.ToInt64(command.ExecuteScalar());
	}

	private static string NewPath() => Path.Combine(Path.GetTempPath(), $"nori-reminder-{Guid.NewGuid():N}.db");

	private static void DeleteDatabase(string path)
	{
		try
		{
			File.Delete(path); File.Delete($"{path}-wal"); File.Delete($"{path}-shm");
			foreach (string backup in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.pre-migration-*.bak")) File.Delete(backup);
		}
		catch (IOException) { }
	}
}
