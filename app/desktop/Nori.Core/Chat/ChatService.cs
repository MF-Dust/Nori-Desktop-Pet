using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Nori.Core.Configuration;
using Nori.Core.Data;

namespace Nori.Core.Chat;

/// <summary>
/// 聊天消息 (输入)
/// 前端: {role: "user" | "assistant", content: "..."}
/// </summary>
public sealed record ChatMessageInput
{
	/// <summary>角色: user / assistant</summary>
	public required string Role { get; init; }

	/// <summary>消息内容</summary>
	public required string Content { get; init; }
}

/// <summary>
/// 聊天消息 (存储 / 输出)
/// 前端: {id, role, content, createdAt}
/// </summary>
public sealed record ChatMessage
{
	/// <summary>自增 id (即时间顺序)</summary>
	public required long Id { get; init; }

	/// <summary>角色</summary>
	public required string Role { get; init; }

	/// <summary>内容</summary>
	public required string Content { get; init; }

	/// <summary>创建时间 (RFC3339)</summary>
	public required string CreatedAt { get; init; }
}

/// <summary>
/// 聊天服务
///
/// 对应 Rust 版 chat.rs. 系统提示词以嵌入资源形式编译进程序集,
/// 与原来的 include_str! 一样 —— 改了 nori-system-prompt.md 必须重新构建才生效.
/// </summary>
public sealed class ChatService(HttpClient httpClient, NoriDatabase database, ConfigStore config)
{
	/// <summary>配置键: LLM 协议类型</summary>
	public const string KeyLlmProvider = "llm_provider";

	/// <summary>聊天请求超时 (秒): 防止接口挂起导致后台任务永久阻塞</summary>
	private const int TimeoutSeconds = 120;

	/// <summary>嵌入资源名</summary>
	private const string PromptResource = "Nori.Core.Chat.nori-system-prompt.md";

	private static readonly Lazy<string> SystemPrompt = new(LoadSystemPrompt);

	private readonly HttpClient _httpClient = httpClient;
	private readonly NoriDatabase _database = database;
	private readonly ConfigStore _config = config;

	/// <summary>
	/// 获取完整聊天历史 (按时间正序, 永不清除)
	/// </summary>
	public IReadOnlyList<ChatMessage> GetHistory() => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT id, role, content, created_at FROM chat_messages ORDER BY id ASC";
		using SqliteDataReader reader = command.ExecuteReader();
		List<ChatMessage> messages = [];
		while (reader.Read())
		{
			messages.Add(new ChatMessage
			{
				Id = reader.GetInt64(0),
				Role = reader.GetString(1),
				Content = reader.GetString(2),
				CreatedAt = reader.GetString(3),
			});
		}
		return (IReadOnlyList<ChatMessage>)messages;
	});

	/// <summary>
	/// 发起一次对话
	///
	/// 返回剥离动作标记后的回复文本; 动作名通过 onMotion 回调交给调用方广播
	/// </summary>
	public async Task<string> CompleteAsync(
		string? providerStr,
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onMotion,
		CancellationToken cancellationToken = default)
	{
		baseUrl = baseUrl.TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");
		if (apiKey.Length == 0) throw new ChatException("API Key 不能为空");
		if (model.Length == 0) throw new ChatException("模型不能为空");
		if (messages.Count == 0) throw new ChatException("消息不能为空");

		// 若未指定 providerStr，优先读配置
		if (string.IsNullOrWhiteSpace(providerStr))
		{
			providerStr = _config.GetStringOr(KeyLlmProvider, "openai");
		}

		LlmProvider provider = LlmProviderExtensions.ParseProvider(providerStr);
		ILlmAdapter adapter = LlmClient.CreateAdapter(provider, _httpClient);

		// 系统提示词 = 人格 + 当前模型动作列表附录
		string modelId = _config.GetStringOr(ConfigStore.KeySelectedModel, "");
		string systemContent = SystemPrompt.Value + MotionMarkers.BuildHint(_config, modelId);

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

		string raw = await adapter.CompleteAsync(baseUrl, apiKey, model, systemContent, messages, timeout.Token);

		// 解析动作标记: 剥离标记并广播给桌宠窗口播放
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract(raw);
		foreach (string motion in motions) onMotion(motion);

		// 写入历史: 仅保存最后一条输入与回复, 避免重复落库
		if (messages.Count > 0)
		{
			SaveMessage(messages[^1].Role, messages[^1].Content);
		}
		SaveMessage("assistant", content);
		return content;
	}

	/// <summary>
	/// 发起一次流式对话
	///
	/// 逐 chunk 触发 onChunk 回调, 并在结束时返回剥离动作标记后的完整回复文本
	/// </summary>
	public async Task<string> StreamAsync(
		string? providerStr,
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onChunk,
		Action<string> onMotion,
		Action<LlmUsageInfo>? onUsage = null,
		CancellationToken cancellationToken = default)
	{
		baseUrl = baseUrl.TrimEnd('/');
		if (baseUrl.Length == 0) throw new ChatException("Base URL 不能为空");
		if (apiKey.Length == 0) throw new ChatException("API Key 不能为空");
		if (model.Length == 0) throw new ChatException("模型不能为空");
		if (messages.Count == 0) throw new ChatException("消息不能为空");

		if (string.IsNullOrWhiteSpace(providerStr))
		{
			providerStr = _config.GetStringOr(KeyLlmProvider, "openai");
		}

		LlmProvider provider = LlmProviderExtensions.ParseProvider(providerStr);
		ILlmAdapter adapter = LlmClient.CreateAdapter(provider, _httpClient);

		string modelId = _config.GetStringOr(ConfigStore.KeySelectedModel, "");
		string systemContent = SystemPrompt.Value + MotionMarkers.BuildHint(_config, modelId);

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

		string raw = await adapter.StreamAsync(baseUrl, apiKey, model, systemContent, messages, onChunk, onUsage, timeout.Token);

		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract(raw);
		foreach (string motion in motions) onMotion(motion);

		if (messages.Count > 0)
		{
			SaveMessage(messages[^1].Role, messages[^1].Content);
		}
		SaveMessage("assistant", content);
		return content;
	}

	/// <summary>
	/// 兼容老接口 (从配置或默认协议发起对话)
	/// </summary>
	public Task<string> CompleteAsync(
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<ChatMessageInput> messages,
		Action<string> onMotion,
		CancellationToken cancellationToken = default)
	{
		return CompleteAsync(null, baseUrl, apiKey, model, messages, onMotion, cancellationToken);
	}

	/// <summary>
	/// 保存一条聊天消息
	/// </summary>
	public void SaveMessage(string role, string content) => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "INSERT INTO chat_messages (role, content, created_at) VALUES ($role, $content, $createdAt)";
		command.Parameters.AddWithValue("$role", role);
		command.Parameters.AddWithValue("$content", content);
		command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
		command.ExecuteNonQuery();
	});

	/// <summary>
	/// 清空全部聊天历史
	/// </summary>
	public void ClearHistory() => _database.Locked(connection =>
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "DELETE FROM chat_messages;";
		command.ExecuteNonQuery();
	});

	/// <summary>
	/// 从嵌入资源读取系统提示词
	/// </summary>
	private static string LoadSystemPrompt()
	{
		using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResource)
			?? throw new InvalidOperationException($"找不到嵌入资源: {PromptResource}");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}

/// <summary>
/// 聊天相关错误, 消息直接展示给用户
/// </summary>
public sealed class ChatException(string message, Exception? inner = null) : Exception(message, inner);
