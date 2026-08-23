using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nori.Core.Tools;

/// <summary>
/// 工具边界限制。工具定义和工具结果都来自模型或外部扩展，不能把它们当作无限可信的内部数据。
/// </summary>
public static class ToolLimits
{
	/// <summary>工具名最大字符数</summary>
	public const int MaxNameCharacters = 128;

	/// <summary>工具描述最大字符数</summary>
	public const int MaxDescriptionCharacters = 2_000;

	/// <summary>单个参数 Schema 最大 JSON 字符数</summary>
	public const int MaxSchemaCharacters = 12_000;

	/// <summary>注入系统提示词的工具清单最大字符数</summary>
	public const int MaxToolsPromptCharacters = 48_000;

	/// <summary>一次工具调用参数最大 JSON 字符数</summary>
	public const int MaxArgumentsCharacters = 32_000;

	/// <summary>工具结果最大 JSON 字符数</summary>
	public const int MaxResultCharacters = 48_000;

	/// <summary>工具错误最大字符数</summary>
	public const int MaxErrorCharacters = 2_000;

	/// <summary>文本截断标记</summary>
	public const string TruncationMarker = "\n[内容已达到安全长度上限]";

	/// <summary>按字符上限截断文本。不会在字符串中间重复添加标记。</summary>
	public static string CapText(string? value, int limit)
	{
		if (string.IsNullOrEmpty(value) || limit <= 0) return "";
		if (value.Length <= limit) return value;
		if (limit <= TruncationMarker.Length) return value[..limit];
		return value[..(limit - TruncationMarker.Length)] + TruncationMarker;
	}

	/// <summary>将 Schema 限制为可安全注入 Prompt 的 JSON 对象。</summary>
	public static JsonObject CapSchema(JsonObject? schema)
	{
		if (schema is null) return new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
		try
		{
			if (schema.ToJsonString().Length <= MaxSchemaCharacters)
			{
				return schema.DeepClone().AsObject();
			}
		}
		catch (JsonException)
		{
			// 结构异常时按空对象 Schema 处理，不能把异常 JSON 拼进系统提示词。
		}

		return new JsonObject
		{
			["type"] = "object",
			["properties"] = new JsonObject(),
			["description"] = "参数 Schema 已达到安全长度上限，未注入完整定义。",
		};
	}

	/// <summary>获取 JSON 节点的序列化大小，序列化失败时返回超限值。</summary>
	public static int SerializedLength(JsonNode? value)
	{
		try
		{
			return (value?.ToJsonString() ?? "null").Length;
		}
		catch (JsonException)
		{
			return MaxArgumentsCharacters + 1;
		}
	}

	/// <summary>将工具返回值限制在可反馈给模型的上限内。</summary>
	public static object? CapResult(object? result)
	{
		string serialized;
		try
		{
			serialized = JsonSerializer.Serialize(result);
		}
		catch (Exception)
		{
			return "[工具结果无法序列化]";
		}

		if (serialized.Length <= MaxResultCharacters) return result;
		return CapText(serialized, MaxResultCharacters);
	}

	/// <summary>限制工具错误文本长度。</summary>
	public static string? CapError(string? error) => error is null ? null : CapText(error, MaxErrorCharacters);
}
