using System.Reflection;

namespace Nori.Core.Agent;

/// <summary>
/// 系统提示词构建
///
/// 组装顺序与前端 promptBuilder.ts 一致:
/// 人设 → 情绪 → 记忆 → 动作/表情 → 技能 → 工具清单 → 输出协议。
/// 基础人设使用嵌入资源 nori-system-prompt.md (与 ChatService 同源)。
/// </summary>
public static class PromptBuilder
{
	private const string PromptResource = "Nori.Core.Chat.nori-system-prompt.md";

	private static readonly Lazy<string> BasePersona = new(LoadBasePersona);

	/// <summary>协议输出规范说明</summary>
	private const string ProtocolInstruction = """"
		【核心通信协议要求】
		你与桌宠宿主系统的所有交互必须严格输出符合 Nori 协议的 JSON 格式：

		1. 普通回复：
		```json
		{
		  "type": "message",
		  "text": "回复内容",
		  "emotion": "happy",
		  "action": "动作名(可选)",
		  "expression": "表情名(可选)"
		}
		```

		2. 调用工具（当你需要查询时间、系统状态或执行特定动作时）：
		```json
		{
		  "type": "tool_call",
		  "id": "call_1",
		  "name": "工具名称",
		  "arguments": { "参数名": "参数值" }
		}
		```
		注意：每次调用工具后，系统会将工具执行结果返回给你，你可以在下一轮回复中根据结果输出友善的自然语言回答。
		"""";

	/// <summary>
	/// 构建系统提示词
	/// </summary>
	public static string Build(PromptBuildOptions options)
	{
		List<string> parts = [];

		// 1. 基础人设 (用户自定义优先, 否则使用嵌入人格文档)
		parts.Add(string.IsNullOrWhiteSpace(options.UserPersona) ? BasePersona.Value : options.UserPersona);

		// 2. 当前情绪状态
		if (!string.IsNullOrEmpty(options.Emotion))
		{
			parts.Add($"【当前情绪状态】：{options.Emotion}（请在回复时适当体现此情绪倾向）");
		}

		// 3. 关联记忆注入
		if (options.Memories is {Count: > 0})
		{
			parts.Add("【关于主人的长期记忆】：\n" + string.Join("\n", options.Memories.Select((m, i) => $"{i + 1}. {m}")));
		}

		// 4. 当前模型动作与表情提示
		if (options.AvailableMotions is {Count: > 0})
		{
			parts.Add($"【可用动作列表 (action)】：{string.Join(", ", options.AvailableMotions)}");
		}
		if (options.AvailableExpressions is {Count: > 0})
		{
			parts.Add($"【可用表情列表 (expression)】：{string.Join(", ", options.AvailableExpressions)}");
		}

		// 5. 注入已激活的技能扩展
		if (!string.IsNullOrEmpty(options.SkillsPrompt))
		{
			parts.Add(options.SkillsPrompt);
		}

		// 6. 工具清单定义
		parts.Add($"【可用工具列表】：\n{options.ToolsJson}");

		// 7. 输出格式规则
		parts.Add(ProtocolInstruction);

		return string.Join("\n\n", parts);
	}

	/// <summary>从嵌入资源读取基础人设</summary>
	private static string LoadBasePersona()
	{
		using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResource)
			?? throw new InvalidOperationException($"找不到嵌入资源: {PromptResource}");
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}
}

/// <summary>Prompt 构建选项</summary>
public sealed record PromptBuildOptions
{
	/// <summary>用户自定义人设 (空串使用默认)</summary>
	public string UserPersona { get; init; } = "";

	/// <summary>当前情绪类型</summary>
	public string? Emotion { get; init; }

	/// <summary>相关长期记忆内容</summary>
	public IReadOnlyList<string>? Memories { get; init; }

	/// <summary>当前模型可用动作列表</summary>
	public IReadOnlyList<string>? AvailableMotions { get; init; }

	/// <summary>当前模型可用表情列表</summary>
	public IReadOnlyList<string>? AvailableExpressions { get; init; }

	/// <summary>已激活技能注入文本</summary>
	public string SkillsPrompt { get; init; } = "";

	/// <summary>可用工具清单 JSON 文本</summary>
	public string ToolsJson { get; init; } = "[]";
}
