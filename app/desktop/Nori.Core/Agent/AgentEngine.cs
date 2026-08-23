using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Emotion;
using Nori.Core.Memory;
using Nori.Core.Skills;
using Nori.Core.Tools;

namespace Nori.Core.Agent;

/// <summary>
/// LLM 用量与缓存命中指标
/// </summary>
public sealed record AgentUsage(
	int PromptTokens,
	int CompletionTokens,
	int TotalTokens,
	int CachedTokens,
	double CacheHitRate,
	long DurationMs,
	string? Model);

/// <summary>
/// Agent 引擎回调集合
/// </summary>
public sealed class AgentCallbacks
{
	/// <summary>运行状态变化</summary>
	public Action<AgentRunState>? OnState { get; init; }

	/// <summary>流式文本增量 (已解析协议后的可见文本)</summary>
	public Action<string>? OnTextChunk { get; init; }

	/// <summary>工具开始执行</summary>
	public Action<string, JsonNode?>? OnToolExecuting { get; init; }

	/// <summary>工具执行完成</summary>
	public Action<string, object?, string?>? OnToolExecuted { get; init; }

	/// <summary>LLM 用量指标</summary>
	public Action<AgentUsage>? OnUsage { get; init; }

	/// <summary>逐调用工具授权; confirm/dangerous 工具执行前必须经用户批准</summary>
	public Func<ToolApprovalRequest, Task<bool>>? RequestApproval { get; init; }

	/// <summary>最终回复产出 (多轮工具调用后)</summary>
	public Action<ProtocolMessage>? OnComplete { get; init; }
}

/// <summary>
/// Agent 引擎
///
/// 在后端执行完整对话回路: 读取配置与秘密 → 组装人格/记忆/技能/情绪/工具提示词 →
/// 流式 LLM 调用 → 协议解析 → 多轮 Tool Calling → 副本分发与最终落库。
/// 对应前端 services/agent/engine.ts 的职责迁移。
/// </summary>
public sealed class AgentEngine
{
	/// <summary>单轮 LLM 调用超时 (秒), 与 ChatService 上限一致</summary>
	public const int CallTimeoutSeconds = ChatService.TimeoutSeconds;

	private const int MaxContextRounds = 12;
	private readonly int _maxToolIterations;

	private readonly HttpClient _http;
	private readonly ConfigStore _config;
	private readonly ChatService _chat;
	private readonly ToolRegistry _tools;
	private readonly SkillService _skills;
	private readonly EmotionManager _emotion;
	private readonly MemoryService _memory;
	private readonly IPetActions? _pet;
	private readonly Func<IReadOnlyList<string>> _motionNames;
	private readonly Func<IReadOnlyList<string>> _expressionNames;

	private static readonly JsonSerializerOptions JsonOptions = new() {PropertyNamingPolicy = JsonNamingPolicy.CamelCase};

	public AgentEngine(
		HttpClient http,
		ConfigStore config,
		ChatService chat,
		ToolRegistry tools,
		SkillService skills,
		EmotionManager emotion,
		MemoryService memory,
		IPetActions? pet,
		Func<IReadOnlyList<string>> motionNames,
		Func<IReadOnlyList<string>> expressionNames,
		int maxToolIterations = 5)
	{
		_http = http;
		_config = config;
		_chat = chat;
		_tools = tools;
		_skills = skills;
		_emotion = emotion;
		_memory = memory;
		_pet = pet;
		_motionNames = motionNames;
		_expressionNames = expressionNames;
		_maxToolIterations = maxToolIterations;
	}

	/// <summary>
	/// 执行一次 Agent 对话回路
	///
	/// 返回最终文本消息; 会话取消时抛出 OperationCanceledException。
	/// </summary>
	public async Task<ProtocolMessage> RunAsync(string userText, string sessionId, AgentCallbacks callbacks, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(userText)) throw new InvalidOperationException("消息内容不能为空");

		void SetState(AgentRunState state) => callbacks.OnState?.Invoke(state);

		SetState(AgentRunState.Thinking);

