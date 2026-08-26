using System.Text;
using System.Text.Json;

namespace Nori.Core.Automation;

/// <summary>浏览器 DOM 自动化允许的结构化动作种类。</summary>
public enum BrowserAutomationActionKind
{
	/// <summary>导航到受限 HTTP/HTTPS 地址。</summary>
	Navigate,
	/// <summary>点击唯一可见元素。</summary>
	Click,
	/// <summary>填写唯一可见表单元素；必须经过宿主审批。</summary>
	Fill,
	/// <summary>在当前页面滚动有限距离。</summary>
	Scroll,
	/// <summary>等待有限时间。</summary>
	Wait,
	/// <summary>读取受大小限制的可见文本。</summary>
	ReadVisibleText,
}

/// <summary>浏览器结构化动作解析错误。</summary>
public sealed class BrowserAutomationActionValidationException : FormatException
{
	/// <summary>创建解析错误。</summary>
	public BrowserAutomationActionValidationException(string message) : base(message) { }
}

/// <summary>浏览器 DOM 自动化动作基类；不包含脚本、文件或权限提升能力。</summary>
public abstract record BrowserAutomationAction
{
	/// <summary>动作种类。</summary>
	public abstract BrowserAutomationActionKind Kind { get; }
}

/// <summary>导航动作。</summary>
public sealed record BrowserNavigateAction(string Url) : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.Navigate;
}

/// <summary>元素点击动作。</summary>
public sealed record BrowserClickAction(string Selector) : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.Click;
}

/// <summary>表单填写动作；文本只在内存执行链中传递。</summary>
public sealed record BrowserFillAction(string Selector, string Text) : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.Fill;
}

/// <summary>页面滚动动作。</summary>
public sealed record BrowserScrollAction(int Pixels) : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.Scroll;
}

/// <summary>有限等待动作。</summary>
public sealed record BrowserWaitAction(int Milliseconds) : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.Wait;
}

/// <summary>读取页面可见文本的动作。</summary>
public sealed record BrowserReadVisibleTextAction : BrowserAutomationAction
{
	/// <inheritdoc />
	public override BrowserAutomationActionKind Kind => BrowserAutomationActionKind.ReadVisibleText;
}

/// <summary>浏览器任务的不可放宽边界。</summary>
public static class BrowserAutomationTaskLimits
{
	/// <summary>单个任务最多动作数。</summary>
	public const int MaxActions = 20;

	/// <summary>单个任务最多持续时间。</summary>
	public static TimeSpan MaximumDuration { get; } = TimeSpan.FromSeconds(120);

	/// <summary>可见文本结果的最大 UTF-8 字节数。</summary>
	public const int MaxVisibleTextBytes = 32 * 1024;

	/// <summary>旧字符上限名称的兼容别名；新实现按 UTF-8 字节而非字符数限制。</summary>
	public const int MaxVisibleTextCharacters = MaxVisibleTextBytes;

	/// <summary>按 UTF-8 字节边界截断文本，绝不拆开 UTF-16 代理项对。</summary>
	public static string TruncateVisibleText(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (Encoding.UTF8.GetByteCount(value) <= MaxVisibleTextBytes) return value;

		byte[] buffer = new byte[MaxVisibleTextBytes];
		Encoder encoder = Encoding.UTF8.GetEncoder();
		encoder.Convert(value.AsSpan(), buffer.AsSpan(), flush: true, out int charactersUsed, out _, out _);
		if (charactersUsed > 0 && char.IsHighSurrogate(value[charactersUsed - 1])) charactersUsed--;
		return value[..charactersUsed];
	}
}

/// <summary>已解析的浏览器动作计划；只保留当前内存执行所需数据。</summary>
public sealed class BrowserAutomationTaskPlan
{
	private readonly BrowserAutomationAction[] _actions;

	/// <summary>创建受动作数限制的计划。</summary>
	public BrowserAutomationTaskPlan(IEnumerable<BrowserAutomationAction> actions)
	{
		ArgumentNullException.ThrowIfNull(actions);
		_actions = actions.ToArray();
		if (_actions.Length is < 1 or > BrowserAutomationTaskLimits.MaxActions)
			throw new BrowserAutomationActionValidationException($"浏览器任务动作数必须在 1 到 {BrowserAutomationTaskLimits.MaxActions} 之间");
		if (_actions.Any(action => action is null)) throw new BrowserAutomationActionValidationException("浏览器任务包含空动作");
	}

