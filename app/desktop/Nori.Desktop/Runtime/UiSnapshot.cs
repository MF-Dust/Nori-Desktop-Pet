using System.Text.Json.Serialization;
using Nori.Core.Mcp;

namespace Nori.Desktop.Runtime;

/// <summary>
/// UI 脱敏状态快照。
///
/// 这是原生设置窗口与 WebView runtime 共用的只读投影；秘密字段只保留是否存在的标记。
/// </summary>
public sealed record UiSnapshot
{
	[JsonPropertyName("version")]
	public int Version { get; init; }

	[JsonPropertyName("app")]
	public required AppSnapshot App { get; init; }

	[JsonPropertyName("general")]
	public required GeneralSnapshot General { get; init; }

	[JsonPropertyName("ai")]
	public required AiSnapshot Ai { get; init; }

	[JsonPropertyName("models")]
	public required ModelsSnapshot Models { get; init; }

	[JsonPropertyName("behaviors")]
	public required BehaviorsSnapshot Behaviors { get; init; }

	[JsonPropertyName("voice")]
	public required VoiceSnapshot Voice { get; init; }

	[JsonPropertyName("embedding")]
	public required EmbeddingSnapshot Embedding { get; init; }

	[JsonPropertyName("proactive")]
	public required ProactiveSnapshot Proactive { get; init; }

	[JsonPropertyName("skills")]
	public IReadOnlyList<SkillSnapshot> Skills { get; init; } = [];

	[JsonPropertyName("enabledSkillsCount")]
	public int EnabledSkillsCount { get; init; }

	[JsonPropertyName("tools")]
	public IReadOnlyList<ToolSnapshot> Tools { get; init; } = [];

	[JsonPropertyName("mcpServersCount")]
	public int? McpServersCount { get; init; }

	[JsonPropertyName("emotion")]
	public required EmotionSnapshot Emotion { get; init; }
}

public sealed record AppSnapshot
{
	[JsonPropertyName("appVersion")]
	public required string AppVersion { get; init; }

	[JsonPropertyName("platform")]
	public required string Platform { get; init; }
}

public sealed record GeneralSnapshot
{
	[JsonPropertyName("language")]
	public required string Language { get; init; }

	[JsonPropertyName("petAutoSummon")]
	public bool PetAutoSummon { get; init; }
}

public sealed record AiSnapshot
{
	[JsonPropertyName("configured")]
	public bool Configured { get; init; }

	[JsonPropertyName("provider")]
	public required string Provider { get; init; }

	[JsonPropertyName("baseUrl")]
	public required string BaseUrl { get; init; }

	[JsonPropertyName("model")]
	public required string Model { get; init; }

	[JsonPropertyName("persona")]
	public required string Persona { get; init; }

	[JsonPropertyName("hasApiKey")]
	public bool HasApiKey { get; init; }
}

public sealed record ModelsSnapshot
{
	[JsonPropertyName("selected")]
	public required string Selected { get; init; }

	[JsonPropertyName("items")]
	public IReadOnlyList<ModelSnapshot> Items { get; init; } = [];

	[JsonPropertyName("scale")]
	public double Scale { get; init; }

	[JsonPropertyName("expressions")]
	public IReadOnlyList<string> Expressions { get; init; } = [];
}

public sealed record ModelSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("installed")]
	public bool Installed { get; init; }
}

public sealed record BehaviorsSnapshot
{
	[JsonPropertyName("clickInteraction")]
	public bool ClickInteraction { get; init; }

	[JsonPropertyName("autoBlink")]
	public bool AutoBlink { get; init; }

	[JsonPropertyName("eyeTracking")]
	public bool EyeTracking { get; init; }

	[JsonPropertyName("idleEyeAnimation")]
	public bool IdleEyeAnimation { get; init; }

	[JsonPropertyName("idleAnimation")]
	public bool IdleAnimation { get; init; }

	[JsonPropertyName("expressionEnabled")]
	public bool ExpressionEnabled { get; init; }

	[JsonPropertyName("lipSync")]
	public bool LipSync { get; init; }

	[JsonPropertyName("shadow")]
	public bool Shadow { get; init; }

	[JsonPropertyName("beatSync")]
	public bool BeatSync { get; init; }

