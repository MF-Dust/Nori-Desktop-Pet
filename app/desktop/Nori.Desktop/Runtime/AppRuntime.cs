using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Nori.Core.Agent;
using Nori.Core.Configuration;
using Nori.Core.Emotion;
using Nori.Core.Logging;
using Nori.Core.Live2D;
using Nori.Core.Memory;
using Nori.Core.Mcp;
using Nori.Core.Network;
using Nori.Core.Proactive;
using Nori.Core.Skills;
using Nori.Core.Tools;
using Nori.Core.Telemetry;
using Nori.Core.Voice;
using Nori.Desktop.Audio;
using Nori.Desktop.Bridge;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Runtime;

/// <summary>
/// 应用运行时协调层
///
/// 承接前端迁移过来的全部业务编排: Agent 会话与取消、工具授权、技能/情绪/提醒/
/// 记忆/语音服务装配, 以及面向 WebView 的带版本号 UI 状态快照。
///
/// 事件出口约定:
/// - nori:agent-event   → 仅推送给发起会话的窗口 (状态/chunk/用量/授权/完成/错误)
/// - nori:state-changed → 全局广播 (快照版本 + 变更主题)
/// - nori:proactive-message / nori:stt-result / nori:voice-notice → 对应窗口或全局
///
/// 秘密纪律: 快照只返回 hasApiKey 等脱敏标记, 明文绝不回传事件/日志/错误。
/// </summary>
public sealed class AppRuntime : IAsyncDisposable
{
	/// <summary>工具授权等待超时 (秒); 超时一律 fail-closed 拒绝</summary>
	public const int ApprovalTimeoutSeconds = 60;

	private readonly ConcurrentDictionary<string, AgentSessionState> _sessions = new();
	private readonly ConcurrentDictionary<string, PendingApproval> _approvals = new();
	private readonly ConcurrentDictionary<Task, byte> _backgroundTasks = new();
	private readonly CancellationTokenSource _lifetimeCts = new();
	private readonly WebViewAudioPlayback _playback;
	private readonly WebViewMicrophoneRecorder _recorder;
	private readonly AudioHostChannel _audioChannel;
	private readonly ReflectionQueue _reflectionQueue;
	private readonly ReflectionWorker _reflectionWorker;
	private readonly PetInteractionReactionService _petInteractionService;
	private readonly SemaphoreSlim _petInteractionGate = new(1, 1);
	private readonly Lock _petInteractionThrottleGate = new();
	private readonly Lock _petSpeechGate = new();
	private CancellationTokenSource? _petInteractionCts;
	private CancellationTokenSource? _petSpeechCts;
	private PetInteractionTrigger? _activePetInteractionTrigger;
	private bool _activePetInteractionFallbackPosted;
	private DateTimeOffset _lastPetInteractionAt = DateTimeOffset.MinValue;
	private bool _petInteractionSubscribed;
	private int _disposed;

	public AppServices Services { get; }

	public ToolRegistry Tools { get; }

	public SkillService Skills { get; }

	public EmotionManager Emotion { get; }

	public ProactiveScheduler Proactive { get; }

	public MemoryService Memory { get; }

	public KnowledgeService Knowledge { get; }

	public MemoryLifecycleService Lifecycle { get; }

	public VoiceService Voice { get; }

	public AgentEngine Engine { get; }

	/// <summary>桌宠轻量互动 LLM 服务, 不进入聊天历史和工具链。</summary>
	public PetInteractionReactionService PetInteraction => _petInteractionService;

	/// <summary>当前快照版本号 (每次状态变更递增)</summary>
	public int SnapshotVersion => Volatile.Read(ref _snapshotVersion);

	private int _snapshotVersion = 1;

	private int _initStartPending;

	/// <summary>
	/// 托盘是否真的可用
	///
	/// 由 App 在装载托盘后回填; 不可用时前端在主窗内显示常驻入口与退出按钮。
	/// </summary>
	public bool TrayAvailable { get; set; } = true;

	/// <summary>
	/// 标记“初始化开始”已发生
	///
	/// 首启路径下 init 窗口隐藏启动, 向导完成时广播的 nori:init-start 有可能早于
	/// init 页面订阅 (WebView 加载比广播慢), 事件就会永久丢失 —— 页面卡在转圈.
	/// 因此额外留一个标志供页面就绪时回放.
	/// </summary>
	public void MarkInitStartPending() => Interlocked.Exchange(ref _initStartPending, 1);

	/// <summary>取走并清除“初始化开始”标志 (只能被消费一次)</summary>
	public bool ConsumeInitStartPending() => Interlocked.Exchange(ref _initStartPending, 0) == 1;

	public AppRuntime(AppServices services)
	{
		Services = services;
		ConfigStore config = services.Config;

		Memory = new MemoryService(services.Memory, services.Embedding, config);
		Knowledge = new KnowledgeService(services.Database, Memory, config);
		Knowledge.StatusChanged = () => InvalidateSnapshot("memory");
		Memory.Knowledge = Knowledge;
		Lifecycle = new MemoryLifecycleService(Memory);
		_reflectionQueue = new ReflectionQueue();
		ReflectionService reflection = new(services.Http, services.Chat, Memory, config);
		_reflectionWorker = new ReflectionWorker(_reflectionQueue, reflection, exception =>
		{
			try { services.Logger.Write(LogSource.Backend, "warn", $"记忆整理失败: {exception.Message}"); }
			catch { }
		}, () => InvalidateSnapshot("memory"));
		Skills = new SkillService(config, services.PublicHttp);
		Emotion = new EmotionManager(config);

		ReminderStore reminderStore = new(services.Database);
		Proactive = new ProactiveScheduler(
			reminderStore, config, services.Logger,
			GetIdleSecondsSafe);

		// 音频与录音下沉到 main 窗口的 WebAudio / MediaRecorder: 三平台一套代码, 不再依赖 NAudio
		MediaExchange media = services.Assets?.Media ?? new MediaExchange();
		Func<string, string> mediaUrl = services.Assets is {} assets
			? assets.MediaUrl
			: _ => throw new InvalidOperationException("资源服务未启动, 音频端点不可用");
		AudioHostChannel channel = new(() => services.Windows?.GetNoriWindow(WindowLabels.Main));
		WebViewAudioPlayback playback = new(media, mediaUrl, channel);
		WebViewMicrophoneRecorder recorder = new(media, mediaUrl, channel);
		_playback = playback;
		_recorder = recorder;
		_audioChannel = channel;
		Voice = new VoiceService(services.Http, config, playback, () => VoiceRetired() ? null : recorder);
		_petInteractionService = new PetInteractionReactionService(services.Http, config);

		Tools = BuildToolRegistry(true);
		Engine = new AgentEngine(
			services.Http,
			config,
			services.Chat,
			Tools,
			Skills,
			Emotion,
			Memory,
			pet: new PetActionsAdapter(() => services.PetRuntime),
			motionNames: () => FlattenMotionNames(),
			expressionNames: () => services.PetRuntime?.Expressions ?? []);

		// 窗口显隐变化 (含托盘切换桌宠) 直接作废快照, 主界面的桌宠状态因此不会陈旧
		if (services.Windows is not null)
		{
			services.Windows.VisibilityChanged += (label, visible) =>
			{
				if (label == WindowLabels.Pet && !visible)
				{
					CancelPetInteractionRequest();
					CancelPetInteractionSpeech();
				}
				InvalidateSnapshot(label == WindowLabels.Pet ? "pet" : "windows");
			};
		}
	}

