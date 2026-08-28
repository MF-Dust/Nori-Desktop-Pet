using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Nori.Core.Configuration;

namespace Nori.Core.Data;

/// <summary>两阶段建立包内 data，并将旧 Tauri 数据安全迁移到新布局。</summary>
public static class StorageBootstrapper
{
	private const int MarkerSchemaVersion = 1;
	private const long MaxCopiedFileBytes = 64L * 1024 * 1024;
	private static readonly string[] KnownLegacyPaths = [
		AppPaths.DatabaseFileName, AppPaths.DatabaseFileName + "-wal", AppPaths.DatabaseFileName + "-shm",
		"secret.key", "knowledge", "resources", "plugins", "plugin-data", "log",
	];

	/// <summary>数据库打开后把精确旧默认知识路径改为稳定逻辑 ID，避免包移动造成重复索引。</summary>
	public static void RelocateKnowledgeIdentifier(NoriDatabase database, ConfigStore config, string oldDefaultPath, string newPath)
	{
		ArgumentNullException.ThrowIfNull(database);
		ArgumentNullException.ThrowIfNull(config);
		string oldPath = Path.GetFullPath(oldDefaultPath);
		if (!string.Equals(config.GetStringOr("memory_knowledge_path", "").Trim(), oldPath, PathComparison)) return;
		config.Set("memory_knowledge_path", new ConfigValue.Text(newPath));
		database.Locked(connection =>
		{
			using SqliteTransaction transaction = connection.BeginTransaction();
			using SqliteCommand find = connection.CreateCommand();
			find.Transaction = transaction;
			find.CommandText = "SELECT id FROM knowledge_documents WHERE path = $old";
			find.Parameters.AddWithValue("$old", oldPath);
			object? oldId = find.ExecuteScalar();
			if (oldId is not null && oldId != DBNull.Value)
			{
				using SqliteCommand stable = connection.CreateCommand();
				stable.Transaction = transaction;
				stable.CommandText = "SELECT id FROM knowledge_documents WHERE path = $stable";
				stable.Parameters.AddWithValue("$stable", "nori://knowledge/Memory.md");
				object? stableId = stable.ExecuteScalar();
				using SqliteCommand update = connection.CreateCommand();
				update.Transaction = transaction;
				update.Parameters.AddWithValue("$id", oldId);
				update.CommandText = stableId is null
					? "UPDATE knowledge_documents SET path = 'nori://knowledge/Memory.md' WHERE id = $id"
					: "DELETE FROM knowledge_chunks WHERE document_id = $id; DELETE FROM knowledge_documents WHERE id = $id";
				update.ExecuteNonQuery();
			}
			transaction.Commit();
		});
	}

	/// <summary>在新数据库打开前执行，目标有效 marker 优先；失败只清理 staging。</summary>
	public static StorageBootstrapResult Bootstrap(AppStoragePaths paths, string productVersion, string rid, string? legacyDataPath = null, bool allowLegacyMigration = true)
	{
		ArgumentNullException.ThrowIfNull(paths);
		ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
		ArgumentException.ThrowIfNullOrWhiteSpace(rid);
		ValidateExistingTarget(paths);
		if (Directory.Exists(paths.DataRoot) && File.Exists(paths.MarkerPath))
		{
			bool migrated = ValidateMarker(paths.MarkerPath);
			ValidateReceipt(paths);
			if (migrated && !File.Exists(paths.CleanupReceiptPath))
				throw new InvalidOperationException("已迁移的数据缺少旧源清理收据");
			paths.EnsureCreated();
			return new StorageBootstrapResult(migrated, true, legacyDataPath, ReadMigrationId(paths));
		}
		if (Directory.Exists(paths.DataRoot) && Directory.EnumerateFileSystemEntries(paths.DataRoot).Any())
			throw new InvalidOperationException($"新数据目录不是空目录且缺少有效 marker: {paths.DataRoot}");

		string source = Path.GetFullPath(legacyDataPath ?? LegacyDataPathResolver.Resolve());
		if (allowLegacyMigration && Directory.Exists(source)) EnsureLegacySourceSafe(source);
		bool hasLegacyData = allowLegacyMigration && Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any();
		string migrationId = Guid.NewGuid().ToString("N");
		string staging = paths.DataRoot + $".staging-{migrationId}";
		try
		{
			Directory.CreateDirectory(staging);
			AppStoragePaths.EnsureNoReparsePoints(staging, paths.PackageRoot);
			CreateLayout(staging);
			if (hasLegacyData && !string.Equals(source, paths.DataRoot, PathComparison))
			{
				EnsureLegacyDatabaseAvailable(source);
				CopyLegacy(source, staging);
			}
			WriteMarker(staging, productVersion, rid, hasLegacyData, migrationId);
			if (hasLegacyData)
				WriteAtomicFile(Path.Combine(staging, AppStoragePaths.CleanupReceiptFileName), JsonSerializer.Serialize(new { schema_version = 1, status = "pending", migration_id = migrationId }) + Environment.NewLine);
			ValidateStaging(staging, paths);
			if (Directory.Exists(paths.DataRoot) && Directory.EnumerateFileSystemEntries(paths.DataRoot).Any())
				throw new InvalidOperationException("迁移期间 data 目录发生变化，已拒绝覆盖");
			if (Directory.Exists(paths.DataRoot)) Directory.Delete(paths.DataRoot);
			MoveStaging(staging, paths.DataRoot);
			return new StorageBootstrapResult(hasLegacyData, false, source, hasLegacyData ? migrationId : null);
		}
		catch
		{
			TryDeleteDirectory(staging);
			throw;
		}
	}

