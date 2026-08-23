using System.Text.Json.Nodes;
using Nori.Core.Tools;

namespace Nori.Core.Chat;

/// <summary>可使用 provider 原生函数调用的 LLM 适配器。</summary>
public interface IToolCallingLlmAdapter
{
	Task<string> StreamWithToolsAsync(
		string baseUrl,
		string apiKey,
		string model,
		string systemPrompt,
		IReadOnlyList<ChatMessageInput> messages,
		IReadOnlyList<RegisteredTool> tools,
		Func<string, JsonNode?, Task<ToolResult>> executeTool,
		Action<string> onChunk,
		Action<LlmUsageInfo>? onUsage = null,
		CancellationToken cancellationToken = default);
}