	// ===================================================================
	// 启动装配
	// ===================================================================

	/// <summary>启动各子系统并接线事件</summary>
	public void Start()
	{
		Emotion.Initialize();
		if (!_petInteractionSubscribed && Services.PetRuntime is not null)
		{
			Services.PetRuntime.InteractionTriggered += OnPetInteractionTriggered;
			Services.PetRuntime.ModelLoadRequested += CancelPetInteractionRequest;
			Services.PetRuntime.ModelLoadRequested += CancelPetInteractionPresentation;
			_petInteractionSubscribed = true;
		}
		Emotion.ExpressionRequested += expression =>
		{
			try
			{
				Services.PetRuntime?.PlayExpression(expression);
			}
			catch
			{
				/* 表情未匹配时忽略 */
			}
		};

		// 回放持久化的工具禁用清单
		if (Services.Config.Get("tools_disabled") is ConfigValue.Json {Value: JsonNode node})
		{
			try
			{
				List<string>? names = node.Deserialize<List<string>>(BridgeJson.Options);
				if (names is {Count: > 0}) Tools.RestoreDisabled(names);
			}
			catch
			{
				/* 清单损坏时忽略 */
			}
		}

		Proactive.Message += message => Dispatcher.UIThread.Post(() => OnProactiveMessage(message));
		Proactive.Start();

		// Knowledge 和 Reflection 都在后台启动；索引或整理失败不能阻塞聊天。
		_reflectionWorker.Start();
		_reflectionWorker.TryEnqueue(new ReflectionJob("startup"));
		TrackBackground(InitializeKnowledgeAsync, "Memory.md index");
		TrackBackground(() => Memory.ReembedAllAsync(_lifetimeCts.Token, false), "memory embedding rebuild");
		TrackBackground(RunMemoryMaintenanceAsync, "memory lifecycle");

		// 口型同步: 前端回传的播放音量采样直驱原生桌宠嘴型
		_playback.VolumeSampled += level =>
		{
			try
			{
				Services.PetRuntime?.SetMouthOpen((float)level, true);
			}
			catch
			{
				/* 桌宠未加载时忽略 */
			}
		};
		_playback.PlayingChanged += playing =>
		{
			try
			{
				Services.PetRuntime?.SetMouthOpen(0, playing);
			}
			catch
			{
				/* 桌宠未加载时忽略 */
			}
		};
		Voice.SpeakingChanged += _ => InvalidateSnapshot("voice");

		Voice.VolumeChanged += volume => _playback.SetDeviceVolume(volume);
		_playback.SetDeviceVolume(Voice.GetVolume());

		DetectLegacyVoiceConfig();
		TrackBackground(() => RefreshMcpToolsAsync(), "MCP tools refresh");

		InvalidateSnapshot("all");
	}

	/// <summary>安全获取系统空闲秒数 (非 Windows 返回 null)</summary>
	private static double? GetIdleSecondsSafe()
	{
		if (!OperatingSystem.IsWindows()) return null;
		try
		{
			return SystemIdleTime.GetIdleSeconds();
		}
		catch
		{
			return null;
		}
	}

	private void DetectLegacyVoiceConfig()
	{
		if (!Voice.HasRetiredVoiceConfig()) return;
		string flagged = Services.Config.GetStringOr("voice_notice_pending", "");
		if (flagged.Length > 0) return; // 已提示过或已处理
		Services.Config.Set("voice_notice_pending", new ConfigValue.Text("1"));
	}

	private bool VoiceRetired() => VoiceService.RetiredProviders.Contains(Services.Config.GetStringOr("stt_provider", ""));

	private void OnProactiveMessage(ProactiveMessage message)
	{
		try
		{
			Services.PetRuntime?.PlayMotionByName(message.Motion);
			Services.PetRuntime?.PlayExpression(message.Expression);
		}
		catch
		{
			/* 桌宠未加载时忽略 */
		}
		BroadcastEvent("nori:proactive-message", new {text = message.Text});
		bool autoTts = ParseBoolFlag(Services.Config.GetStringOr("tts_auto_play", "")) ?? false;
		if (autoTts)
		{
			_ = SpeakSafelyAsync(message.Text);
		}
	}

	private void CancelPetInteractionRequest() => CancelPetInteractionRequest(false);

	/// <summary>取消当前桌宠 AI 请求；聊天抢占时只补发一次本地兜底。</summary>
	private void CancelPetInteractionRequest(bool applyLocalFallback)
	{
		CancellationTokenSource? requestCts;
		PetInteractionTrigger? fallback = null;
		lock (_petInteractionThrottleGate)
		{
			requestCts = _petInteractionCts;
			if (applyLocalFallback
				&& !_activePetInteractionFallbackPosted
				&& _activePetInteractionTrigger is { } trigger)
			{
				_activePetInteractionFallbackPosted = true;
				fallback = trigger;
			}
		}
		try { requestCts?.Cancel(); }
		catch (ObjectDisposedException) { }
		if (fallback is not null) PostPetInteractionFallback(fallback);
	}

