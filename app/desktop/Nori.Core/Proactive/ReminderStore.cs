using System.Globalization;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;

namespace Nori.Core.Proactive;

/// <summary>
/// 提醒事项记录
/// </summary>
public sealed record ReminderItem
{
	/// <summary>提醒唯一 ID</summary>
	public required string Id { get; init; }

	/// <summary>提醒内容</summary>
	public required string Content { get; init; }

	/// <summary>触发时间 (Unix 毫秒)</summary>
	public required long TriggerAt { get; init; }

	/// <summary>是否每日重复 (暂未启用, 预留字段)</summary>
	public bool RepeatDaily { get; init; }

	/// <summary>创建时间</summary>
	public required string CreatedAt { get; init; }
}

/// <summary>
/// 定时提醒存储层 (SQLite, 可恢复)
///
/// v3 迁移新增 reminders 表: 应用重启后未触发的提醒自动恢复调度。
/// </summary>
public sealed class ReminderStore(NoriDatabase database)
{
	/// <summary>添加提醒</summary>
	public ReminderItem Add(string content, long triggerAt)
	{
		string id = $"reminder-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.NextInt64(0x10000, 0xFFFFF):x}";
		string now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO reminders (id, content, trigger_at, repeat_daily, created_at)
				VALUES ($id, $content, $trigger_at, 0, $created_at)
				""";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$content", content);
			command.Parameters.AddWithValue("$trigger_at", triggerAt);
			command.Parameters.AddWithValue("$created_at", now);
			command.ExecuteNonQuery();
		});
		return new ReminderItem {Id = id, Content = content, TriggerAt = triggerAt, CreatedAt = now};
	}

	/// <summary>列出全部提醒 (按触发时间正序)</summary>
	public IReadOnlyList<ReminderItem> List()
	{
		return database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT id, content, trigger_at, repeat_daily, created_at FROM reminders ORDER BY trigger_at ASC";
			using SqliteDataReader reader = command.ExecuteReader();
			List<ReminderItem> items = [];
			while (reader.Read())
			{
				items.Add(new ReminderItem
				{
					Id = reader.GetString(0),
					Content = reader.GetString(1),
					TriggerAt = reader.GetInt64(2),
					RepeatDaily = reader.GetInt64(3) != 0,
					CreatedAt = reader.GetString(4),
				});
			}
			return items;
		});
	}

	/// <summary>取走所有已到期 (trigger_at <= now) 的提醒并从库中删除</summary>
	public IReadOnlyList<ReminderItem> TakeDue(long now) => database.Locked(connection =>
	{
		List<ReminderItem> due = [];
		using (SqliteTransaction transaction = connection.BeginTransaction())
		{
			try
			{
				using (SqliteCommand select = connection.CreateCommand())
				{
					select.Transaction = transaction;
					select.CommandText = "SELECT id, content, trigger_at, repeat_daily, created_at FROM reminders WHERE trigger_at <= $now ORDER BY trigger_at ASC";
					select.Parameters.AddWithValue("$now", now);
					using SqliteDataReader reader = select.ExecuteReader();
					while (reader.Read())
					{
						due.Add(new ReminderItem
						{
							Id = reader.GetString(0),
							Content = reader.GetString(1),
							TriggerAt = reader.GetInt64(2),
							RepeatDaily = reader.GetInt64(3) != 0,
							CreatedAt = reader.GetString(4),
						});
					}
				}

				if (due.Count > 0)
				{
					using SqliteCommand delete = connection.CreateCommand();
					delete.Transaction = transaction;
					delete.CommandText = "DELETE FROM reminders WHERE trigger_at <= $now";
					delete.Parameters.AddWithValue("$now", now);
					delete.ExecuteNonQuery();
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
					// 保留原始异常
				}
				throw;
			}
		}
		return due;
	});

	/// <summary>删除单条提醒</summary>
	public bool Delete(string id) => database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM reminders WHERE id = $id";
		command.Parameters.AddWithValue("$id", id);
		return command.ExecuteNonQuery() > 0;
	});

	/// <summary>清空全部提醒</summary>
	public void Clear()
	{
		database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "DELETE FROM reminders";
			command.ExecuteNonQuery();
		});
	}
}