	/// <summary>动作列表。</summary>
	public IReadOnlyList<BrowserAutomationAction> Actions => _actions;

	/// <summary>从桥接 JSON 解析严格白名单动作。</summary>
	public static BrowserAutomationTaskPlan Parse(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Array) throw new BrowserAutomationActionValidationException("浏览器任务 actions 必须是数组");
		List<BrowserAutomationAction> actions = [];
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (actions.Count == BrowserAutomationTaskLimits.MaxActions)
				throw new BrowserAutomationActionValidationException($"浏览器任务最多允许 {BrowserAutomationTaskLimits.MaxActions} 个动作");
			actions.Add(ParseAction(item));
		}
		return new BrowserAutomationTaskPlan(actions);
	}

	private static BrowserAutomationAction ParseAction(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object) throw new BrowserAutomationActionValidationException("浏览器动作必须是对象");
		string type = RequiredString(value, "type");
		return type switch
		{
			"navigate" => new BrowserNavigateAction(RequiredString(value, "url")),
			"click" => new BrowserClickAction(RequiredString(value, "selector")),
			"fill" => new BrowserFillAction(RequiredString(value, "selector"), RequiredString(value, "text")),
			"scroll" => new BrowserScrollAction(RequiredInt(value, "pixels")),
			"wait" => new BrowserWaitAction(RequiredInt(value, "milliseconds")),
			"read_visible_text" or "read-visible-text" => new BrowserReadVisibleTextAction(),
			_ => throw new BrowserAutomationActionValidationException("浏览器动作类型不在白名单内"),
		};
	}

	private static string RequiredString(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
			throw new BrowserAutomationActionValidationException($"浏览器动作缺少字符串字段: {name}");
		string? text = property.GetString();
		if (text is null) throw new BrowserAutomationActionValidationException($"浏览器动作字段无效: {name}");
		return text;
	}

	private static int RequiredInt(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int number))
			throw new BrowserAutomationActionValidationException($"浏览器动作缺少整数字段: {name}");
		return number;
	}
}

/// <summary>浏览器任务向宿主报告的脱敏进度状态。</summary>
public enum BrowserAutomationProgressState
{
	/// <summary>任务正在执行。</summary>
	Running,
	/// <summary>填写动作等待用户审批。</summary>
	AwaitingApproval,
	/// <summary>一个动作已经完成。</summary>
	ActionSucceeded,
	/// <summary>安全页面触发暂停。</summary>
	Paused,
}

/// <summary>浏览器任务脱敏进度；不包含 URL、选择器、文本或页面内容。</summary>
public sealed record BrowserAutomationProgress(
	int Step,
	BrowserAutomationActionKind? ActionKind,
	BrowserAutomationProgressState State,
	Guid? ApprovalRequestId = null,
	string? PauseReason = null);

/// <summary>浏览器执行上下文；由宿主提供审批、入口复核和脱敏进度投影。</summary>
public sealed class BrowserAutomationExecutionContext
{
	/// <summary>创建执行上下文。</summary>
	public BrowserAutomationExecutionContext(
		Guid taskId,
		AutomationApprovalCallback? approvalCallback = null,
		Func<CancellationToken, Task>? ensureExecutionAllowedAsync = null,
		Action<BrowserAutomationProgress>? progress = null)
	{
		if (taskId == Guid.Empty) throw new ArgumentException("任务标识不能为空", nameof(taskId));
		TaskId = taskId;
		ApprovalCallback = approvalCallback;
		EnsureExecutionAllowedAsync = ensureExecutionAllowedAsync;
		Progress = progress;
	}

	/// <summary>任务标识。</summary>
	public Guid TaskId { get; }

	/// <summary>填写动作使用的宿主审批回调。</summary>
	public AutomationApprovalCallback? ApprovalCallback { get; }