	private void OnPetInteractionTriggered(PetInteractionTrigger trigger)
	{
		if (Volatile.Read(ref _disposed) != 0) return;
		if (!IsPetInteractionAiEnabled() || !IsLlmConfigured() || _sessions.Count > 0)
		{
			PostPetInteractionFallback(trigger);
			return;
		}
		if (!_petInteractionGate.Wait(0))
		{
			PostPetInteractionFallback(trigger);
			return;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		lock (_petInteractionThrottleGate)
		{
			if (now - _lastPetInteractionAt < TimeSpan.FromSeconds(3))
			{
				_petInteractionGate.Release();
				PostPetInteractionFallback(trigger);
				return;
			}
			_lastPetInteractionAt = now;
		}

		Task task = RunPetInteractionAsync(trigger);
		TrackTask(task);
	}

	private async Task RunPetInteractionAsync(PetInteractionTrigger trigger)
	{
		using CancellationTokenSource requestCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
		lock (_petInteractionThrottleGate)
		{
			_petInteractionCts = requestCts;
			_activePetInteractionTrigger = trigger;
			_activePetInteractionFallbackPosted = false;
		}
		try
		{
			PetInteractionReactionRequest request = new()
			{
				ModelId = trigger.ModelId,
				RegionId = trigger.Hit.Region.Id,
				RegionName = trigger.Hit.Region.Name,
				ModelX = trigger.Hit.ModelX,
				ModelY = trigger.Hit.ModelY,
				RegionX = trigger.Hit.RegionX,
				RegionY = trigger.Hit.RegionY,
				CurrentEmotion = Emotion.CurrentType,
				AvailableMotions = Services.PetRuntime.MotionGroups
					.Select(group => new MotionGroupInfo {Group = group.Group, Names = [.. group.Names]})
					.ToArray(),
				AvailableExpressions = Services.PetRuntime.Expressions.ToArray(),
			};
			PetInteractionReaction reaction = await _petInteractionService.ReactAsync(request, requestCts.Token).ConfigureAwait(false);
			if (requestCts.IsCancellationRequested || !IsCurrentPetInteraction(trigger)) return;
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				if (!requestCts.IsCancellationRequested) ApplyPetInteractionReaction(trigger, reaction);
			});
		}
		catch (OperationCanceledException) when (requestCts.IsCancellationRequested || _lifetimeCts.IsCancellationRequested)
		{
			// 应用退出、模型切换、隐藏或聊天抢占时取消，不显示错误也不应用旧结果。
		}
		catch (Exception exception)
		{
			try { Services.Logger.Write(LogSource.Backend, "warn", $"桌宠 AI 互动失败: {exception.Message}"); } catch { }
			PostActivePetInteractionFallback(trigger, requestCts);
		}
		finally
		{
			lock (_petInteractionThrottleGate)
			{
				if (ReferenceEquals(_petInteractionCts, requestCts))
				{
					_petInteractionCts = null;
					_activePetInteractionTrigger = null;
					_activePetInteractionFallbackPosted = false;
				}
			}
			_petInteractionGate.Release();
		}
	}

	private void ApplyPetInteractionReaction(PetInteractionTrigger trigger, PetInteractionReaction reaction)
	{
		if (!IsCurrentPetInteraction(trigger)) return;
		if (!string.IsNullOrWhiteSpace(reaction.Emotion) && EmotionTypes.IsValid(reaction.Emotion))
		{
			try { Emotion.SetEmotion(reaction.Emotion); } catch { }
		}
		if (!string.IsNullOrWhiteSpace(reaction.Motion)) Services.PetRuntime.PlayMotionByName(reaction.Motion);
		if (!string.IsNullOrWhiteSpace(reaction.Expression)) Services.PetRuntime.PlayExpression(reaction.Expression);
		if (string.IsNullOrWhiteSpace(reaction.Text)) return;
		Services.Windows.ShowPetSpeech(reaction.Text);
		bool autoTts = ParseBoolFlag(Services.Config.GetStringOr("tts_auto_play", "")) ?? false;
		if (autoTts) StartPetInteractionSpeech(reaction.Text);
	}

	private void PostActivePetInteractionFallback(PetInteractionTrigger trigger, CancellationTokenSource requestCts)
	{
		bool shouldPost = false;
		lock (_petInteractionThrottleGate)
		{
			if (ReferenceEquals(_petInteractionCts, requestCts) && !_activePetInteractionFallbackPosted)
			{
				_activePetInteractionFallbackPosted = true;
				shouldPost = true;
			}
		}
		if (shouldPost) PostPetInteractionFallback(trigger);
	}

	private void PostPetInteractionFallback(PetInteractionTrigger trigger)
	{
		Dispatcher.UIThread.Post(() =>
		{
			if (IsCurrentPetInteraction(trigger)) Services.PetRuntime.ApplyLocalInteraction(trigger.Hit.Region);
		});
	}

	private bool IsCurrentPetInteraction(PetInteractionTrigger trigger) =>
		Services.Windows.IsWindowVisible(WindowLabels.Pet)
		&& Services.PetRuntime.CurrentModelId.Equals(trigger.ModelId, StringComparison.OrdinalIgnoreCase)
		&& Services.PetRuntime.ModelGeneration == trigger.ModelGeneration;

	private bool IsPetInteractionAiEnabled() =>
		ParseBoolFlag(Services.Config.GetStringOr(PetInteractionConfig.AiEnabledKey, "")) ?? false;

	private bool IsLlmConfigured() =>
		Services.Config.GetStringOr("llm_api_base", "").Trim().Length > 0
		&& Services.Config.GetStringOr("llm_api_key", "").Length > 0
		&& Services.Config.GetStringOr("llm_model", "").Trim().Length > 0;

	private void StartPetInteractionSpeech(string text)
	{
		CancelPetInteractionSpeech();
		CancellationTokenSource speechCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
		lock (_petSpeechGate) _petSpeechCts = speechCts;
		TrackTask(SpeakPetInteractionSafelyAsync(text, speechCts));
	}

	private void CancelPetInteractionPresentation()
	{
		CancelPetInteractionSpeech();
		Dispatcher.UIThread.Post(Services.Windows.ClearPetSpeech);
	}

	private void CancelPetInteractionSpeech()
	{
		CancellationTokenSource? speechCts;
		lock (_petSpeechGate)
		{
			speechCts = _petSpeechCts;
			_petSpeechCts = null;
		}
		try { speechCts?.Cancel(); }
		catch (ObjectDisposedException) { }
	}

	private async Task SpeakPetInteractionSafelyAsync(string text, CancellationTokenSource speechCts)
	{
		try
		{
			await Voice.SpeakAsync(text, null, speechCts.Token);
		}
		catch (OperationCanceledException) when (speechCts.IsCancellationRequested)
		{
			// 隐藏、切换模型、开始聊天或退出时取消，不作为播放失败。
		}
		catch (Exception exception)
		{
			try { Services.Logger.Write(LogSource.Backend, "warn", $"桌宠互动朗读失败: {exception.Message}"); } catch { }
		}
		finally
		{
			lock (_petSpeechGate)
			{
				if (ReferenceEquals(_petSpeechCts, speechCts)) _petSpeechCts = null;
			}
			speechCts.Dispose();
		}
	}

	private async Task SpeakSafelyAsync(string text)
	{
		try
		{
			await Voice.SpeakAsync(text);
		}
		catch (Exception exception)
		{
			try
			{
				Services.Logger.Write(LogSource.Backend, "warn", $"主动朗读失败: {exception.Message}");
			}
			catch
			{
				// 日志失败保持静默
			}
		}
	}

	/// <summary>
	/// 同步已连接 MCP 工具到 Agent 注册表。
	/// 每个动态工具默认 confirm, 由 AgentRuntime 的逐调用授权链路 fail-closed 控制。
	/// </summary>
	public async Task RefreshMcpToolsAsync(CancellationToken cancellationToken = default)
	{
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken);
		CancellationToken ct = linkedCts.Token;
		ct.ThrowIfCancellationRequested();
		foreach (RegisteredTool tool in Tools.List().Where(tool => tool.Category == "mcp"))
		{
			Tools.Unregister(tool.Name);
		}

		IReadOnlyList<McpServerStatusInfo> servers = await Services.Mcp.GetServersAsync();
		ct.ThrowIfCancellationRequested();
		foreach (McpServerStatusInfo server in servers.Where(server => server.Status == "connected"))
		{
			foreach (McpToolDefinition definition in server.Tools)
			{
				string serverId = server.ServerId;
				string toolName = definition.Name;
				string fullName = $"mcp__{serverId}__{toolName}";
				JsonObject schema = definition.InputSchema?.DeepClone() as JsonObject ?? new JsonObject
				{
					["type"] = "object",
					["properties"] = new JsonObject(),
				};

				Tools.Register(new RegisteredTool
				{
					Name = fullName,
					Description = $"[{server.Name}] {definition.Description ?? toolName}",
					Parameters = schema,
					PermissionLevel = "confirm",
					Category = "mcp",
					Execute = async (arguments, context) =>
					{
						JsonObject? objectArguments = arguments as JsonObject;
						McpToolResult result = await Services.Mcp.CallToolAsync(serverId, toolName, objectArguments, context.CancellationToken);
						if (result.IsError) throw new InvalidOperationException(result.AsText());
						return result.AsText();
					},
				});
			}
		}
	}

	private ToolRegistry BuildToolRegistry(bool audioAvailable)
	{
		ToolRegistry registry = new();
		BuiltinTools.RegisterAll(registry, new BuiltinToolDeps
		{
			Memory = Memory,
			Emotion = Emotion,
			Proactive = Proactive,
			Pet = new PetActionsAdapter(() => Services.PetRuntime),
			Clipboard = audioAvailable ? new AvaloniaClipboardOps(() => Services.Windows.Get(WindowLabels.Main)) : null,
			SystemInfo = new DesktopSystemInfo(Services.Config),
			Fetcher = new WebPageFetcher(Services.PublicHttp),
			Http = Services.PublicHttp,
			Config = Services.Config,
			OpenUrl = url => ShellOpen.OpenUrl(url),
		});
		return registry;
	}

	private IReadOnlyList<string> FlattenMotionNames()
	{
		IReadOnlyList<Core.Live2D.MotionGroupInfo>? groups = Services.PetRuntime?.MotionGroups;
		if (groups is null || groups.Count == 0) return [];
		return groups.SelectMany(group => group.Names).Distinct().ToList();
	}

	// ===================================================================
	// 聊天会话
	// ===================================================================

	/// <summary>
	/// 启动一次 Agent 会话; 返回 sessionId 供取消/授权关联
	/// </summary>
	public string StartChat(IBridgeSource source, string text)
	{
		if (Volatile.Read(ref _disposed) != 0) throw new InvalidOperationException("Application is shutting down");
		string sessionId = $"agent-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Interlocked.Increment(ref _sessionCounter):x}";
		AgentSessionState session = new(source.Label);
		_sessions[sessionId] = session;
		// 聊天请求优先于桌宠轻量请求与其语音；旧 AI 请求改走该区域的本地兜底。
		CancelPetInteractionRequest(true);
		CancelPetInteractionPresentation();

		AgentCallbacks callbacks = new()
		{
			OnState = state => PostAgentEvent(session.SourceLabel, new {type = "state", sessionId, state = state.ToString().ToLowerInvariant()}),
			OnTextChunk = chunk => PostAgentEvent(session.SourceLabel, new {type = "chunk", sessionId, chunk}),
			OnToolExecuting = (name, args) => PostAgentEvent(session.SourceLabel, new {type = "tool-executing", sessionId, toolName = name, arguments = args}),
			OnToolExecuted = (name, result, error) => PostAgentEvent(session.SourceLabel, new
			{
				type = "tool-executed",
				sessionId,
				toolName = name,
				result = ToJsonNode(result),
				success = error is null,
				error,
			}),
			OnUsage = usage => PostAgentEvent(session.SourceLabel, new
			{
				type = "usage",
				sessionId,
				promptTokens = usage.PromptTokens,
				completionTokens = usage.CompletionTokens,
				totalTokens = usage.TotalTokens,
				cachedTokens = usage.CachedTokens,
				cacheHitRate = usage.CacheHitRate,
				durationMs = usage.DurationMs,
				model = usage.Model,
			}),
			RequestApproval = request => RequestApprovalAsync(session, sessionId, request),
			OnComplete = _ =>
			{
				/* complete 事件在 RunAsync 正常返回后统一发出 */
			},
		};

		Task worker = Task.Run(async () =>
		{
			using ITelemetryTransaction operation = Services.Telemetry.StartTransaction("agent.run");
			try
			{
				await RefreshMcpToolsAsync(session.Cts.Token);
				ProtocolMessage final = await Engine.RunAsync(text, sessionId, callbacks, session.Cts.Token);
				_reflectionWorker.TryEnqueue(new ReflectionJob("chat"));
				PostAgentEvent(session.SourceLabel, new
				{
					type = "complete",
					sessionId,
					message = new
					{
						text = final.Text,
						emotion = final.Emotion,
						expression = final.Expression,
						action = final.Action,
					},
				});

				await AutoSpeakAsync(final.Text, session.SourceLabel, session.Cts.Token);
			}
			catch (OperationCanceledException)
			{
				PostAgentEvent(session.SourceLabel, new {type = "cancelled", sessionId});
			}
			catch (Exception exception)
			{
				PostAgentEvent(session.SourceLabel, new {type = "error", sessionId, error = exception.Message});
			}
			finally
			{
				_sessions.TryRemove(sessionId, out _);
				session.Dispose();
			}
		});
		session.Worker = worker;
		TrackTask(worker);

		return sessionId;
	}

	private int _sessionCounter;

	private async Task AutoSpeakAsync(string text, string sourceLabel, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		bool autoTts = ParseBoolFlag(Services.Config.GetStringOr("tts_auto_play", "")) ?? false;
		if (!autoTts) return;

		PostAgentEvent(sourceLabel, new {type = "state", state = AgentRunState.Speaking.ToString().ToLowerInvariant()});
		try
		{
			await Voice.SpeakAsync(text, null, ct);
		}
		catch
		{
			/* 自动朗读失败不阻断完成事件 */
		}
		finally
		{
			PostAgentEvent(sourceLabel, new {type = "state", state = AgentRunState.Idle.ToString().ToLowerInvariant()});
		}
	}

	/// <summary>取消指定来源窗口的会话</summary>
	public bool CancelChat(string sourceLabel, string sessionId)
	{
		if (!_sessions.TryGetValue(sessionId, out AgentSessionState? session) || session.SourceLabel != sourceLabel)
		{
			return false;
		}
		session.Cts.Cancel();
		// 取消所有该会话挂起的授权 (fail-closed)
		foreach ((string requestId, PendingApproval approval) in _approvals)
		{
			if (approval.SessionId != sessionId) continue;
			if (_approvals.TryRemove(new KeyValuePair<string, PendingApproval>(requestId, approval)))
			{
				approval.Tcs.TrySetResult(false);
				approval.Dispose();
				PostAgentEvent(approval.SourceLabel, new {type = "approval-result", sessionId, requestId, approved = false, reason = "cancelled"});
			}
		}
		return true;
	}

	/// <summary>会话是否仍在运行</summary>
	public bool IsSessionActive(string sessionId) => _sessions.ContainsKey(sessionId);

	// ===================================================================
	// 工具授权
	// ===================================================================

	private async Task<bool> RequestApprovalAsync(AgentSessionState session, string sessionId, ToolApprovalRequest request)
	{
		TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		PendingApproval approval = new(request.RequestId, session.SourceLabel, sessionId, tcs);

		if (!_approvals.TryAdd(request.RequestId, approval))
		{
			return false;
		}

		approval.ArmTimeout(ApprovalTimeoutSeconds, () =>
		{
			if (_approvals.TryRemove(request.RequestId, out PendingApproval? expired))
			{
				expired.Tcs.TrySetResult(false);
				expired.Dispose();
				PostAgentEvent(expired.SourceLabel, new {type = "approval-result", sessionId, requestId = request.RequestId, approved = false, reason = "timeout"});
			}
		});

		PostAgentEvent(session.SourceLabel, new
		{
			type = "approval-request",
			sessionId,
			requestId = request.RequestId,
			toolName = request.ToolName,
			arguments = request.Arguments,
			description = request.Description,
			permissionLevel = request.PermissionLevel,
			category = request.Category,
		});

		return await tcs.Task;
	}

	/// <summary>
	/// 回传授权决定; 只允许原始窗口响应, 未匹配的请求 fail-closed 忽略
	/// </summary>
	public bool RespondApproval(string sourceLabel, string requestId, bool approved)
	{
		if (!_approvals.TryGetValue(requestId, out PendingApproval? approval) || approval.SourceLabel != sourceLabel)
		{
			return false;
		}
		if (!_approvals.TryRemove(new KeyValuePair<string, PendingApproval>(requestId, approval))) return false;
		approval.Dispose(); // 停掉超时定时器
		PostAgentEvent(approval.SourceLabel, new
		{
			type = "approval-result",
			sessionId = approval.SessionId,
			requestId,
			approved,
			reason = approved ? "approved" : "denied",
		});
		return approval.Tcs.TrySetResult(approved);
	}

	// ===================================================================
	// 前端音频宿主回报
	// ===================================================================

	/// <summary>前端回报一段音频播放结束 (或失败)</summary>
	public void ReportPlaybackFinished(string token, string? error) =>
		_playback.ReportPlaybackFinished(token, error);

	/// <summary>前端回报实时播放音量 (0~1), 驱动桌宠口型</summary>
	public void ReportAudioLevel(double level) => _playback.ReportLevel(level);

	/// <summary>前端 main WebView 完成监听器安装后的就绪握手。</summary>
	public void MarkAudioHostReady() => _audioChannel.MarkReady();

	/// <summary>前端回报 MediaRecorder 已获权并开始。</summary>
	public void ReportRecordingReady(string token) => _recorder.ReportRecordingReady(token);

	/// <summary>前端回报麦克风权限、录音或上传失败。</summary>
	public void ReportRecordingFailed(string token, string? error) => _recorder.ReportRecordingFailed(token, error);

	// ===================================================================
	// UI 状态快照
	// ===================================================================

	/// <summary>使快照失效并广播变更主题</summary>
	public void InvalidateSnapshot(params string[] topics)
	{
		Interlocked.Increment(ref _snapshotVersion);
		BroadcastEvent("nori:state-changed", new {version = SnapshotVersion, topics});
	}

	/// <summary>构建脱敏 UI 状态快照</summary>
	public object BuildSnapshot(IBridgeSource source)
	{
		ConfigStore config = Services.Config;
		string provider = config.GetStringOr("llm_provider", "openai");
		string baseUrl = config.GetStringOr("llm_api_base", "");
		string model = config.GetStringOr("llm_model", "");
		string persona = config.GetStringOr("nori_user_persona", "");
		bool hasApiKey = config.GetStringOr("llm_api_key", "").Length > 0;

		var models = ModelCatalogIds().Select(id => new
		{
			id,
			installed = IsModelInstalled(id),
		});

		string selectedModel = config.GetStringOr("selected_model", ConfigStore.DefaultModel);
		float modelOpacity = ReadFloat(config, $"l2d_opacity_{selectedModel}") ?? ReadFloat(config, "l2d_opacity") ?? 1.0f;
		float modelRenderScale = ReadFloat(config, $"l2d_render_scale_{selectedModel}") ?? ReadFloat(config, "l2d_render_scale") ?? 2.0f;
		bool modelShadow = ParseBoolFlag(ReadModelString(config, "l2d_shadow", selectedModel, "true")) ?? true;
		string modelQualityMode = ReadModelString(config, "l2d_quality_mode", selectedModel, "adaptive");
		int modelMaxFps = (int)(ReadFloat(config, $"l2d_max_fps_{selectedModel}") ?? ReadFloat(config, "l2d_max_fps") ?? 0);
		Live2DRenderSettings modelRenderSettings = Live2DRenderSettings.Normalize(
			selectedModel, modelOpacity, modelShadow, modelRenderScale, modelQualityMode, modelMaxFps);

		Nori.Core.Memory.MemorySettings memorySettings = Memory.Settings;
		(int activeMemories, int atomCount, int archivedMemories, int totalMemories) = Memory.GetOverview();
		Nori.Core.Memory.MemoryIndexStatus memoryIndex = Knowledge.Status;

		return new
		{
			version = SnapshotVersion,
			app = new {appVersion = config.GetStringOr("app_version", "0.1.0"), platform = PlatformOsName()},
			general = new
			{
				language = config.GetStringOr("language", "zh-CN"),
				petAutoSummon = ParseBoolFlag(config.GetStringOr("pet_auto_summon", "true")) ?? true,
				sidebarCollapsed = ParseBoolFlag(config.GetStringOr("ui_sidebar_collapsed", "")) ?? false,
			},
			telemetry = new
			{
				enabled = config.GetBoolOr(ConfigStore.KeyTelemetryEnabled, true),
				available = Services.Telemetry.IsAvailable,
			},
			ai = new
			{
				configured = baseUrl.Length > 0 && hasApiKey && model.Length > 0,
				provider,
				baseUrl,
				model,
				persona,
				hasApiKey,
			},
			models = new
			{
				selected = selectedModel,
				items = models,
				scale = ReadFloat(config, $"l2d_scale_{selectedModel}") ?? ReadFloat(config, "l2d_scale") ?? 1.0,
				expressions = ModelExpressions(selectedModel),
			},
			pet = new
			{
				visible = Services.Windows.IsWindowVisible(WindowLabels.Pet),
				renderMetrics = Services.PetRuntime?.RenderMetrics,
			},
			platform = new
			{
				os = PlatformOsName(),
				sessionType = Nori.Core.Platform.PlatformServices.Current.Session.ToString().ToLowerInvariant(),
				supportsGlobalCursor = Nori.Core.Platform.PlatformServices.Current.Capabilities.SupportsGlobalCursor,
				supportsWindowDrag = Nori.Core.Platform.PlatformServices.Current.Capabilities.SupportsWindowDrag,
				supportsHitThrough = Nori.Core.Platform.PlatformServices.Current.Capabilities.SupportsHitThrough,
				supportsTopmost = Nori.Core.Platform.PlatformServices.Current.Capabilities.SupportsTopmost,
				supportsTray = Nori.Core.Platform.PlatformServices.Current.Capabilities.SupportsTray && TrayAvailable,
			},
			behaviors = new
			{
				clickInteraction = ParseBoolFlag(config.GetStringOr("l2d_click_interaction", "true")) ?? true,
				autoBlink = ParseBoolFlag(config.GetStringOr("l2d_auto_blink", "true")) ?? true,
				eyeTracking = ParseBoolFlag(config.GetStringOr("l2d_eye_tracking", "true")) ?? true,
				idleEyeAnimation = ParseBoolFlag(config.GetStringOr("l2d_idle_eye_animation", "true")) ?? true,
				idleAnimation = ParseBoolFlag(config.GetStringOr("l2d_idle_animation", "true")) ?? true,
				expressionEnabled = ParseBoolFlag(config.GetStringOr("l2d_expression_enabled", "true")) ?? true,
				lipSync = ParseBoolFlag(config.GetStringOr("l2d_lip_sync", "true")) ?? true,
				shadow = modelRenderSettings.ShadowEnabled,
				beatSync = ParseBoolFlag(config.GetStringOr("l2d_beat_sync", "")) ?? false,
				aiInteraction = ParseBoolFlag(config.GetStringOr(PetInteractionConfig.AiEnabledKey, "")) ?? false,
				opacity = modelRenderSettings.Opacity,
				renderScale = modelRenderSettings.RenderScale,
				qualityMode = Live2DRenderSettings.QualityModeToStorage(modelRenderSettings.QualityMode),
				maxFps = modelRenderSettings.MaxFps,
			},
			memory = new
			{
				enabled = memorySettings.Enabled,
				reflectionEnabled = memorySettings.ReflectionEnabled,
				decayEnabled = memorySettings.DecayEnabled,
				archiveEnabled = memorySettings.ArchiveEnabled,
				active = activeMemories,
				atoms = atomCount,
				archived = archivedMemories,
				total = totalMemories,
				knowledgePath = Knowledge.Path,
				knowledgeChunks = Knowledge.Status.Total,
				indexState = memoryIndex.State.ToString().ToLowerInvariant(),
				indexProcessed = memoryIndex.Processed,
				indexTotal = memoryIndex.Total,
				lastError = memoryIndex.LastError,
				lastReflection = Memory.Store.GetEngineState("last_reflection_at"),
				lastMaintenance = Memory.Store.GetEngineState("last_maintenance_at"),
				ftsAvailable = Services.Memory.IsFtsAvailable,
				reflectionRounds = memorySettings.ReflectionRounds,
				reflectionMinChars = memorySettings.ReflectionMinChars,
				recallTopK = memorySettings.RecallTopK,
				keywordTopK = memorySettings.KeywordTopK,
				vectorTopK = memorySettings.VectorTopK,
				rrfK = memorySettings.RrfK,
				minSimilarity = memorySettings.MinSimilarity,
				sourceRetentionThreshold = memorySettings.SourceRetentionThreshold,
				archiveThreshold = memorySettings.ArchiveThreshold,
				knowledgeEnabled = memorySettings.KnowledgeEnabled,
				knowledgeWatch = memorySettings.KnowledgeWatch,
				debugRetrieval = memorySettings.DebugRetrieval,
			},
			voice = new
			{
				volume = Voice.GetVolume(),
				ttsProvider = Voice.ResolveProviderName(),
				ttsBaseUrl = config.GetStringOr("tts_base_url", ""),
				hasTtsApiKey = config.GetStringOr("tts_api_key", "").Length > 0,
				ttsVoice = config.GetStringOr("tts_voice", ""),
				ttsSpeed = ReadFloat(config, "tts_speed") ?? 1.0,
				ttsAutoPlay = ParseBoolFlag(config.GetStringOr("tts_auto_play", "true")) ?? true,
				gptsovitsBaseUrl = config.GetStringOr("gptsovits_base_url", "http://127.0.0.1:9880"),
				gptsovitsRefAudio = config.GetStringOr("gptsovits_ref_audio", ""),
				gptsovitsPromptText = config.GetStringOr("gptsovits_prompt_text", ""),
				gptsovitsPromptLang = config.GetStringOr("gptsovits_prompt_lang", "zh"),
				sttProvider = config.GetStringOr("stt_provider", "whisper"),
				sttBaseUrl = config.GetStringOr("stt_base_url", ""),
				hasSttApiKey = config.GetStringOr("stt_api_key", "").Length > 0,
				noticePending = config.GetStringOr("voice_notice_pending", "") == "1",
				speaking = Voice.IsSpeaking,
			},
			embedding = new
			{
				model = config.GetStringOr("embedding_model", "BAAI/bge-m3"),
				baseUrl = config.GetStringOr("embedding_api_base", ""),
				dimensions = config.GetStringOr("embedding_dimensions", ""),
				hasApiKey = config.GetStringOr("embedding_api_key", "").Length > 0,
			},
			proactive = new
			{
				idleEnabled = ParseBoolFlag(config.GetStringOr("proactive_idle_enabled", "true")) ?? true,
				idleMinutes = (int)(ReadFloat(config, "proactive_idle_minutes") ?? ProactiveScheduler.DefaultIdleMinutes),
				dailyGreeting = ParseBoolFlag(config.GetStringOr("proactive_daily_greeting", "true")) ?? true,
				reminders = Proactive.ListReminders().Select(item => new
				{
					id = item.Id, content = item.Content, triggerTime = item.TriggerAt,
				}),
			},
			skills = Skills.GetInstalled().Select(skill => new
			{
				id = skill.Id, name = skill.Name, description = skill.Description, author = skill.Author,
				version = skill.Version, icon = skill.Icon, tags = skill.Tags, category = skill.Category,
				instructions = "", // 详情按需 skills_export 获取, 避免快照膨胀
				enabled = skill.Enabled, source = skill.Source,
			}),
			enabledSkillsCount = Skills.GetEnabled().Count,
			tools = Tools.List().Select(tool => new
			{
				name = tool.Name, description = tool.Description,
				permissionLevel = tool.PermissionLevel, category = tool.Category, enabled = tool.Enabled,
			}),
			mcpServersCount = McpServerCount(),
			emotion = new {type = Emotion.CurrentType},
		};
	}

	/// <summary>当前操作系统名 (前端按它决定平台相关文案)</summary>
	private static string PlatformOsName()
	{
		if (OperatingSystem.IsWindows()) return "windows";
		if (OperatingSystem.IsMacOS()) return "macos";
		if (OperatingSystem.IsLinux()) return "linux";
		return "unknown";
	}

	/// <summary>已知模型目录 (展示名由前端静态目录映射)</summary>
	private static IReadOnlyList<string> ModelCatalogIds() => ["arg-nori", "nori"];

	private IReadOnlyList<string> ModelExpressions(string modelId)
	{
		try
		{
			string dir = Services.Resources.ResourceDir(Nori.Core.Resources.ResourceType.Live2D, modelId);
			return Core.Live2D.Model3Meta.Read(dir).Expressions;
		}
		catch
		{
			return [];
		}
	}

	private bool IsModelInstalled(string modelId)
	{
		try
		{
			return Services.Resources.IsInstalled(Nori.Core.Resources.ResourceType.Live2D, modelId);
		}
		catch
		{
			return false;
		}
	}

	private int McpServerCount()
	{
		try
		{
			return Services.Mcp.GetServersAsync().GetAwaiter().GetResult().Count;
		}
		catch
		{
			return 0;
		}
	}

	// ===================================================================
	// 事件出口
	// ===================================================================

	/// <summary>向指定窗口推送 Agent 事件</summary>
	private void PostAgentEvent(string label, object payload)
	{
		if (Volatile.Read(ref _disposed) != 0) return;
		try { Services.Windows.GetNoriWindow(label)?.PostEvent(AgentEventName, payload); }
		catch { /* windows may already be closing */ }
	}

	/// <summary>向所有 WebView 窗口广播</summary>
	private void BroadcastEvent(string name, object payload)
	{
		if (Volatile.Read(ref _disposed) != 0) return;
		Dispatcher.UIThread.Post(() =>
		{
			if (Volatile.Read(ref _disposed) == 0)
			{
				try { Services.Windows.Broadcast(name, payload); }
				catch { /* windows may already be closing */ }
			}
		});
	}

	/// <summary>Agent 事件通道名</summary>
	public const string AgentEventName = "nori:agent-event";

	private static JsonNode? ToJsonNode(object? value)
	{
		if (value is null) return null;
		try
		{
			return JsonSerializerNode(value);
		}
		catch
		{
			return System.Text.Json.Nodes.JsonValue.Create(value.ToString());
		}
	}

	private static System.Text.Json.Nodes.JsonNode? JsonSerializerNode(object value)
	{
		string json = System.Text.Json.JsonSerializer.Serialize(value, BridgeJson.Options);
		return System.Text.Json.Nodes.JsonNode.Parse(json);
	}

	private static bool? ParseBoolFlag(string raw) => raw switch
	{
		"1" => true,
		"0" => false,
		_ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
		_ when raw.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
		_ => null,
	};

	private static string ReadModelString(ConfigStore config, string baseKey, string modelId, string fallback)
	{
		string modelValue = config.GetStringOr($"{baseKey}_{modelId}", "");
		return modelValue.Length > 0 ? modelValue : config.GetStringOr(baseKey, fallback);
	}

	private static float? ReadFloat(ConfigStore config, string key)
	{
		string raw = config.GetStringOr(key, "");
		if (raw.Length == 0) return null;
		if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1f;
		if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0f;
		return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : null;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_lifetimeCts.Cancel();
		if (_petInteractionSubscribed && Services.PetRuntime is not null)
		{
			Services.PetRuntime.InteractionTriggered -= OnPetInteractionTriggered;
			Services.PetRuntime.ModelLoadRequested -= CancelPetInteractionRequest;
			Services.PetRuntime.ModelLoadRequested -= CancelPetInteractionPresentation;
			_petInteractionSubscribed = false;
		}
		CancelPetInteractionRequest();
		CancelPetInteractionSpeech();

		foreach ((string _, AgentSessionState session) in _sessions)
		{
			session.Cts.Cancel();
		}

		foreach ((string _, PendingApproval approval) in _approvals)
		{
			approval.Tcs.TrySetResult(false);
			approval.Dispose();
		}
		_approvals.Clear();

		Task[] workers = _sessions.Values.Select(session => session.Worker).OfType<Task>().ToArray();
		await WaitBoundedAsync(workers, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		foreach (AgentSessionState session in _sessions.Values) session.Dispose();
		_sessions.Clear();
		await WaitBoundedAsync(_backgroundTasks.Keys.ToArray(), TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		_backgroundTasks.Clear();

		try { await _reflectionWorker.DisposeAsync().ConfigureAwait(false); } catch { }
		try { await Knowledge.DisposeAsync().ConfigureAwait(false); } catch { }
		try { Proactive.Dispose(); } catch { }
		try { Emotion.Dispose(); } catch { }
		// Voice.Dispose 会逆向释放 _playback; 录音票据要单独作废
		try { _recorder.Dispose(); } catch { }
		try { Voice.Dispose(); } catch { }
		_petInteractionGate.Dispose();
		_lifetimeCts.Dispose();
	}

	private void TrackTask(Task task)
	{
		_backgroundTasks.TryAdd(task, 0);
		_ = task.ContinueWith(
			completed => _backgroundTasks.TryRemove(completed, out _),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private Task TrackBackground(Func<Task> operation, string name)
	{
		Task task = ObserveBackgroundAsync(operation, name);
		TrackTask(task);
		return task;
	}

	private async Task InitializeKnowledgeAsync()
	{
		Knowledge.EnsureDefaultFile();
		Knowledge.StartWatcher();
		await Knowledge.ReindexAsync(_lifetimeCts.Token).ConfigureAwait(false);
	}

	/// <summary>Embedding 配置变化后重新检查知识和个人记忆向量。</summary>
	public void QueueEmbeddingRebuild()
	{
		TrackBackground(() => Knowledge.ReindexAsync(_lifetimeCts.Token), "Memory.md embedding rebuild");
		TrackBackground(() => Memory.ReembedAllAsync(_lifetimeCts.Token, false), "memory embedding rebuild");
		InvalidateSnapshot("memory", "embedding");
	}

	private async Task RunMemoryMaintenanceAsync()
	{
		while (!_lifetimeCts.IsCancellationRequested)
		{
			int changed = Lifecycle.RunOnce();
			if (changed > 0) InvalidateSnapshot("memory");
			try { await Task.Delay(TimeSpan.FromHours(6), _lifetimeCts.Token).ConfigureAwait(false); }
			catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { break; }
		}
	}

	private async Task ObserveBackgroundAsync(Func<Task> operation, string name)
	{
		try { await operation().ConfigureAwait(false); }
		catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { }
		catch (Exception exception)
		{
			try
			{
				Services.Telemetry.CaptureException(exception, "runtime.background_task");
				Services.Logger.Write(LogSource.Backend, "warn", $"{name} failed: {exception.Message}");
			}
			catch { }
		}
	}

	private static async Task WaitBoundedAsync(IReadOnlyCollection<Task> tasks, TimeSpan timeout)
	{
		if (tasks.Count == 0) return;
		Task all = Task.WhenAll(tasks);
		await Task.WhenAny(all, Task.Delay(timeout)).ConfigureAwait(false);
	}

	/// <summary>活动 Agent 会话状态</summary>
	private sealed class AgentSessionState(string sourceLabel) : IDisposable
	{
		public string SourceLabel { get; } = sourceLabel;

		public CancellationTokenSource Cts { get; } = new();

		public Task? Worker { get; set; }

		public void Dispose()
		{
			Cts.Dispose();
		}
	}

	/// <summary>待决授权请求</summary>
	private sealed class PendingApproval(string requestId, string sourceLabel, string sessionId, TaskCompletionSource<bool> tcs) : IDisposable
	{
		public string RequestId { get; } = requestId;
		public string SourceLabel { get; } = sourceLabel;
		public string SessionId { get; } = sessionId;
		public TaskCompletionSource<bool> Tcs { get; } = tcs;

		private System.Threading.Timer? _timeout;

		/// <summary>启动超时定时器; 触发时执行回调 (fail-closed)</summary>
		public void ArmTimeout(int seconds, Action onExpired)
		{
			_timeout = new System.Threading.Timer(_ => onExpired(), null, seconds * 1000, Timeout.Infinite);
		}

		public void Dispose()
		{
			_timeout?.Dispose();
			_timeout = null;
		}
	}
}
