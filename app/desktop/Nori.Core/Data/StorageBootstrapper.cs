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
	public static StorageBootstrapResult Bootstrap(AppStoragePaths paths, string productVersion, string rid, string? legacyDataPath = null)
	{
		ArgumentNullException.ThrowIfNull(paths);
		ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
		ArgumentException.ThrowIfNullOrWhiteSpace(rid);
		ValidateExistingTarget(paths);
		if (Directory.Exists(paths.DataRoot) && File.Exists(paths.MarkerPath))
		{
			ValidateMarker(paths.MarkerPath);
			paths.EnsureCreated();
			return new StorageBootstrapResult(false, true, legacyDataPath);
		}
		if (Directory.Exists(paths.DataRoot) && Directory.EnumerateFileSystemEntries(paths.DataRoot).Any())
			throw new InvalidOperationException($"新数据目录不是空目录且缺少有效 marker: {paths.DataRoot}");

		string source = Path.GetFullPath(legacyDataPath ?? LegacyDataPathResolver.Resolve());
		bool hasLegacyData = Directory.Exists(source) && Directory.EnumerateFileSystemEntries(source).Any();
		string staging = paths.DataRoot + $".staging-{Guid.NewGuid():N}";
		try
		{
			Directory.CreateDirectory(staging);
			AppStoragePaths.EnsureNoReparsePoints(staging, paths.PackageRoot);
			CreateLayout(staging);
			if (hasLegacyData && !string.Equals(source, paths.DataRoot, PathComparison)) CopyLegacy(source, staging);
			WriteMarker(staging, productVersion, rid, hasLegacyData);
			FlushMarker(staging);
			ValidateStaging(staging, paths);
			if (Directory.Exists(paths.DataRoot) && Directory.EnumerateFileSystemEntries(paths.DataRoot).Any())
				throw new InvalidOperationException("迁移期间 data 目录发生变化，已拒绝覆盖");
			if (Directory.Exists(paths.DataRoot)) Directory.Delete(paths.DataRoot);
			MoveStaging(staging, paths.DataRoot);
			return new StorageBootstrapResult(hasLegacyData, false, source);
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
		if (!result.Migrated || string.IsNullOrWhiteSpace(result.LegacyDataPath)) return;
		string source = Path.GetFullPath(result.LegacyDataPath);
		if (!Directory.Exists(source)) return;
		try
		{
			foreach (string relative in KnownLegacyPaths)
			{
				string candidate = Path.Combine(source, relative);
				if (File.Exists(candidate)) File.Delete(candidate);
				else if (Directory.Exists(candidate)) Directory.Delete(candidate, true);
			}
			foreach (string backup in Directory.EnumerateFiles(source, AppPaths.DatabaseFileName + "-pre-migration-*", SearchOption.TopDirectoryOnly)) File.Delete(backup);
			foreach (string path in Directory.EnumerateFileSystemEntries(source))
				if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
			if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
			if (File.Exists(paths.CleanupReceiptPath)) File.Delete(paths.CleanupReceiptPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			try { File.WriteAllText(paths.CleanupReceiptPath, JsonSerializer.Serialize(new { source, error = exception.Message })); } catch { }
		}
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
			if (name is "." or ".." || name is "webview" or "staging" or "temp") continue;
			if (name.Equals(AppPaths.DatabaseFileName + "-wal", StringComparison.OrdinalIgnoreCase)
				|| name.Equals(AppPaths.DatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith(AppPaths.DatabaseFileName + "-pre-migration-", StringComparison.OrdinalIgnoreCase)) continue;
			if (name.Equals(AppPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase))
			{
				CopyDatabase(entry, Path.Combine(staging, "core", "database", AppPaths.DatabaseFileName));
				CopyMatchingFiles(source, staging, $"{AppPaths.DatabaseFileName}-pre-migration-");
			}
			else if (name.Equals("secret.key", StringComparison.OrdinalIgnoreCase)) CopyFileChecked(entry, Path.Combine(staging, "core", "security", name));
			else if (name.Equals("knowledge", StringComparison.OrdinalIgnoreCase)) CopyKnowledge(entry, Path.Combine(staging, "knowledge", "documents"));
			else if (name.Equals("resources", StringComparison.OrdinalIgnoreCase)) CopyResources(entry, staging);
			else if (name.Equals("plugins", StringComparison.OrdinalIgnoreCase)) CopyTree(entry, Path.Combine(staging, "plugins", "installed"));
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
		EnsureDirectoryTree(source);
		string file = Path.Combine(source, "Memory.md");
		if (File.Exists(file)) CopyFileChecked(file, Path.Combine(target, "Memory.md"));
	}

	private static void CopyResources(string source, string staging)
	{
		EnsureDirectoryTree(source);
		string live2d = Path.Combine(source, "live2d");
		if (Directory.Exists(live2d)) CopyTree(live2d, Path.Combine(staging, "resources", "installed", "live2d"));
		string cache = Path.Combine(source, "cache");
		if (Directory.Exists(cache)) CopyTree(cache, Path.Combine(staging, "resources", "cache"));
	}

	private static void CopyTree(string source, string target)
	{
		if (File.Exists(source)) { CopyFileChecked(source, target); return; }
		EnsureDirectoryTree(source);
		Directory.CreateDirectory(target);
		foreach (string entry in Directory.EnumerateFileSystemEntries(source)) CopyTree(entry, Path.Combine(target, Path.GetFileName(entry)));
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

	private static void WriteMarker(string root, string productVersion, string rid, bool migrated)
	{
		string marker = Path.Combine(root, AppStoragePaths.MarkerFileName);
		string json = JsonSerializer.Serialize(new { schema_version = MarkerSchemaVersion, product_version = productVersion, numeric_version = ExtractNumericVersion(productVersion), rid, migrated, created_at = DateTimeOffset.UtcNow });
		File.WriteAllText(marker + ".tmp", json + Environment.NewLine);
		File.Move(marker + ".tmp", marker, true);
	}

	private static void FlushMarker(string root)
	{
		using FileStream stream = new(Path.Combine(root, AppStoragePaths.MarkerFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
		stream.Flush(true);
	}

	private static void ValidateMarker(string path)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("schema_version", out JsonElement schema) || schema.GetInt32() != MarkerSchemaVersion
			|| !root.TryGetProperty("product_version", out JsonElement product) || string.IsNullOrWhiteSpace(product.GetString())
			|| !root.TryGetProperty("numeric_version", out JsonElement numeric) || string.IsNullOrWhiteSpace(numeric.GetString())
			|| !root.TryGetProperty("rid", out JsonElement rid) || string.IsNullOrWhiteSpace(rid.GetString()))
			throw new InvalidOperationException($"数据 marker 无效: {path}");
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

	private static void TryDeleteDirectory(string path)
	{
		try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
	}
}

/// <summary>存储 bootstrap 的结果，用于 ready 后进行旧源清理。</summary>
public sealed record StorageBootstrapResult(bool Migrated, bool ExistingMarker, string? LegacyDataPath);