	/// <summary>每个动作前复核安全模式、平台和开关。</summary>
	public Func<CancellationToken, Task>? EnsureExecutionAllowedAsync { get; }

	/// <summary>脱敏进度通知。</summary>
	public Action<BrowserAutomationProgress>? Progress { get; }

	/// <summary>复核当前执行入口。</summary>
	public Task EnsureExecutionAllowedAsyncCore(CancellationToken cancellationToken) =>
		EnsureExecutionAllowedAsync?.Invoke(cancellationToken) ?? Task.CompletedTask;

	/// <summary>报告脱敏进度。</summary>
	public void Report(BrowserAutomationProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);
		Progress?.Invoke(progress);
	}
}

/// <summary>浏览器会话的受限内存执行结果。</summary>
public sealed record BrowserAutomationExecutionResult(
	bool Succeeded,
	int CompletedActions,
	string? VisibleText,
	string? FailureCode = null)
{
	/// <summary>创建成功结果并按 UTF-8 字节上限截断文本。</summary>
	public static BrowserAutomationExecutionResult Completed(int completedActions, string? visibleText) =>
		new(true, Math.Max(0, completedActions), visibleText is null ? null : BrowserAutomationTaskLimits.TruncateVisibleText(visibleText));

	/// <summary>创建不携带页面内容的失败结果。</summary>
	public static BrowserAutomationExecutionResult Failed(int completedActions, string failureCode) =>
		new(false, Math.Max(0, completedActions), null, string.IsNullOrWhiteSpace(failureCode) ? "execution_failed" : failureCode);
}

/// <summary>可供桥接按短期读取的内存浏览器结果。</summary>
public sealed record BrowserAutomationTaskResult(
	Guid TaskId,
	bool Succeeded,
	string? VisibleText,
	string? FailureCode,
	DateTimeOffset FinishedAt);

/// <summary>浏览器任务结果的短期内存仓储；绝不写入数据库或文件。</summary>
public sealed class BrowserAutomationResultStore
{
	/// <summary>最多同时保留的结果数。</summary>
	public const int MaximumResults = 32;

	/// <summary>结果读取有效期。</summary>
	public static TimeSpan ResultTtl { get; } = TimeSpan.FromMinutes(5);

	private readonly object _gate = new();
	private readonly Dictionary<Guid, Entry> _entries = [];
	private readonly TimeProvider _timeProvider;

	/// <summary>创建短期结果仓储。</summary>
	public BrowserAutomationResultStore(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

	/// <summary>保存一个受大小限制的结果。</summary>
	public void Set(BrowserAutomationTaskResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.TaskId == Guid.Empty) throw new ArgumentException("任务标识不能为空", nameof(result));
		BrowserAutomationTaskResult bounded = result with
		{
			VisibleText = result.VisibleText is null ? null : BrowserAutomationTaskLimits.TruncateVisibleText(result.VisibleText),
			FailureCode = string.IsNullOrWhiteSpace(result.FailureCode) ? null : result.FailureCode,
		};
		lock (_gate)
		{
			Prune(_timeProvider.GetUtcNow());
			_entries[bounded.TaskId] = new(bounded, _timeProvider.GetUtcNow() + ResultTtl);
			while (_entries.Count > MaximumResults)
			{
				Guid oldest = _entries.OrderBy(pair => pair.Value.ExpiresAt).First().Key;
				_entries.Remove(oldest);
			}
		}
	}

	/// <summary>读取尚未过期的结果。</summary>
	public BrowserAutomationTaskResult? Get(Guid taskId)
	{
		if (taskId == Guid.Empty) return null;
		lock (_gate)
		{
			Prune(_timeProvider.GetUtcNow());
			return _entries.TryGetValue(taskId, out Entry? entry) ? entry.Result : null;
		}
	}

	/// <summary>删除一个任务的结果。</summary>
	public void Remove(Guid taskId)
	{
		lock (_gate) _entries.Remove(taskId);
	}

	private void Prune(DateTimeOffset now)
	{
		foreach (Guid key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
			_entries.Remove(key);
	}

	private sealed record Entry(BrowserAutomationTaskResult Result, DateTimeOffset ExpiresAt);
}
