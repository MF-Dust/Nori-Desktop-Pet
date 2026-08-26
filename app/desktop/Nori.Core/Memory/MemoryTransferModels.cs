using System.Text.Json.Serialization;

namespace Nori.Core.Memory;

/// <summary>记忆传输的固定安全边界；预览令牌只存在于进程内。</summary>
public sealed record MemoryTransferLimits
{
	private int _maxFieldBytes = 16 * 1024;

	public int MaxItems { get; init; } = 1000;
	public int MaxBytes { get; init; } = 4 * 1024 * 1024;
	public int MaxFieldBytes { get => _maxFieldBytes; init => _maxFieldBytes = value; }
	/// <summary>旧测试/调用方的兼容别名；限制始终按 UTF-8 字节计算。</summary>
	public int MaxFieldLength { get => _maxFieldBytes; init => _maxFieldBytes = value; }
	public TimeSpan PreviewLifetime { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>nori-memory-v1 的导出文档；仅包含可跨设备迁移的白名单字段。</summary>
public sealed record MemoryTransferDocument
{
	[JsonPropertyName("version")]
	public string? Version { get; init; }

	[JsonPropertyName("format")]
	public string? Format { get; init; }

	[JsonPropertyName("exported_at")]
	public string? ExportedAt { get; init; }

	[JsonPropertyName("memories")]
	public IReadOnlyList<MemoryTransferItem> Memories { get; init; } = [];
}

/// <summary>导出和导入均允许的记忆字段；永不包含 ID、状态、向量、来源正文或工具数据。</summary>
public sealed record MemoryTransferItem
{
	[JsonPropertyName("content")]
	public string? Content { get; init; }

	[JsonPropertyName("canonical_summary")]
	public string? CanonicalSummary { get; init; }

	[JsonPropertyName("persona_summary")]
	public string? PersonaSummary { get; init; }

	[JsonPropertyName("kind")]
	public string? Kind { get; init; }

	[JsonPropertyName("confidence")]
	public double? Confidence { get; init; }

	[JsonPropertyName("importance")]
	public double? Importance { get; init; }

	[JsonPropertyName("tags")]
	public string? Tags { get; init; }

	[JsonPropertyName("created_at")]
	public string? CreatedAt { get; init; }

	[JsonPropertyName("updated_at")]
	public string? UpdatedAt { get; init; }

	[JsonPropertyName("source_type")]
	public string? SourceType { get; init; }

	[JsonPropertyName("dedupe_hash")]
	public string? DedupeHash { get; init; }
}

/// <summary>传输层向前端返回的安全导出摘要。</summary>
public sealed record MemoryTransferExport
{
	public required MemoryTransferDocument Document { get; init; }
	public required string FileName { get; init; }
	public required string Content { get; init; }
	public required string Version { get; init; }
	public required string ExportedAt { get; init; }
	public int TotalCount { get; init; }
	public int ActiveCount { get; init; }
	public int ArchivedCount { get; init; }
	public IReadOnlyList<string> SanitizedFields { get; init; } = [];
}

/// <summary>导入预览或提交时的稳定安全错误类别。</summary>
public enum MemoryTransferErrorCategory
{
	InvalidJson,
	UnsupportedVersion,
	InvalidPayload,
	PayloadTooLarge,
	TooManyItems,
	InvalidItem,
	PreviewExpired,
	PreviewAlreadyUsed,
	InvalidPreviewToken,
	RevalidationFailed,
	WriteFailed,
}

/// <summary>导入冲突来源；不会携带本地记忆正文或数据库 ID。</summary>
public enum MemoryTransferConflictReason
{
	Existing,
	DuplicateInPayload,
}

/// <summary>已有记忆时的提交策略。</summary>
public enum MemoryTransferConflictStrategy
{
	Skip,
	Overwrite,
	CreateCopy,
}

/// <summary>无正文的冲突摘要。</summary>
public sealed record MemoryTransferConflict
{
	public required int ItemIndex { get; init; }
	public required string DedupeHash { get; init; }
	public required string Kind { get; init; }
	public required MemoryTransferConflictReason Reason { get; init; }
}

/// <summary>预览所需的受限显示信息；内容只截取为短摘要。</summary>
public sealed record MemoryTransferPreviewItem
{
	public required int ItemIndex { get; init; }
	public required string ContentSummary { get; init; }
	public required string Kind { get; init; }
	public required double Importance { get; init; }
	public required double Confidence { get; init; }
	public string? Tags { get; init; }
	public MemoryTransferConflictReason? ConflictReason { get; init; }
}

/// <summary>不含异常正文的错误摘要。</summary>
public sealed record MemoryTransferError
{
	public required MemoryTransferErrorCategory Category { get; init; }
	public int Count { get; init; } = 1;
}

/// <summary>导入预览结果；只有完全通过校验时才签发令牌。</summary>
public sealed record MemoryTransferPreview
{
	public bool IsValid { get; init; }
	public string? PreviewToken { get; init; }
	public int TotalCount { get; init; }
	public int AcceptedCount { get; init; }
	public int InvalidCount { get; init; }
	public IReadOnlyList<MemoryTransferPreviewItem> Items { get; init; } = [];
	public IReadOnlyList<MemoryTransferConflict> Conflicts { get; init; } = [];
	public IReadOnlyList<MemoryTransferError> Errors { get; init; } = [];
	public int ConflictCount => Conflicts.Count(conflict => conflict.Reason == MemoryTransferConflictReason.Existing);
	public int DuplicateCount => Conflicts.Count(conflict => conflict.Reason == MemoryTransferConflictReason.DuplicateInPayload);
}

/// <summary>一次性提交结果；不返回记忆正文或内部异常。</summary>
public sealed record MemoryTransferCommitResult
{
	public bool Succeeded { get; init; }
	public int AddedCount { get; init; }
	public int UpdatedCount { get; init; }
	public int SkippedCount { get; init; }
	public int ConflictCount { get; init; }
	public IReadOnlyList<MemoryTransferConflict> Conflicts { get; init; } = [];
	public IReadOnlyList<MemoryTransferError> Errors { get; init; } = [];
}

/// <summary>核心层抛出的稳定、无正文传输错误。</summary>
public sealed class MemoryTransferException : InvalidOperationException
{
	public MemoryTransferException(MemoryTransferErrorCategory category)
		: base(MessageFor(category))
	{
		Category = category;
	}

	public MemoryTransferErrorCategory Category { get; }

	public static string MessageFor(MemoryTransferErrorCategory category) => category switch
	{
		MemoryTransferErrorCategory.InvalidJson => "记忆传输文件不是有效的 JSON",
		MemoryTransferErrorCategory.UnsupportedVersion => "不支持的记忆传输版本",
		MemoryTransferErrorCategory.InvalidPayload => "记忆传输文件格式无效",
		MemoryTransferErrorCategory.PayloadTooLarge => "记忆传输数据超过大小上限",
		MemoryTransferErrorCategory.TooManyItems => "记忆传输条目数量超过上限",
		MemoryTransferErrorCategory.InvalidItem => "记忆传输条目不符合安全格式",
		MemoryTransferErrorCategory.PreviewExpired => "导入预览已过期，请重新解析文件",
		MemoryTransferErrorCategory.PreviewAlreadyUsed => "导入预览已使用，请重新解析文件",
		MemoryTransferErrorCategory.InvalidPreviewToken => "导入预览无效，请重新解析文件",
		MemoryTransferErrorCategory.RevalidationFailed => "导入预览校验失败，请重新解析文件",
		MemoryTransferErrorCategory.WriteFailed => "记忆传输写入失败",
		_ => "记忆传输失败",
	};
}

internal sealed record MemoryTransferValidatedItem(
	int ItemIndex,
	string Content,
	MemoryKind Kind,
	double Importance,
	double Confidence,
	string? Tags,
	string? CanonicalSummary,
	string? PersonaSummary,
	string DedupeHash);

internal sealed record MemoryTransferStoreCommit(
	int AddedCount,
	int UpdatedCount,
	int SkippedCount,
	IReadOnlyList<MemoryTransferConflict> Conflicts,
	IReadOnlyList<MemoryEmbeddingWorkItem> EmbeddingWork);