	[JsonPropertyName("renderScale")]
	public double RenderScale { get; init; }

	[JsonPropertyName("maxFps")]
	public int MaxFps { get; init; }
}

public sealed record VoiceSnapshot
{
	[JsonPropertyName("volume")]
	public double Volume { get; init; }

	[JsonPropertyName("ttsProvider")]
	public required string TtsProvider { get; init; }

	[JsonPropertyName("ttsBaseUrl")]
	public required string TtsBaseUrl { get; init; }

	[JsonPropertyName("hasTtsApiKey")]
	public bool HasTtsApiKey { get; init; }

	[JsonPropertyName("ttsVoice")]
	public required string TtsVoice { get; init; }

	[JsonPropertyName("ttsSpeed")]
	public double TtsSpeed { get; init; }

	[JsonPropertyName("ttsAutoPlay")]
	public bool TtsAutoPlay { get; init; }

	[JsonPropertyName("gptsovitsBaseUrl")]
	public required string GptsovitsBaseUrl { get; init; }

	[JsonPropertyName("gptsovitsRefAudio")]
	public required string GptsovitsRefAudio { get; init; }

	[JsonPropertyName("gptsovitsPromptText")]
	public required string GptsovitsPromptText { get; init; }

	[JsonPropertyName("gptsovitsPromptLang")]
	public required string GptsovitsPromptLang { get; init; }

	[JsonPropertyName("sttProvider")]
	public required string SttProvider { get; init; }

	[JsonPropertyName("sttBaseUrl")]
	public required string SttBaseUrl { get; init; }

	[JsonPropertyName("hasSttApiKey")]
	public bool HasSttApiKey { get; init; }

	[JsonPropertyName("noticePending")]
	public bool NoticePending { get; init; }

	[JsonPropertyName("speaking")]
	public bool Speaking { get; init; }
}

public sealed record EmbeddingSnapshot
{
	[JsonPropertyName("model")]
	public required string Model { get; init; }

	[JsonPropertyName("baseUrl")]
	public required string BaseUrl { get; init; }

	[JsonPropertyName("dimensions")]
	public required string Dimensions { get; init; }

	[JsonPropertyName("hasApiKey")]
	public bool HasApiKey { get; init; }
}

public sealed record ProactiveSnapshot
{
	[JsonPropertyName("idleEnabled")]
	public bool IdleEnabled { get; init; }

	[JsonPropertyName("idleMinutes")]
	public int IdleMinutes { get; init; }

	[JsonPropertyName("dailyGreeting")]
	public bool DailyGreeting { get; init; }

	[JsonPropertyName("reminders")]
	public IReadOnlyList<ReminderSnapshot> Reminders { get; init; } = [];
}

public sealed record ReminderSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("content")]
	public required string Content { get; init; }

	[JsonPropertyName("triggerTime")]
	public long TriggerTime { get; init; }
}

public sealed record SkillSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("description")]
	public required string Description { get; init; }

	[JsonPropertyName("author")]
	public required string Author { get; init; }

	[JsonPropertyName("version")]
	public required string Version { get; init; }

	[JsonPropertyName("icon")]
	public required string Icon { get; init; }

	[JsonPropertyName("tags")]
	public IReadOnlyList<string> Tags { get; init; } = [];

	[JsonPropertyName("category")]
	public required string Category { get; init; }

	[JsonPropertyName("instructions")]
	public string Instructions { get; init; } = "";

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; }

	[JsonPropertyName("source")]
	public required string Source { get; init; }
}

public sealed record ToolSnapshot
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("description")]
	public required string Description { get; init; }

	[JsonPropertyName("permissionLevel")]
	public required string PermissionLevel { get; init; }

	[JsonPropertyName("category")]
	public required string Category { get; init; }

	[JsonPropertyName("enabled")]
	public bool Enabled { get; init; }
}

public sealed record EmotionSnapshot
{
	[JsonPropertyName("type")]
	public required string Type { get; init; }
}

public sealed record LogSnapshot
{
	[JsonPropertyName("time")]
	public required string Time { get; init; }

	[JsonPropertyName("level")]
	public required string Level { get; init; }

	[JsonPropertyName("source")]
	public required string Source { get; init; }

	[JsonPropertyName("message")]
	public required string Message { get; init; }
}