		// 1. 读取 AI 与用户自定义人设配置 (秘密只在后端流转)
		string provider = _config.GetStringOr("llm_provider", "openai");
		string baseUrl = _config.GetStringOr("llm_api_base", "").Trim();
		string apiKey = _config.GetStringOr("llm_api_key", "");
		string model = _config.GetStringOr("llm_model", "").Trim();
		string userPersona = _config.GetStringOr("nori_user_persona", "");
		if (baseUrl.Length == 0 || apiKey.Length == 0 || model.Length == 0)
		{
			throw new InvalidOperationException("尚未配置完整的 LLM 参数 (API Base, API Key 或 Model 缺失)");
		}
		LlmProvider providerKind = LlmProviderExtensions.ParseProvider(provider);

		// 2. 组装静态上下文: 最近对话 / 分层记忆 / 情绪 / 动作 / 表情 / 技能 / 工具清单
		IReadOnlyList<(string Role, string Content)> recent = AgentHistory.NormalizeRecent(_chat.GetHistory(MaxContextRounds * 2, 0));
		MemoryContext memoryContext = await _memory.BuildContextAsync(userText, recent, cancellationToken);
		string currentEmotion = _emotion.CurrentType;
		IReadOnlyList<string> motions = _motionNames();
		IReadOnlyList<string> expressions = _expressionNames();
		HashSet<string> availableToolNames = _tools.ListEnabled().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
		string skillsPrompt = _skills.BuildSkillsPrompt(availableToolNames);

		PromptBuildOptions promptOptions = new()
		{
			UserPersona = userPersona,
			Emotion = currentEmotion,
			PersonalMemories = memoryContext.Personal
				.Select(item => item.PersonaSummary ?? item.CanonicalSummary ?? item.Content)
				.Concat(memoryContext.Atoms.Select(atom => atom.Content))
				.Distinct(StringComparer.Ordinal)
				.Take(6)
				.ToList(),
			RelatedKnowledge = memoryContext.Knowledge
				.Where(item => item.Awareness != KnowledgeAwareness.Recovered)
				.Select(item => item.Content).ToList(),
			RecoveredKnowledge = memoryContext.Knowledge
				.Where(item => item.Awareness == KnowledgeAwareness.Recovered)
				.Select(item => item.Content).ToList(),
			MemoryEchoes = memoryContext.Echoes.Select(item => item.Content).ToList(),
			AvailableMotions = motions,
			AvailableExpressions = expressions,
			SkillsPrompt = skillsPrompt,
			ToolsJson = _tools.BuildToolsPrompt(),
		};
		string systemPrompt = PromptBuilder.Build(promptOptions);

		// 3. 准备工作历史: 最近 N 条 + 当前输入 (滑动窗口截断)
		List<(string Role, string Content)> working =
		[.. recent, ("user", userText)];

