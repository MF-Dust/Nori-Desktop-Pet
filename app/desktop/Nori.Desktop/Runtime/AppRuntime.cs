using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Nori.Core.Agent;
using Nori.Core.Configuration;
using Nori.Core.Emotion;
using Nori.Core.Logging;
using Nori.Core.Memory;
using Nori.Core.Mcp;
using Nori.Core.Network;
using Nori.Core.Proactive;
using Nori.Core.Skills;
using Nori.Core.Tools;
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
	private readonly NativeAudioPlayback? _playback;

	public AppServices Services { get; }

	public ToolRegistry Tools { get; }

	public SkillService Skills { get; }

	public EmotionManager Emotion { get; }

	public ProactiveScheduler Proactive { get; }

	public MemoryService Memory { get; }

	public VoiceService Voice { get; }

	public AgentEngine Engine { get; }

	/// <summary>当前快照版本号 (每次状态变更递增)</summary>
	public int SnapshotVersion => Volatile.Read(ref _snapshotVersion);

	private int _snapshotVersion = 1;

	public AppRuntime(AppServices services)
	{
		Services = services;
		ConfigStore config = services.Config;

		Memory = new MemoryService(services.Memory, services.Embedding, config);
		Skills = new SkillService(config, services.PublicHttp);
		Emotion = new EmotionManager(config);

		ReminderStore reminderStore = new(services.Database);
		Proactive = new ProactiveScheduler(
			reminderStore, config, services.Logger,
			GetIdleSecondsSafe);

		NativeAudioPlayback? playback = OperatingSystem.IsWindows() ? new NativeAudioPlayback() : null;
		_playback = playback;
		Voice = new VoiceService(services.Http, config, playback, () =>
			OperatingSystem.IsWindows() && !VoiceRetired() ? new NativeMicrophoneRecorder() : null);

		Tools = BuildToolRegistry(playback is not null);
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
	}

	// ===================================================================
	// 启动装配
	// ===================================================================

	/// <summary>启动各子系统并接线事件</summary>
	public void Start()
	{
		Emotion.Initialize();
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

		// 口型同步: 播放音量采样直驱原生桌宠嘴型
		if (_playback is not null)
		{
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
		}

		if (_playback is not null)
		{
			Voice.VolumeChanged += v => _playback.SetDeviceVolume(v);
			_playback.SetDeviceVolume(Voice.GetVolume());
		}

		DetectLegacyVoiceConfig();
		_ = RefreshMcpToolsAsync();
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
	public async Task RefreshMcpToolsAsync()
	{
		foreach (RegisteredTool tool in Tools.List().Where(tool => tool.Category == "mcp"))
		{
			Tools.Unregister(tool.Name);
		}

		IReadOnlyList<McpServerStatusInfo> servers = await Services.Mcp.GetServersAsync();
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
		string sessionId = $"agent-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Interlocked.Increment(ref _sessionCounter):x}";
		AgentSessionState session = new(source.Label);
		_sessions[sessionId] = session;

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

		_ = Task.Run(async () =>
		{
			try
			{
				await RefreshMcpToolsAsync();
				ProtocolMessage final = await Engine.RunAsync(text, sessionId, callbacks, session.Cts.Token);
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

		return new
		{
			version = SnapshotVersion,
			app = new {appVersion = config.GetStringOr("app_version", "0.1.0"), platform = "windows"},
			general = new
			{
				language = config.GetStringOr("language", "zh-CN"),
				petAutoSummon = ParseBoolFlag(config.GetStringOr("pet_auto_summon", "true")) ?? true,
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
			behaviors = new
			{
				clickInteraction = ParseBoolFlag(config.GetStringOr("l2d_click_interaction", "true")) ?? true,
				autoBlink = ParseBoolFlag(config.GetStringOr("l2d_auto_blink", "true")) ?? true,
				eyeTracking = ParseBoolFlag(config.GetStringOr("l2d_eye_tracking", "true")) ?? true,
				idleEyeAnimation = ParseBoolFlag(config.GetStringOr("l2d_idle_eye_animation", "true")) ?? true,
				idleAnimation = ParseBoolFlag(config.GetStringOr("l2d_idle_animation", "true")) ?? true,
				expressionEnabled = ParseBoolFlag(config.GetStringOr("l2d_expression_enabled", "true")) ?? true,
				lipSync = ParseBoolFlag(config.GetStringOr("l2d_lip_sync", "true")) ?? true,
				shadow = ParseBoolFlag(config.GetStringOr("l2d_shadow", "true")) ?? true,
				beatSync = ParseBoolFlag(config.GetStringOr("l2d_beat_sync", "")) ?? false,
				renderScale = ReadFloat(config, "l2d_render_scale") ?? 2.0,
				maxFps = (int)(ReadFloat(config, "l2d_max_fps") ?? 0),
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
		Services.Windows.GetNoriWindow(label)?.PostEvent(AgentEventName, payload);
	}

	/// <summary>向所有 WebView 窗口广播</summary>
	private void BroadcastEvent(string name, object payload)
	{
		Dispatcher.UIThread.Post(() => Services.Windows.Broadcast(name, payload));
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

	private static float? ReadFloat(ConfigStore config, string key)
	{
		string raw = config.GetStringOr(key, "");
		if (raw.Length == 0) return null;
		if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1f;
		if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0f;
		return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : null;
	}

	public ValueTask DisposeAsync()
	{
		foreach ((string _, AgentSessionState session) in _sessions)
		{
			session.Cts.Cancel();
			session.Dispose();
		}
		_sessions.Clear();

		foreach ((string _, PendingApproval approval) in _approvals)
		{
			approval.Tcs.TrySetResult(false);
			approval.Dispose();
		}
		_approvals.Clear();

		Proactive.Dispose();
		Emotion.Dispose();
		Voice.Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>活动 Agent 会话状态</summary>
	private sealed class AgentSessionState(string sourceLabel) : IDisposable
	{
		public string SourceLabel { get; } = sourceLabel;

		public CancellationTokenSource Cts { get; } = new();

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
