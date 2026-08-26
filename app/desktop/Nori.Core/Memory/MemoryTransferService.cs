using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nori.Core.Memory;

/// <summary>
/// nori-memory-v1 的安全导出、预览和提交入口。
/// 传输内容只经过白名单解析；预览令牌保留在内存且只能被原子消费一次。
/// </summary>
public sealed class MemoryTransferService : IDisposable
{
	public const string Format = "nori-memory-v1";
	public const string ImportSource = "memory_transfer";
	private const int PreviewSummaryLength = 240;
	private const int MaxPendingPreviews = 32;
	private static readonly UTF8Encoding Utf8 = new(false, true);
	private static readonly JsonSerializerOptions ExportJsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false,
	};
	private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
	{
		"version", "format", "exported_at", "memories",
	};
	private static readonly HashSet<string> ItemFields = new(StringComparer.Ordinal)
	{
		"content", "canonical_summary", "persona_summary", "kind", "confidence", "importance", "tags",
		"created_at", "updated_at", "source_type", "dedupe_hash",
	};

	private readonly MemoryStore _store;
	private readonly MemoryTransferLimits _limits;
	private readonly TimeProvider _timeProvider;
	private readonly Action<MemoryEmbeddingWorkItem>? _queueEmbedding;
	private readonly Lock _previewGate = new();
	private readonly Dictionary<string, PendingPreview> _previews = [];
	private readonly Dictionary<string, DateTimeOffset> _usedTokens = [];
	private readonly Dictionary<string, DateTimeOffset> _expiredTokens = [];
	private int _disposed;

	public MemoryTransferService(
		MemoryStore store,
		MemoryTransferLimits? limits = null,
		TimeProvider? timeProvider = null,
		Action<MemoryEmbeddingWorkItem>? queueEmbedding = null)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_limits = limits ?? new MemoryTransferLimits();
		_timeProvider = timeProvider ?? TimeProvider.System;
		_queueEmbedding = queueEmbedding;
		ValidateLimits(_limits);
	}

	/// <summary>保留直接以 MemoryService 构造的 Core 调用兼容入口；此入口不安装后台向量回调。</summary>
	public MemoryTransferService(MemoryService memory, MemoryTransferLimits? limits = null, TimeProvider? timeProvider = null)
		: this((memory ?? throw new ArgumentNullException(nameof(memory))).Store, limits, timeProvider)
	{
	}

	public MemoryTransferLimits Limits => _limits;

	/// <summary>导出 nori-memory-v1 文档，保留早期 Core 调用的文档返回形状。</summary>
	public MemoryTransferDocument Export() => ExportResult().Document;

	/// <summary>导出全部可迁移记忆，并在返回前校验 UTF-8 总大小。</summary>
	public MemoryTransferExport ExportResult()
	{
		EnsureNotDisposed();
		try
		{
			DateTimeOffset now = _timeProvider.GetUtcNow();
			List<MemoryItem> memories = _store.GetAll(int.MaxValue)
				.Where(item => IsEligibleForTransfer(item, now))
				.OrderBy(item => item.Id)
				.ToList();
			if (memories.Count > _limits.MaxItems) throw new MemoryTransferException(MemoryTransferErrorCategory.TooManyItems);

			List<MemoryTransferItem> items = new(memories.Count);
			foreach (MemoryItem item in memories) items.Add(ToExportItem(item));
			string exportedAt = now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
			MemoryTransferDocument document = new()
			{
				Version = Format,
				Format = Format,
				ExportedAt = exportedAt,
				Memories = items,
			};
			string content = SerializeDocument(document);
			EnsureByteLimit(content);

			int active = memories.Count(item => item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
				|| item.Status.Equals("dormant", StringComparison.OrdinalIgnoreCase));
			int archived = memories.Count(item => item.Status.Equals("archived", StringComparison.OrdinalIgnoreCase));
			return new MemoryTransferExport
			{
				Document = document,
				FileName = $"nori-memory-v1-{now:yyyyMMdd}.json",
				Content = content,
				Version = Format,
				ExportedAt = exportedAt,
				TotalCount = memories.Count,
				ActiveCount = active,
				ArchivedCount = archived,
				SanitizedFields =
				[
					"content", "canonicalSummary", "personaSummary", "kind", "importance", "confidence", "tags",
					"createdAt", "updatedAt", "sourceType", "dedupeHash",
				],
			};
		}
		catch (MemoryTransferException)
		{
			throw;
		}
		catch
		{
			throw new MemoryTransferException(MemoryTransferErrorCategory.WriteFailed);
		}
	}

	/// <summary>兼容仅需要导出 JSON 正文的调用方。</summary>
	public string ExportJson() => ExportResult().Content;

	/// <summary>解析并分析导入文件；预览阶段绝不写入数据库。</summary>
	public MemoryTransferPreview Preview(string? content)
	{
		EnsureNotDisposed();
		if (string.IsNullOrWhiteSpace(content)) return PreviewFailure(0, 1, MemoryTransferErrorCategory.InvalidJson);

		byte[] utf8;
		try
		{
			utf8 = Utf8.GetBytes(content);
		}
		catch (EncoderFallbackException)
		{
			return PreviewFailure(0, 1, MemoryTransferErrorCategory.InvalidPayload);
		}
		if (utf8.Length > _limits.MaxBytes) return PreviewFailure(0, 1, MemoryTransferErrorCategory.PayloadTooLarge);

		try
		{
			using JsonDocument document = JsonDocument.Parse(utf8, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 16,
			});
			return PreviewDocument(document.RootElement);
		}
		catch (JsonException)
		{
			return PreviewFailure(0, 1, MemoryTransferErrorCategory.InvalidJson);
		}
		catch (MemoryTransferException exception)
		{
			return PreviewFailure(0, 1, exception.Category);
		}
		catch (ArgumentException)
		{
			return PreviewFailure(0, 1, MemoryTransferErrorCategory.InvalidPayload);
		}
		catch
		{
			return PreviewFailure(0, 1, MemoryTransferErrorCategory.WriteFailed);
		}
	}

	/// <summary>同步提交预览令牌；客户端传来的 items 永远不会作为写入输入。</summary>
	public MemoryTransferCommitResult Commit(
		string? previewToken,
		MemoryTransferConflictStrategy strategy = MemoryTransferConflictStrategy.Skip)
	{
		EnsureNotDisposed();
		if (!Enum.IsDefined(strategy)) return CommitFailure(MemoryTransferErrorCategory.InvalidPayload);
		if (!TryTakePreview(previewToken, out PendingPreview? pending, out MemoryTransferErrorCategory tokenError))
		{
			return CommitFailure(tokenError);
		}

		try
		{
			if (!Revalidate(pending!)) return CommitFailure(MemoryTransferErrorCategory.RevalidationFailed);
			MemoryTransferStoreCommit stored = _store.CommitMemoryTransfer(pending!.Items, strategy, _timeProvider.GetUtcNow());
			foreach (MemoryEmbeddingWorkItem work in stored.EmbeddingWork)
			{
				try { _queueEmbedding?.Invoke(work); }
				catch { /* 写入已提交；后台排队异常不得让令牌重放。 */ }
			}
			return new MemoryTransferCommitResult
			{
				Succeeded = true,
				AddedCount = stored.AddedCount,
				UpdatedCount = stored.UpdatedCount,
				SkippedCount = stored.SkippedCount,
				ConflictCount = stored.Conflicts.Count,
				Conflicts = stored.Conflicts,
			};
		}
		catch (MemoryTransferException exception)
		{
			return CommitFailure(exception.Category);
		}
		catch
		{
			return CommitFailure(MemoryTransferErrorCategory.WriteFailed);
		}
	}

	/// <summary>异步包装，保持桥接命令不会在 UI 调用链上阻塞。</summary>
	public Task<MemoryTransferCommitResult> CommitAsync(
		string? previewToken,
		MemoryTransferConflictStrategy strategy = MemoryTransferConflictStrategy.Skip,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Commit(previewToken, strategy));
	}

	/// <summary>兼容未传入冲突策略的异步调用。</summary>
	public Task<MemoryTransferCommitResult> CommitAsync(string? previewToken, CancellationToken cancellationToken) =>
		CommitAsync(previewToken, MemoryTransferConflictStrategy.Skip, cancellationToken);

	/// <summary>kind 与规范化摘要组成的稳定 SHA-256；不信任传输文件自带的 hash。</summary>
	public static string ComputeDedupeHash(string? kind, string content)
	{
		if (content is null) throw new ArgumentNullException(nameof(content));
		MemoryKind resolved = MemoryKindExtensions.Parse(kind);
		return ComputeDedupeHash(resolved, content);
	}

	internal static string ComputeDedupeHash(MemoryKind kind, string semanticText)
	{
		string input = kind.ToStorage() + "\n" + NormalizeForDedupe(semanticText);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
	}

	/// <summary>只有活跃、休眠或已归档且未超期、未被替代的记忆可以迁移或参与去重。</summary>
	internal static bool IsEligibleForTransfer(MemoryItem item, DateTimeOffset now)
	{
		if (item.SupersededBy is not null) return false;
		if (!item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
			&& !item.Status.Equals("dormant", StringComparison.OrdinalIgnoreCase)
			&& !item.Status.Equals("archived", StringComparison.OrdinalIgnoreCase)) return false;
		if (string.IsNullOrWhiteSpace(item.ExpiresAt)) return true;
		return DateTimeOffset.TryParse(item.ExpiresAt, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out DateTimeOffset expiresAt) && expiresAt > now;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		lock (_previewGate)
		{
			_previews.Clear();
			_usedTokens.Clear();
			_expiredTokens.Clear();
		}
	}

	private MemoryTransferPreview PreviewDocument(JsonElement root)
	{
		if (root.ValueKind != JsonValueKind.Object) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidPayload);
		ValidateProperties(root, RootFields, MemoryTransferErrorCategory.InvalidPayload);
		if (!HasSupportedVersion(root)) throw new MemoryTransferException(MemoryTransferErrorCategory.UnsupportedVersion);
		if (!root.TryGetProperty("memories", out JsonElement source) || source.ValueKind != JsonValueKind.Array)
			throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidPayload);
		if (source.GetArrayLength() > _limits.MaxItems)
			return PreviewFailure(source.GetArrayLength(), 0, MemoryTransferErrorCategory.TooManyItems);
		if (root.TryGetProperty("exported_at", out JsonElement exportedAt)) ValidateOptionalTimestamp(exportedAt);

		List<MemoryTransferValidatedItem> items = [];
		Dictionary<MemoryTransferErrorCategory, int> errors = [];
		int index = 0;
		foreach (JsonElement value in source.EnumerateArray())
		{
			index++;
			try { items.Add(ParseItem(value, index)); }
			catch (MemoryTransferException exception) { AddError(errors, exception.Category); }
		}
		if (errors.Count > 0)
		{
			return new MemoryTransferPreview
			{
				IsValid = false,
				TotalCount = source.GetArrayLength(),
				AcceptedCount = 0,
				InvalidCount = errors.Values.Sum(),
				Errors = ToErrors(errors),
			};
		}

		HashSet<string> existing = GetExistingHashes(_timeProvider.GetUtcNow());
		List<MemoryTransferConflict> conflicts = [];
		Dictionary<int, MemoryTransferConflictReason> reasons = [];
		HashSet<string> payloadHashes = new(StringComparer.Ordinal);
		foreach (MemoryTransferValidatedItem item in items)
		{
			MemoryTransferConflictReason? reason = null;
			if (!payloadHashes.Add(item.DedupeHash)) reason = MemoryTransferConflictReason.DuplicateInPayload;
			else if (existing.Contains(item.DedupeHash)) reason = MemoryTransferConflictReason.Existing;
			if (reason is null) continue;
			reasons[item.ItemIndex] = reason.Value;
			conflicts.Add(ToConflict(item, reason.Value));
		}

		string token = StorePreview(items);
		return new MemoryTransferPreview
		{
			IsValid = true,
			PreviewToken = token,
			TotalCount = items.Count,
			AcceptedCount = items.Count - conflicts.Count,
			Items = items.Select(item => ToPreviewItem(item,
				reasons.TryGetValue(item.ItemIndex, out MemoryTransferConflictReason reason) ? reason : null)).ToArray(),
			Conflicts = conflicts,
		};
	}

	private MemoryTransferValidatedItem ParseItem(JsonElement source, int itemIndex)
	{
		if (source.ValueKind != JsonValueKind.Object) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		ValidateProperties(source, ItemFields, MemoryTransferErrorCategory.InvalidItem);
		string content = RequiredString(source, "content", MemoryTransferErrorCategory.InvalidItem).Trim();
		if (content.Length == 0) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		ValidateField(content);

		string? canonical = OptionalString(source, "canonical_summary", MemoryTransferErrorCategory.InvalidItem)?.Trim();
		string? persona = OptionalString(source, "persona_summary", MemoryTransferErrorCategory.InvalidItem)?.Trim();
		string? tags = OptionalString(source, "tags", MemoryTransferErrorCategory.InvalidItem)?.Trim();
		ValidateField(canonical);
		ValidateField(persona);
		ValidateField(tags);
		if (canonical?.Length == 0) canonical = null;
		if (persona?.Length == 0) persona = null;
		if (tags?.Length == 0) tags = null;

		string? kindText = OptionalString(source, "kind", MemoryTransferErrorCategory.InvalidItem);
		ValidateField(kindText);
		MemoryKind kind = ParseKind(kindText);
		double importance = OptionalScore(source, "importance", 0.5);
		double confidence = OptionalScore(source, "confidence", 0.8);
		if (source.TryGetProperty("created_at", out JsonElement createdAt)) ValidateOptionalTimestamp(createdAt);
		if (source.TryGetProperty("updated_at", out JsonElement updatedAt)) ValidateOptionalTimestamp(updatedAt);
		if (source.TryGetProperty("source_type", out JsonElement sourceType)) ValidateOptionalString(sourceType, MemoryTransferErrorCategory.InvalidItem);

		string hash = ComputeDedupeHash(kind, canonical ?? content);
		string? suppliedHash = OptionalString(source, "dedupe_hash", MemoryTransferErrorCategory.InvalidItem);
		if (suppliedHash is not null)
		{
			ValidateField(suppliedHash);
			if (!IsHash(suppliedHash) || !string.Equals(suppliedHash, hash, StringComparison.OrdinalIgnoreCase))
				throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		}
		return new MemoryTransferValidatedItem(itemIndex, content, kind, importance, confidence, tags, canonical, persona, hash);
	}

	private MemoryTransferItem ToExportItem(MemoryItem item)
	{
		if (string.IsNullOrWhiteSpace(item.Content)) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		string content = item.Content.Trim();
		ValidateField(content);
		MemoryKind kind = ParseKind(item.Kind);
		if (!IsScore(item.Importance) || !IsScore(item.Confidence)) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);

		string? canonical = NormalizeOptionalExportField(item.CanonicalSummary);
		string? persona = NormalizeOptionalExportField(item.PersonaSummary);
		string? tags = NormalizeOptionalExportField(item.Tags);
		string semantic = canonical ?? content;
		return new MemoryTransferItem
		{
			Content = content,
			CanonicalSummary = canonical,
			PersonaSummary = persona,
			Kind = kind.ToStorage(),
			Importance = item.Importance,
			Confidence = item.Confidence,
			Tags = tags,
			CreatedAt = SafeTimestamp(item.CreatedAt),
			UpdatedAt = SafeTimestamp(item.UpdatedAt),
			SourceType = SafeSourceType(item.Source),
			DedupeHash = ComputeDedupeHash(kind, semantic),
		};
	}

	private bool Revalidate(PendingPreview pending)
	{
		if (pending.Items.Count > _limits.MaxItems) return false;
		try
		{
			List<MemoryTransferItem> items = [];
			foreach (MemoryTransferValidatedItem item in pending.Items)
			{
				ValidateField(item.Content);
				ValidateField(item.CanonicalSummary);
				ValidateField(item.PersonaSummary);
				ValidateField(item.Tags);
				if (!IsScore(item.Importance) || !IsScore(item.Confidence)
					|| ComputeDedupeHash(item.Kind, item.CanonicalSummary ?? item.Content) != item.DedupeHash) return false;
				items.Add(new MemoryTransferItem
				{
					Content = item.Content,
					CanonicalSummary = item.CanonicalSummary,
					PersonaSummary = item.PersonaSummary,
					Kind = item.Kind.ToStorage(),
					Importance = item.Importance,
					Confidence = item.Confidence,
					Tags = item.Tags,
					DedupeHash = item.DedupeHash,
				});
			}
			EnsureByteLimit(SerializeDocument(new MemoryTransferDocument {Version = Format, Format = Format, Memories = items}));
			return true;
		}
		catch (MemoryTransferException)
		{
			return false;
		}
	}

	private HashSet<string> GetExistingHashes(DateTimeOffset now) => _store.GetAll(int.MaxValue)
		.Where(item => IsEligibleForTransfer(item, now))
		.Select(item => ComputeDedupeHash(item.Kind, item.CanonicalSummary ?? item.Content))
		.ToHashSet(StringComparer.Ordinal);

	private string StorePreview(IReadOnlyList<MemoryTransferValidatedItem> items)
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		lock (_previewGate)
		{
			CleanupTokens(now);
			if (_previews.Count >= MaxPendingPreviews)
			{
				KeyValuePair<string, PendingPreview> oldest = _previews.OrderBy(pair => pair.Value.ExpiresAt).First();
				_previews.Remove(oldest.Key);
				_expiredTokens[oldest.Key] = now;
			}
			string token;
			do { token = CreateToken(); }
			while (_previews.ContainsKey(token) || _usedTokens.ContainsKey(token) || _expiredTokens.ContainsKey(token));
			_previews[token] = new PendingPreview(items.ToArray(), now.Add(_limits.PreviewLifetime));
			return token;
		}
	}

	private bool TryTakePreview(string? token, out PendingPreview? pending, out MemoryTransferErrorCategory error)
	{
		pending = null;
		error = MemoryTransferErrorCategory.InvalidPreviewToken;
		if (string.IsNullOrWhiteSpace(token)) return false;
		DateTimeOffset now = _timeProvider.GetUtcNow();
		lock (_previewGate)
		{
			CleanupTokens(now);
			if (_usedTokens.ContainsKey(token))
			{
				error = MemoryTransferErrorCategory.PreviewAlreadyUsed;
				return false;
			}
			if (_expiredTokens.ContainsKey(token))
			{
				error = MemoryTransferErrorCategory.PreviewExpired;
				return false;
			}
			if (!_previews.Remove(token, out PendingPreview? stored)) return false;
			if (stored.ExpiresAt <= now)
			{
				_expiredTokens[token] = now;
				error = MemoryTransferErrorCategory.PreviewExpired;
				return false;
			}
			_usedTokens[token] = now;
			pending = stored;
			return true;
		}
	}

	private void CleanupTokens(DateTimeOffset now)
	{
		foreach (string token in _previews.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
		{
			_previews.Remove(token);
			_expiredTokens[token] = now;
		}
		DateTimeOffset cutoff = now - _limits.PreviewLifetime;
		foreach (string token in _usedTokens.Where(pair => pair.Value <= cutoff).Select(pair => pair.Key).ToArray()) _usedTokens.Remove(token);
		foreach (string token in _expiredTokens.Where(pair => pair.Value <= cutoff).Select(pair => pair.Key).ToArray()) _expiredTokens.Remove(token);
	}

	private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
		.Replace('+', '-').Replace('/', '_').TrimEnd('=');

	private bool HasSupportedVersion(JsonElement root)
	{
		string? format = OptionalString(root, "format", MemoryTransferErrorCategory.UnsupportedVersion);
		ValidateField(format);
		bool hasVersion = root.TryGetProperty("version", out JsonElement version);
		if (format is not null && !format.Equals(Format, StringComparison.Ordinal)) return false;
		if (!hasVersion) return format is not null;
		if (version.ValueKind == JsonValueKind.String) ValidateField(version.GetString());
		return version.ValueKind switch
		{
			JsonValueKind.String => version.GetString() is string value
				&& (value.Equals(Format, StringComparison.Ordinal) || value.Equals("1", StringComparison.Ordinal)),
			JsonValueKind.Number => version.TryGetInt32(out int number) && number == 1,
			_ => false,
		};
	}

	private static void ValidateProperties(JsonElement source, IReadOnlySet<string> allowed, MemoryTransferErrorCategory category)
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (JsonProperty property in source.EnumerateObject())
		{
			if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) throw new MemoryTransferException(category);
		}
	}

	private string RequiredString(JsonElement source, string name, MemoryTransferErrorCategory category)
	{
		if (!source.TryGetProperty(name, out JsonElement value)) throw new MemoryTransferException(category);
		return ReadString(value, category) ?? throw new MemoryTransferException(category);
	}

	private static string? OptionalString(JsonElement source, string name, MemoryTransferErrorCategory category)
	{
		return source.TryGetProperty(name, out JsonElement value) ? ReadString(value, category) : null;
	}

	private static string? ReadString(JsonElement value, MemoryTransferErrorCategory category)
	{
		if (value.ValueKind == JsonValueKind.Null) return null;
		if (value.ValueKind != JsonValueKind.String) throw new MemoryTransferException(category);
		return value.GetString();
	}

	private double OptionalScore(JsonElement source, string name, double fallback)
	{
		if (!source.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return fallback;
		if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double score) || !IsScore(score))
			throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		return score;
	}

	private void ValidateOptionalString(JsonElement value, MemoryTransferErrorCategory category)
	{
		string? text = ReadString(value, category);
		ValidateField(text);
	}

	private void ValidateOptionalTimestamp(JsonElement value)
	{
		string? text = ReadString(value, MemoryTransferErrorCategory.InvalidPayload);
		ValidateField(text);
		if (text is not null && !TryParseTimestamp(text, out _)) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidPayload);
	}

	private void ValidateField(string? value)
	{
		if (value is null) return;
		try
		{
			if (Utf8.GetByteCount(value) > _limits.MaxFieldBytes) throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		}
		catch (EncoderFallbackException)
		{
			throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem);
		}
	}

	private void EnsureByteLimit(string json)
	{
		try
		{
			if (Utf8.GetByteCount(json) > _limits.MaxBytes) throw new MemoryTransferException(MemoryTransferErrorCategory.PayloadTooLarge);
		}
		catch (EncoderFallbackException)
		{
			throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidPayload);
		}
	}

	private static MemoryKind ParseKind(string? value)
	{
		string normalized = value?.Trim().ToLowerInvariant() ?? "general";
		return normalized switch
		{
			"general" => MemoryKind.General,
			"episodic" or "event" => MemoryKind.Episodic,
			"factual" or "fact" => MemoryKind.Factual,
			"preference" or "prefer" => MemoryKind.Preference,
			"relational" or "relationship" => MemoryKind.Relational,
			"planned" or "plan" => MemoryKind.Planned,
			"identity" or "name" => MemoryKind.Identity,
			_ => throw new MemoryTransferException(MemoryTransferErrorCategory.InvalidItem),
		};
	}

	private static bool IsScore(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

	private static bool IsHash(string value) => value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

	private static string NormalizeForDedupe(string value)
	{
		string normalized = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
		StringBuilder result = new(normalized.Length);
		bool spacePending = false;
		foreach (char character in normalized)
		{
			if (char.IsWhiteSpace(character))
			{
				spacePending = result.Length > 0;
				continue;
			}
			if (spacePending) result.Append(' ');
			result.Append(character);
			spacePending = false;
		}
		return result.ToString();
	}

	private string? NormalizeOptionalExportField(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		string trimmed = value.Trim();
		ValidateField(trimmed);
		return trimmed;
	}

	private static string? SafeTimestamp(string? value)
	{
		return value is not null && TryParseTimestamp(value, out DateTimeOffset timestamp)
			? timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
			: null;
	}

	private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp) => DateTimeOffset.TryParse(
		value,
		CultureInfo.InvariantCulture,
		DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
		out timestamp);

	private static string SafeSourceType(string source) => source.Trim().ToLowerInvariant() switch
	{
		"chat" => "chat",
		"agent" => "agent",
		"manual" => "manual",
		"reflection" => "reflection",
		ImportSource => ImportSource,
		_ => "other",
	};

	private static MemoryTransferPreviewItem ToPreviewItem(
		MemoryTransferValidatedItem item,
		MemoryTransferConflictReason? reason) => new()
	{
		ItemIndex = item.ItemIndex,
		ContentSummary = Summarize(item.Content),
		Kind = item.Kind.ToStorage(),
		Importance = item.Importance,
		Confidence = item.Confidence,
		Tags = item.Tags,
		ConflictReason = reason,
	};

	private static string Summarize(string value) => value.Length <= PreviewSummaryLength
		? value
		: value[..PreviewSummaryLength] + "…";

	private static MemoryTransferConflict ToConflict(MemoryTransferValidatedItem item, MemoryTransferConflictReason reason) => new()
	{
		ItemIndex = item.ItemIndex,
		DedupeHash = item.DedupeHash,
		Kind = item.Kind.ToStorage(),
		Reason = reason,
	};

	private static string SerializeDocument(MemoryTransferDocument document) => JsonSerializer.Serialize(document, ExportJsonOptions);

	private static MemoryTransferPreview PreviewFailure(int totalCount, int invalidCount, MemoryTransferErrorCategory category) => new()
	{
		IsValid = false,
		TotalCount = totalCount,
		InvalidCount = invalidCount,
		Errors = [new MemoryTransferError {Category = category}],
	};

	private static MemoryTransferCommitResult CommitFailure(MemoryTransferErrorCategory category) => new()
	{
		Succeeded = false,
		Errors = [new MemoryTransferError {Category = category}],
	};

	private static void AddError(Dictionary<MemoryTransferErrorCategory, int> counts, MemoryTransferErrorCategory category) =>
		counts[category] = counts.GetValueOrDefault(category) + 1;

	private static IReadOnlyList<MemoryTransferError> ToErrors(Dictionary<MemoryTransferErrorCategory, int> counts) =>
		counts.OrderBy(pair => pair.Key).Select(pair => new MemoryTransferError {Category = pair.Key, Count = pair.Value}).ToArray();

	private static void ValidateLimits(MemoryTransferLimits limits)
	{
		if (limits.MaxItems <= 0 || limits.MaxItems > 1000) throw new ArgumentOutOfRangeException(nameof(limits.MaxItems));
		if (limits.MaxBytes <= 0 || limits.MaxBytes > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(limits.MaxBytes));
		if (limits.MaxFieldBytes <= 0 || limits.MaxFieldBytes > 16 * 1024) throw new ArgumentOutOfRangeException(nameof(limits.MaxFieldBytes));
		if (limits.PreviewLifetime <= TimeSpan.Zero || limits.PreviewLifetime > TimeSpan.FromMinutes(5))
			throw new ArgumentOutOfRangeException(nameof(limits.PreviewLifetime));
	}

	private void EnsureNotDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(MemoryTransferService));
	}

	private sealed record PendingPreview(IReadOnlyList<MemoryTransferValidatedItem> Items, DateTimeOffset ExpiresAt);
}