			ProtocolMessage finalMessage = new("", null, null, null);
		try
		{
			ILlmAdapter adapter = LlmClient.CreateAdapter(providerKind, _http);
			async Task<ToolResult> ExecuteToolAsync(string name, JsonNode? arguments, CancellationToken token)
			{
				callbacks.OnToolExecuting?.Invoke(name, arguments);
				ToolResult result = await _tools.ExecuteAsync(name, arguments, new ToolContext
				{
					SessionId = sessionId,
					CancellationToken = token,
					Approve = callbacks.RequestApproval is { } approve
						? request => approve(request)
						: null,
				});
				callbacks.OnToolExecuted?.Invoke(name, result.Result, result.Error);
				return result;
			}

			for (int iteration = 0; iteration < _maxToolIterations; iteration++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				StreamingJsonParser parser = new();
				string rawResponseText = "";

				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(TimeSpan.FromSeconds(CallTimeoutSeconds));

				SetState(AgentRunState.Streaming);
				Action<string> onChunk = chunk =>
				{
					rawResponseText += chunk;
					foreach (AgentProtocolItem item in SafePush(parser, chunk))
					{
						if (item is ProtocolMessage {Text.Length: > 0} message)
						{
							callbacks.OnTextChunk?.Invoke(message.Text);
						}
					}
				};
				Action<LlmUsageInfo> onUsage = usage => callbacks.OnUsage?.Invoke(new AgentUsage(
					usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, usage.CachedTokens,
					usage.CacheHitRate, usage.DurationMs, usage.Model));
				IReadOnlyList<RegisteredTool> enabledTools = _tools.ListEnabled();
				string raw;
				if (adapter is IToolCallingLlmAdapter toolAdapter)
				{
					try
					{
						raw = await toolAdapter.StreamWithToolsAsync(
							baseUrl.TrimEnd('/'), apiKey, model, systemPrompt,
							[.. working.Select(item => new ChatMessageInput {Role = item.Role, Content = item.Content})],
							enabledTools,
							(name, arguments) => ExecuteToolAsync(name, arguments, timeout.Token),
							onChunk, onUsage, timeout.Token);
					}
					catch (Exception exception) when (IsToolCallingUnavailable(exception))
					{
						// Older OpenAI-compatible endpoints reject the native tools field.
						// Retry once through the portable Nori JSON protocol.
						rawResponseText = "";
						parser = new StreamingJsonParser();
						raw = await adapter.StreamAsync(
							baseUrl.TrimEnd('/'), apiKey, model, systemPrompt,
							[.. working.Select(item => new ChatMessageInput {Role = item.Role, Content = item.Content})],
							onChunk, onUsage, timeout.Token);
					}
				}
				else
				{
					raw = await adapter.StreamAsync(
						baseUrl.TrimEnd('/'), apiKey, model, systemPrompt,
						[.. working.Select(item => new ChatMessageInput {Role = item.Role, Content = item.Content})],
						onChunk, onUsage, timeout.Token);
				}

				cancellationToken.ThrowIfCancellationRequested();
				SetState(AgentRunState.Streaming);

				// 剥离动作标记并触发桌宠播放, 再做完整协议解析
				(string stripped, IReadOnlyList<string> markerMotions) = MotionMarkers.Extract(raw);
				foreach (string motion in markerMotions)
				{
					try
					{
						_pet?.PlayMotionByName(motion);
					}
					catch
					{
						/* 桌宠未加载时忽略 */
					}
				}

				IReadOnlyList<AgentProtocolItem> items = StreamingJsonParser.ParseComplete(stripped);
				bool hasToolCall = false;

				foreach (AgentProtocolItem item in items)
				{
					switch (item)
					{
						// A. 普通消息 (工具调用之后的消息需要等结果反馈，跳过提前定稿)
						case ProtocolMessage message when !hasToolCall:
							finalMessage = message;
							DispatchEffects(message);
							break;

						// B. 工具调用 (同一轮可执行多个工具，全部并入下一轮推理)
						case ProtocolToolCall call:
						{
							hasToolCall = true;
							SetState(AgentRunState.ToolExecuting);
							ToolResult result = await ExecuteToolAsync(call.Name, call.Arguments, cancellationToken);

							working.Add(("assistant", SerializeToolCall(call)));
							working.Add(("user",
								$"【系统工具执行反馈 - {call.Name}】:\n" + JsonSerializer.Serialize(new
								{
									id = call.Id,
									name = call.Name,
									result = result.Result,
									error = result.Error,
								}, JsonOptions)));

							SetState(AgentRunState.Thinking);
							break;
						}
					}
				}

				// 若本轮没有触发新的工具调用，说明已产出最终回复，跳出循环
				if (!hasToolCall) break;
			}

			SetState(AgentRunState.Idle);

			// 落库: 只保存用户可见的最终一轮对话 (纯文本, 不再存协议 JSON)
			if (finalMessage.Text.Length > 0)
			{
				_chat.SaveMessage("user", userText);
				_chat.SaveMessage("assistant", finalMessage.Text);
			}
			callbacks.OnComplete?.Invoke(finalMessage);
			return finalMessage;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// 非用户取消的超时: 转成可读错误而不是当作正常中止
			throw new ChatException($"回复超时 ({CallTimeoutSeconds}s), 请稍后重试");
		}
		catch (OperationCanceledException)
		{
			SetState(AgentRunState.Idle);
			throw;
		}
		catch (Exception)
		{
			SetState(AgentRunState.Error);
			throw;
		}
	}

	/// <summary>分发消息附加的情绪、表情、动作副作用</summary>
	private void DispatchEffects(ProtocolMessage message)
	{
		if (!string.IsNullOrEmpty(message.Emotion))
		{
			try
			{
				_emotion.SetEmotion(message.Emotion);
			}
			catch
			{
				/* 忽略未知情绪 */
			}
		}
		if (_pet is null) return;
		if (!string.IsNullOrEmpty(message.Expression))
		{
			try
			{
				_pet.PlayExpression(message.Expression);
			}
			catch
			{
				/* 表情未匹配时忽略 */
			}
		}
		if (!string.IsNullOrEmpty(message.Action))
		{
			try
			{
				_pet.PlayMotionByName(message.Action);
			}
			catch
			{
				/* 动作未匹配时忽略 */
			}
		}
	}

	/// <summary>序列化 tool_call 为 assistant 反馈上下文</summary>
	private static string SerializeToolCall(ProtocolToolCall call) =>
		JsonSerializer.Serialize(new
		{
			type = "tool_call",
			id = call.Id,
			name = call.Name,
			arguments = call.Arguments,
		}, JsonOptions);

	/// <summary>push 异常隔离: 解析器内部错误不应打断流式回调链</summary>
	private static IReadOnlyList<AgentProtocolItem> SafePush(StreamingJsonParser parser, string chunk)
	{
		try
		{
			return parser.Push(chunk);
		}
		catch
		{
			return [];
		}
	}

	private static bool IsToolCallingUnavailable(Exception exception)
	{
		if (exception is OperationCanceledException) return false;
		if (exception is NotSupportedException) return true;
		string message = exception.Message.ToLowerInvariant();
		return message.Contains("tool", StringComparison.Ordinal)
			|| message.Contains("function", StringComparison.Ordinal)
			|| message.Contains("unsupported", StringComparison.Ordinal);
	}
}

