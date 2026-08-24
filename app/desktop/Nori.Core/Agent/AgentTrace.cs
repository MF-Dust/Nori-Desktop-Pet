using Nori.Core.Security;

namespace Nori.Core.Agent;

/// <summary>一次 Agent 运行的安全结构化用量。</summary>
public sealed record AgentTraceUsage
{
	/// <summary>输入 Token 数。</summary>
	public int PromptTokens { get; }

	/// <summary>输出 Token 数。</summary>
	public int CompletionTokens { get; }

	/// <summary>总 Token 数。</summary>
	public int TotalTokens { get; }

	/// <summary>命中缓存的 Token 数。</summary>
	public int CachedTokens { get; }

	/// <summary>缓存命中率。</summary>
	public double CacheHitRate { get; }

	/// <summary>模型标识。</summary>
	public string? Model { get; }

	public AgentTraceUsage(
		int promptTokens,
		int completionTokens,
		int totalTokens,
		int cachedTokens,
		double cacheHitRate,
		string? model)
	{
		PromptTokens = Math.Max(0, promptTokens);
		CompletionTokens = Math.Max(0, completionTokens);
		TotalTokens = Math.Max(0, totalTokens);
		CachedTokens = Math.Max(0, cachedTokens);
		CacheHitRate = double.IsFinite(cacheHitRate) ? Math.Clamp(cacheHitRate, 0, 100) : 0;
		Model = AgentTraceRecord.Sanitize(model, AgentTraceRecord.MaxModelLength);
	}
}

/// <summary>
/// Agent Trace 的单条安全记录。
/// 此类型只允许元数据、阶段、工具名和用量，不承载提示词、回复、工具参数或工具结果。
/// </summary>
public sealed record AgentTraceRecord
{
	public const int MaxSessionIdLength = 128;
	public const int MaxToolNameLength = 80;
	internal const int MaxPhaseLength = 40;
	internal const int MaxStatusLength = 32;
	internal const int MaxFailureCategoryLength = 48;
	internal const int MaxModelLength = 128;

	/// <summary>记录时间 (UTC)。</summary>
	public DateTimeOffset RecordedAtUtc { get; }

	/// <summary>会话标识。</summary>
	public string SessionId { get; }

	/// <summary>运行阶段。</summary>
	public string Phase { get; }

	/// <summary>阶段耗时 (毫秒)。</summary>
	public long DurationMs { get; }

	/// <summary>工具调用轮次。</summary>
	public int? Iteration { get; }

	/// <summary>工具名。</summary>
	public string? ToolName { get; }

	/// <summary>阶段状态。</summary>
	public string Status { get; }

	/// <summary>不含异常正文的稳定失败分类。</summary>
	public string? FailureCategory { get; }

	/// <summary>LLM 用量与缓存指标。</summary>
	public AgentTraceUsage? Usage { get; }

	public AgentTraceRecord(
		string sessionId,
		string phase,
		long durationMs,
		int? iteration,
		string? toolName,
		string status,
		string? failureCategory = null,
		AgentTraceUsage? usage = null)
	{
		RecordedAtUtc = DateTimeOffset.UtcNow;
		SessionId = Sanitize(sessionId, MaxSessionIdLength) ?? string.Empty;
		Phase = Sanitize(phase, MaxPhaseLength) ?? string.Empty;
		DurationMs = Math.Max(0, durationMs);
		Iteration = iteration is >= 0 ? iteration : null;
		ToolName = Sanitize(toolName, MaxToolNameLength);
		Status = Sanitize(status, MaxStatusLength) ?? string.Empty;
		FailureCategory = Sanitize(failureCategory, MaxFailureCategoryLength);
		Usage = usage;
	}

	internal static string? Normalize(string? value, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		string trimmed = value.Trim();
		return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
	}

	internal static string? Sanitize(string? value, int maxLength)
	{
		string? normalized = Normalize(value, maxLength);
		return normalized is null ? null : Normalize(SensitiveDataRedactor.Redact(normalized), maxLength);
	}
}

/// <summary>Agent Trace 接收端。实现不得依赖任何对话正文。</summary>
public abstract class AgentTraceSink
{
	/// <summary>默认不记录任何内容的接收端。</summary>
	public static AgentTraceSink Noop { get; } = new NoopAgentTraceSink();

	/// <summary>接收一条安全结构化记录。</summary>
	public abstract void Record(AgentTraceRecord record);

	private sealed class NoopAgentTraceSink : AgentTraceSink
	{
		public override void Record(AgentTraceRecord record)
		{
			// 默认关闭 Trace，避免无意义的分配与输出。
		}
	}
}

/// <summary>线程安全、有界的内存 Trace 收集器。</summary>
public sealed class AgentTraceCollector : AgentTraceSink
{
	private readonly object _gate = new();
	private readonly Queue<AgentTraceRecord> _records = [];

	/// <summary>最多保留的记录数。</summary>
	public int Capacity { get; }

	/// <summary>当前保留的记录数。</summary>
	public int Count
	{
		get
		{
			lock (_gate) return _records.Count;
		}
	}

	public AgentTraceCollector(int capacity = 512)
	{
		if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Trace 容量必须为正数");
		Capacity = capacity;
	}

	/// <summary>追加记录；超过容量时丢弃最旧记录。</summary>
	public override void Record(AgentTraceRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		lock (_gate)
		{
			if (_records.Count == Capacity) _records.Dequeue();
			_records.Enqueue(record);
		}
	}

	/// <summary>获取当前快照，不暴露内部队列。</summary>
	public IReadOnlyList<AgentTraceRecord> Snapshot()
	{
		lock (_gate) return _records.ToArray();
	}
}
