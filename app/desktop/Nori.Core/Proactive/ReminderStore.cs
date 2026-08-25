using System.Globalization;
using Microsoft.Data.Sqlite;
using Nori.Core.Data;

namespace Nori.Core.Proactive;

/// <summary>提醒事项记录</summary>
public sealed record ReminderItem
{
	/// <summary>提醒唯一 ID</summary>
	public required string Id { get; init; }
	/// <summary>提醒内容</summary>
	public required string Content { get; init; }
	/// <summary>触发时间 (Unix 毫秒)</summary>
	public required long TriggerAt { get; init; }
	/// <summary>是否每日重复 (兼容旧字段)</summary>
	public bool RepeatDaily { get; init; }
	/// <summary>创建时间</summary>
	public required string CreatedAt { get; init; }
	/// <summary>领取状态: pending、claimed 或 fired</summary>
	public string Status { get; init; } = "pending";
	/// <summary>重复规则使用的时区</summary>
	public string Timezone { get; init; } = "UTC";
	/// <summary>JSON 格式的重复规则</summary>
	public string? RecurrenceJson { get; init; }
	/// <summary>推迟到期时间 (Unix 毫秒)</summary>
	public long? SnoozedUntil { get; init; }
	/// <summary>最近一次领取时间</summary>
	public string? ClaimedAt { get; init; }
	/// <summary>最近一次成功投递时间</summary>
	public string? FiredAt { get; init; }
	/// <summary>最后更新时间</summary>
	public string UpdatedAt { get; init; } = "";
}

/// <summary>
/// 定时提醒存储层 (SQLite, 可恢复)
///
/// v6 迁移新增 reminders 的领取状态。TakeDue 只领取而不删除，成功投递后由
/// MarkFired 确认；进程崩溃留下的 claimed 记录在领取租约过期后可再次领取。
/// </summary>
public sealed class ReminderStore(NoriDatabase database)
{
	/// <summary>领取租约时长。进程崩溃后超过此时长的领取会自动重试。</summary>
	public static readonly TimeSpan ClaimLeaseDuration = TimeSpan.FromMinutes(5);