	/// <summary>ready 后清理已迁移的旧数据；失败保留收据供下次重试。</summary>
	public static void CleanupLegacy(StorageBootstrapResult result, AppStoragePaths paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		string source = Path.GetFullPath(LegacyDataPathResolver.Resolve());
		if (!result.Migrated && !File.Exists(paths.CleanupReceiptPath)) return;
		if (result.Migrated && !string.IsNullOrWhiteSpace(result.LegacyDataPath)
			&& !string.Equals(source, Path.GetFullPath(result.LegacyDataPath), PathComparison)) return;
		if (!Directory.Exists(source)) { TryDeleteReceipt(paths.CleanupReceiptPath); return; }
		try
		{
			EnsureLegacySourceSafe(source);
			EnsureLegacyDatabaseAvailable(source);
			foreach (string relative in KnownLegacyPaths)
			{
				string candidate = Path.Combine(source, relative);
				EnsureLegacyEntrySafe(candidate);
				if (File.Exists(candidate)) File.Delete(candidate);
				else if (Directory.Exists(candidate)) Directory.Delete(candidate, true);
			}
			foreach (string backup in Directory.EnumerateFiles(source, AppPaths.DatabaseFileName + "-pre-migration-*", SearchOption.TopDirectoryOnly))
			{
				EnsureLegacyEntrySafe(backup);
				File.Delete(backup);
			}
			if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
			TryDeleteReceipt(paths.CleanupReceiptPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			try
			{
				string? migrationId = result.MigrationId ?? ReadMigrationId(paths);
				if (!string.IsNullOrWhiteSpace(migrationId))
					WriteAtomicFile(paths.CleanupReceiptPath, JsonSerializer.Serialize(new { schema_version = 1, status = "pending", migration_id = migrationId, error_type = exception.GetType().FullName }) + Environment.NewLine);
			}
			catch { }
		}
	}

	private static void TryDeleteReceipt(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); } catch { }
	}

	private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
	private static void ValidateExistingTarget(AppStoragePaths paths)
	{
		if (File.Exists(paths.DataRoot)) throw new IOException($"data 路径被文件占用: {paths.DataRoot}");
		if (Directory.Exists(paths.DataRoot)) AppStoragePaths.EnsureNoReparsePoints(paths.DataRoot, paths.PackageRoot);
	}

	private static void CreateLayout(string root)
	{
		string[] directories = [
			"core/database", "core/security", "knowledge/documents", "resources/installed/live2d", "resources/cache", "resources/temp/import",
			"plugins/installed", "plugins/data", "plugins/cache/webview", "plugins/cache/packages/inbox", "plugins/temp/staging",
			"webview/cache/host", "automation/temp/browser", "diagnostics/logs", "legacy/unclassified",
		];
		foreach (string relative in directories) Directory.CreateDirectory(Path.Combine(root, relative));
	}

	private static void CopyLegacy(string source, string staging)
	{
		foreach (string entry in Directory.EnumerateFileSystemEntries(source))
		{
			string name = Path.GetFileName(entry);
			if (name is "." or ".." || name is "webview" or "staging" or "temp" or "tmp" or "cache") continue;
			if (name.Equals(AppPaths.DatabaseFileName + "-wal", StringComparison.OrdinalIgnoreCase)
				|| name.Equals(AppPaths.DatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith(AppPaths.DatabaseFileName + "-pre-migration-", StringComparison.OrdinalIgnoreCase)) continue;
			if (name.Equals(AppPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase))
			{
				CopyDatabase(entry, Path.Combine(staging, "core", "database", AppPaths.DatabaseFileName));
				CopyMatchingFiles(source, staging, $"{AppPaths.DatabaseFileName}-pre-migration-");
			}
			else if (name.Equals("secret.key", StringComparison.OrdinalIgnoreCase))
			{
				string target = Path.Combine(staging, "core", "security", name);
				CopyFileChecked(entry, target);
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			else if (name.Equals("knowledge", StringComparison.OrdinalIgnoreCase)) CopyKnowledge(entry, Path.Combine(staging, "knowledge", "documents"));
			else if (name.Equals("resources", StringComparison.OrdinalIgnoreCase)) CopyResources(entry, staging);
			else if (name.Equals("plugins", StringComparison.OrdinalIgnoreCase)) CopyPlugins(entry, staging);
			else if (name.Equals("plugin-data", StringComparison.OrdinalIgnoreCase)) CopyTree(entry, Path.Combine(staging, "plugins", "data"));
			else if (name.Equals("log", StringComparison.OrdinalIgnoreCase)) CopyTree(entry, Path.Combine(staging, "diagnostics", "logs"));
			else CopyTree(entry, Path.Combine(staging, "legacy", "unclassified", name));
		}
	}

	private static void CopyDatabase(string source, string target)
	{
		EnsureRegularFile(source);
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		using (SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadWrite }.ToString()))
		{
			connection.Open();
			ExecuteScalarCheck(connection, "PRAGMA quick_check;", "quick_check");
			using SqliteCommand vacuum = connection.CreateCommand();
			vacuum.CommandText = "VACUUM INTO $target";
			vacuum.Parameters.AddWithValue("$target", target);
			vacuum.ExecuteNonQuery();
			connection.Close();
		}
		using (SqliteConnection copied = new(new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadOnly }.ToString()))
		{
			copied.Open();
			ExecuteScalarCheck(copied, "PRAGMA integrity_check;", "integrity_check");
			copied.Close();
		}
		SqliteConnection.ClearAllPools();
		VerifyFile(target);
	}

	private static void ExecuteScalarCheck(SqliteConnection connection, string sql, string operation)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = sql;
		string result = command.ExecuteScalar()?.ToString() ?? "";
		if (!result.Equals("ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"旧数据库 {operation} 失败: {result}");
	}

	private static void CopyMatchingFiles(string source, string staging, string prefix)
	{
		foreach (string path in Directory.EnumerateFiles(source, prefix + "*", SearchOption.TopDirectoryOnly))
			CopyFileChecked(path, Path.Combine(staging, "core", "database", Path.GetFileName(path)));
	}

	private static void CopyKnowledge(string source, string target)
	{
		// knowledge 下除明确临时项外全部保全，不能只复制默认 Memory.md。
		EnsureDirectoryTree(source);
		CopyTreeExcept(source, target, name => name is "cache" or "staging" or "temp");
	}

	private static void CopyResources(string source, string staging)
	{
		EnsureDirectoryTree(source);
		// cache/temp 是明确可丢弃项；installed 之外的持久资源归入 unclassified，避免静默丢失。
		string installed = Path.Combine(staging, "resources", "installed");
		foreach (string entry in Directory.EnumerateFileSystemEntries(source))
		{
			string name = Path.GetFileName(entry);
			if (name is "cache" or "temp" or "staging") continue;
			if (name.Equals("live2d", StringComparison.OrdinalIgnoreCase)) CopyTree(entry, Path.Combine(installed, "live2d"));
			else CopyTree(entry, Path.Combine(staging, "legacy", "unclassified", "resources", name));
		}
	}

	private static void CopyTreeExcept(string source, string target, Func<string, bool> skip)
	{
		EnsureRegularDirectory(source);
		Directory.CreateDirectory(target);
		foreach (string entry in Directory.EnumerateFileSystemEntries(source))
		{
			if (skip(Path.GetFileName(entry))) continue;
			CopyTree(entry, Path.Combine(target, Path.GetFileName(entry)));
		}
	}

	private static void CopyPlugins(string source, string staging)
	{
		EnsureDirectoryTree(source);
		foreach (string entry in Directory.EnumerateFileSystemEntries(source))
		{
			string name = Path.GetFileName(entry);
			// inbox/staging 是未完成安装的缓存，不属于可迁移的持久插件数据。
			if (name is "inbox" or ".staging" or "staging" or "cache" or "temp") continue;
			string target = Path.Combine(staging, "plugins", "installed", name);
			CopyTree(entry, target);
		}
	}

	private static void CopyTree(string source, string target)
	{
		if (File.Exists(source)) { CopyFileChecked(source, target); return; }
		EnsureDirectoryTree(source);
		Directory.CreateDirectory(target);
		foreach (string entry in Directory.EnumerateFileSystemEntries(source)) CopyTree(entry, Path.Combine(target, Path.GetFileName(entry)));
	}

	private static void EnsureRegularDirectory(string path)
	{
		if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException($"迁移源目录无效: {path}");
	}

	private static void EnsureDirectoryTree(string path)
	{
		if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"迁移源包含 reparse point: {path}");
		foreach (string entry in Directory.EnumerateFileSystemEntries(path))
		{
			if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"迁移源包含 reparse point: {entry}");
			if (Directory.Exists(entry)) EnsureDirectoryTree(entry);
		}
	}

	private static void EnsureRegularFile(string path)
	{
		if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"迁移源文件无效: {path}");
	}

	private static void CopyFileChecked(string source, string target)
	{
		EnsureRegularFile(source);
		FileInfo info = new(source);
		if (info.Length > MaxCopiedFileBytes) throw new InvalidOperationException($"迁移文件超过 64 MiB 限制: {source}");
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		File.Copy(source, target, false);
		if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(source));
		VerifyFile(target, info.Length, ComputeHash(source));
	}

	private static void VerifyFile(string path, long? expectedLength = null, string? expectedHash = null)
	{
		FileInfo info = new(path);
		if (!info.Exists || info.Length > MaxCopiedFileBytes || (expectedLength is not null && info.Length != expectedLength)) throw new InvalidOperationException($"迁移文件校验失败: {path}");
		if (expectedHash is not null && !ComputeHash(path).Equals(expectedHash, StringComparison.Ordinal)) throw new InvalidOperationException($"迁移文件摘要校验失败: {path}");
	}

	private static string ComputeHash(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private static void WriteMarker(string root, string productVersion, string rid, bool migrated, string migrationId)
	{
		string marker = Path.Combine(root, AppStoragePaths.MarkerFileName);
		string json = JsonSerializer.Serialize(new
		{
			schema_version = MarkerSchemaVersion,
			status = "ready",
			product_version = productVersion,
			numeric_version = ExtractNumericVersion(productVersion),
			rid,
			migration_id = migrated ? migrationId : null,
			migrated,
			created_at = DateTimeOffset.UtcNow,
		});
		WriteAtomicFile(marker, json + Environment.NewLine);
	}

	private static void WriteAtomicFile(string path, string content)
	{
		string temporary = path + $".tmp-{Guid.NewGuid():N}";
		try
		{
			using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		using (StreamWriter writer = new(stream, new System.Text.UTF8Encoding(false), leaveOpen: true))
		{
			writer.Write(content);
			writer.Flush();
			stream.Flush(true);
		}
			File.Move(temporary, path, true);
		}
		finally
		{
			try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
		}
	}

	private static void ValidateReceipt(AppStoragePaths paths)
	{
		if (!File.Exists(paths.CleanupReceiptPath)) return;
		if ((File.GetAttributes(paths.CleanupReceiptPath) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException("旧数据清理收据不是普通文件");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paths.CleanupReceiptPath));
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("schema_version", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1
			|| !root.TryGetProperty("status", out JsonElement status) || status.ValueKind != JsonValueKind.String || status.GetString() != "pending"
			|| !root.TryGetProperty("migration_id", out JsonElement id) || id.ValueKind != JsonValueKind.String
			|| !Guid.TryParseExact(id.GetString(), "N", out _))
			throw new InvalidOperationException("旧数据清理收据无效");
		using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(paths.MarkerPath));
		string? markerId = marker.RootElement.TryGetProperty("migration_id", out JsonElement markerValue) ? markerValue.GetString() : null;
		if (!string.Equals(markerId, id.GetString(), StringComparison.Ordinal)) throw new InvalidOperationException("旧数据清理收据与 marker 不匹配");
	}

	private static string? ReadMigrationId(AppStoragePaths paths)
	{
		if (!File.Exists(paths.CleanupReceiptPath)) return null;
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paths.CleanupReceiptPath));
		return document.RootElement.TryGetProperty("migration_id", out JsonElement id) && id.ValueKind == JsonValueKind.String
			? id.GetString() : null;
	}

	private static bool ValidateMarker(string path)
	{
		if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException($"数据 marker 不是普通文件: {path}");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("schema_version", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != MarkerSchemaVersion
			|| !root.TryGetProperty("status", out JsonElement status) || status.ValueKind != JsonValueKind.String || status.GetString() != "ready"
			|| !root.TryGetProperty("product_version", out JsonElement product) || product.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(product.GetString())
			|| !root.TryGetProperty("numeric_version", out JsonElement numeric) || numeric.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(numeric.GetString())
			|| !root.TryGetProperty("rid", out JsonElement rid) || rid.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(rid.GetString()))
			throw new InvalidOperationException($"数据 marker 无效: {path}");
		bool migrated = root.TryGetProperty("migrated", out JsonElement migratedValue) && migratedValue.ValueKind == JsonValueKind.True;
		if (migrated && (!root.TryGetProperty("migration_id", out JsonElement id) || id.ValueKind != JsonValueKind.String || !Guid.TryParseExact(id.GetString(), "N", out _)))
			throw new InvalidOperationException($"数据 marker 缺少有效迁移标识: {path}");
		if (!migrated && root.TryGetProperty("migration_id", out JsonElement nonMigratedId) && nonMigratedId.ValueKind != JsonValueKind.Null)
			throw new InvalidOperationException($"数据 marker 的迁移字段无效: {path}");
		return migrated;
	}

	private static void ValidateStaging(string staging, AppStoragePaths paths)
	{
		ValidateMarker(Path.Combine(staging, AppStoragePaths.MarkerFileName));
		AppStoragePaths.EnsureNoReparsePoints(staging, paths.PackageRoot);
	}

	private static string ExtractNumericVersion(string version)
	{
		string value = version.TrimStart('v', 'V').Split(['-', '+'], 2)[0];
		return System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9]+\\.[0-9]+\\.[0-9]+$") ? value : "0.0.0";
	}

	private static void MoveStaging(string staging, string target)
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		IOException? last = null;
		for (int attempt = 0; attempt < 10; attempt++)
		{
			try { Directory.Move(staging, target); return; }
			catch (IOException exception) { last = exception; if (attempt < 9) Thread.Sleep(50); }
		}
		throw new IOException("无法提交 data staging", last);
	}

	private static void EnsureLegacySourceSafe(string source)
	{
		string full = Path.GetFullPath(source);
		string? current = full;
		while (current is not null && !string.IsNullOrEmpty(Path.GetPathRoot(current)))
		{
			if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidOperationException("旧数据路径包含符号链接或 reparse point");
			string? parent = Path.GetDirectoryName(current);
			if (parent is null || string.Equals(parent, current, PathComparison)) break;
			current = parent;
		}
		EnsureDirectoryTree(full);
	}

	private static void EnsureLegacyEntrySafe(string path)
	{
		if (!File.Exists(path) && !Directory.Exists(path)) return;
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException($"旧数据待清理项包含 reparse point: {path}");
		if (Directory.Exists(path)) EnsureDirectoryTree(path);
	}

	private static void EnsureLegacyDatabaseAvailable(string source)
	{
		string database = Path.Combine(source, AppPaths.DatabaseFileName);
		if (!File.Exists(database)) return;
		EnsureRegularFile(database);
		SqliteConnection.ClearAllPools();
		try
		{
			using FileStream stream = new(database, FileMode.Open, FileAccess.Read, FileShare.None);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new IOException("旧数据库仍被其它进程使用，已拒绝迁移", exception);
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
	}
}

/// <summary>存储 bootstrap 的结果，用于 ready 后进行旧源清理。</summary>
public sealed record StorageBootstrapResult(bool Migrated, bool ExistingMarker, string? LegacyDataPath, string? MigrationId = null);
