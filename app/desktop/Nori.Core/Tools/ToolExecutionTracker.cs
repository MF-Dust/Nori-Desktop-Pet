using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Nori.Core.Tools;

/// <summary>
/// 单次 Agent 会话的工具执行记录。
/// 同一个协议 call id 只允许产生一次副作用；没有 id 时使用名称和规范化参数作为保守去重键，
/// 防止流式重放或原生工具回退再次执行同一调用。
/// </summary>
public sealed class ToolExecutionTracker
{
	private readonly object _gate = new();
	private readonly Dictionary<string, ToolResult> _completed = new(StringComparer.Ordinal);
	private readonly HashSet<string> _started = new(StringComparer.Ordinal);

	/// <summary>是否已经开始过任意工具执行。</summary>
	public bool HasStarted
	{
		get
		{
			lock (_gate) return _started.Count > 0;
		}
	}

	/// <summary>按调用 ID / 名称参数生成稳定键。</summary>
	public static string Key(string? callId, string name, JsonNode? arguments)
	{
		if (!string.IsNullOrWhiteSpace(callId)) return $"id:{callId}";
		string json;
		try { json = arguments?.ToJsonString() ?? "null"; }
		catch (Exception) { json = "<invalid>"; }
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		return $"call:{name}:{Convert.ToHexString(hash)}";
	}

	/// <summary>尝试登记执行。重复调用返回 false，不应再次调用工具体。</summary>
	public bool TryStart(string key)
	{
		lock (_gate)
		{
			if (_started.Contains(key)) return false;
			_started.Add(key);
			return true;
		}
	}

	/// <summary>保存执行结果，后续重复调用可复用相同结果。</summary>
	public void Complete(string key, ToolResult result)
	{
		lock (_gate) _completed[key] = result;
	}

	/// <summary>获取已经完成的调用结果。</summary>
	public bool TryGetCompleted(string key, out ToolResult result)
	{
		lock (_gate) return _completed.TryGetValue(key, out result!);
	}
}