	/// <summary>添加提醒</summary>
	public ReminderItem Add(string content, long triggerAt)
	{
		string id = $"reminder-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.NextInt64(0x10000, 0xFFFFF):x}";
		string now = UtcNowText();
		database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO reminders
					(id, content, trigger_at, repeat_daily, created_at, status, timezone, recurrence_json, updated_at)
				VALUES ($id, $content, $trigger_at, 0, $created_at, 'pending', 'UTC', NULL, $updated_at)
				""";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$content", content);
			command.Parameters.AddWithValue("$trigger_at", triggerAt);
			command.Parameters.AddWithValue("$created_at", now);
			command.Parameters.AddWithValue("$updated_at", now);
			command.ExecuteNonQuery();
		});
		return new ReminderItem {Id = id, Content = content, TriggerAt = triggerAt, CreatedAt = now, UpdatedAt = now};
	}

	/// <summary>列出尚未成功投递或取消的提醒 (按触发时间正序)</summary>
	public IReadOnlyList<ReminderItem> List() => database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = """
			SELECT id, content, trigger_at, repeat_daily, created_at, status, timezone,
			       recurrence_json, snoozed_until, claimed_at, fired_at, updated_at
			FROM reminders
			WHERE status <> 'fired'
			ORDER BY trigger_at ASC
			""";
		using SqliteDataReader reader = command.ExecuteReader();
		List<ReminderItem> items = [];
		while (reader.Read()) items.Add(ReadItem(reader));
		return items;
	});

	/// <summary>
	/// 原子领取所有已到期提醒。领取不会删除记录；调用方完成投递后必须调用 MarkFired，
	/// 否则租约过期后会重试。
	/// </summary>
	public IReadOnlyList<ReminderItem> TakeDue(long now) => database.Locked(connection =>
	{
		List<ReminderItem> due = [];
		string claimedAt = UtcNowText();
		string claimBefore = DateTimeOffset.FromUnixTimeMilliseconds(now).Subtract(ClaimLeaseDuration).ToString("o", CultureInfo.InvariantCulture);
		using SqliteTransaction transaction = connection.BeginTransaction();
		try
		{
			using SqliteCommand claim = connection.CreateCommand();
			claim.Transaction = transaction;
			claim.CommandText = """
				UPDATE reminders
				SET status = 'claimed', claimed_at = $claimed_at, updated_at = $claimed_at
				WHERE id IN (
					SELECT id FROM reminders
					WHERE trigger_at <= $now
					  AND (snoozed_until IS NULL OR snoozed_until <= $now)
					  AND (status = 'pending' OR (status = 'claimed' AND (claimed_at IS NULL OR claimed_at < $claim_before)))
					ORDER BY trigger_at ASC
				)
				RETURNING id, content, trigger_at, repeat_daily, created_at, status, timezone,
				          recurrence_json, snoozed_until, claimed_at, fired_at, updated_at
				""";
			claim.Parameters.AddWithValue("$now", now);
			claim.Parameters.AddWithValue("$claimed_at", claimedAt);
			claim.Parameters.AddWithValue("$claim_before", claimBefore);
			using SqliteDataReader reader = claim.ExecuteReader();
			while (reader.Read()) due.Add(ReadItem(reader));
			transaction.Commit();
		}
		catch
		{
			try { transaction.Rollback(); }
			catch { /* 保留原始异常。 */ }
			throw;
		}
		return due;
	});

	/// <summary>确认一条提醒已经成功投递；每日重复提醒会重新排队到下一天。</summary>
	public bool MarkFired(string id)
	{
		string now = UtcNowText();
		return database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				UPDATE reminders
				SET status = CASE WHEN repeat_daily <> 0 THEN 'pending' ELSE 'fired' END,
				    trigger_at = CASE WHEN repeat_daily <> 0 THEN trigger_at + 86400000 ELSE trigger_at END,
				    claimed_at = NULL, fired_at = $fired_at, updated_at = $fired_at
				WHERE id = $id AND status = 'claimed'
				""";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$fired_at", now);
			return command.ExecuteNonQuery() > 0;
		});
	}

	/// <summary>投递失败时立即释放领取；未释放的崩溃领取由租约策略自动重试。</summary>
	public bool ReleaseClaim(string id)
	{
		string now = UtcNowText();
		return database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "UPDATE reminders SET status = 'pending', claimed_at = NULL, updated_at = $updated_at WHERE id = $id AND status = 'claimed'";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$updated_at", now);
			return command.ExecuteNonQuery() > 0;
		});
	}

	/// <summary>删除单条提醒</summary>
	public bool Delete(string id) => database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM reminders WHERE id = $id";
		command.Parameters.AddWithValue("$id", id);
		return command.ExecuteNonQuery() > 0;
	});

	/// <summary>原子领取一个主动问候 occurrence，重启后仍只会触发一次。</summary>
	public bool TryClaimOccurrence(string key, string occurrence)
	{
		string now = UtcNowText();
		return database.Locked(connection =>
		{
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO proactive_occurrences(key, occurrence, updated_at)
				VALUES ($key, $occurrence, $updated)
				ON CONFLICT(key) DO UPDATE SET occurrence = excluded.occurrence, updated_at = excluded.updated_at
				WHERE proactive_occurrences.occurrence <> excluded.occurrence
				""";
			command.Parameters.AddWithValue("$key", key);
			command.Parameters.AddWithValue("$occurrence", occurrence);
			command.Parameters.AddWithValue("$updated", now);
			return command.ExecuteNonQuery() > 0;
		});
	}

	/// <summary>清空全部提醒</summary>
	public void Clear() => database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM reminders";
		command.ExecuteNonQuery();
	});

	private static string UtcNowText() => DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

	private static ReminderItem ReadItem(SqliteDataReader reader) => new()
	{
		Id = reader.GetString(0), Content = reader.GetString(1), TriggerAt = reader.GetInt64(2),
		RepeatDaily = reader.GetInt64(3) != 0, CreatedAt = reader.GetString(4), Status = reader.GetString(5),
		Timezone = reader.GetString(6), RecurrenceJson = reader.IsDBNull(7) ? null : reader.GetString(7),
		SnoozedUntil = reader.IsDBNull(8) ? null : reader.GetInt64(8), ClaimedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
		FiredAt = reader.IsDBNull(10) ? null : reader.GetString(10), UpdatedAt = reader.GetString(11),
	};
}