/// <summary>
/// 聊天历史规范化
///
/// 兼容读取旧版前端落库的协议 JSON (assistant 行内嵌 ```json 包裹的协议对象),
/// 过滤历史工具反馈行 —— 前端不再解析历史业务内容。
/// </summary>
public static class AgentHistory
{
	/// <summary>工具反馈行前缀</summary>
	private const string FeedbackPrefix = "【系统工具执行反馈 -";

	/// <summary>
	/// 把存储行规整为 (role, 纯文本) 序列:
	/// assistant 行若为旧版协议 JSON 则提取全部 message 文本。
	/// </summary>
	public static IReadOnlyList<(string Role, string Content)> NormalizeRecent(IReadOnlyList<ChatMessage> rows)
	{
		List<(string, string)> result = [];
		foreach (ChatMessage row in rows)
		{
			if (row.Role == "assistant")
			{
				string text = ExtractDisplayText(row.Content);
				if (text.Length > 0) result.Add(("assistant", text));
				continue;
			}
			if (row.Role == "user" && row.Content.StartsWith(FeedbackPrefix, StringComparison.Ordinal))
			{
				continue;
			}
			result.Add((row.Role, row.Content));
		}
		return result;
	}

	/// <summary>提取一条存储内容的展示文本</summary>
	public static string ExtractDisplayText(string content)
	{
		string trimmed = content.TrimStart();
		if (!trimmed.StartsWith('{') && !trimmed.StartsWith('`'))
		{
			// 已经是纯文本 (新版引擎落库格式)
			return content;
		}
		try
		{
			var items = StreamingJsonParser.ParseComplete(content);
			var texts = items.OfType<ProtocolMessage>()
				.Where(message => message.Text.Length > 0)
				.Select(message => message.Text);
			string joined = string.Join("\n", texts);
			return joined.Length > 0 ? joined : content;
		}
		catch
		{
			return content;
		}
	}
}
